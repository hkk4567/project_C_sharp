using AppResources = global::SmartTourGuide.Mobile.Resources.Strings.AppResources;

namespace SmartTourGuide.Mobile;

// Mục đích file:
// - Điều phối khởi tạo MainPage và gắn các event chính.
// - Quản lý vòng đời trang (OnAppearing/OnDisappearing).
// - Xử lý ngôn ngữ, quyền vị trí, GPS khởi tạo và các thao tác map cơ bản.
// - Liên kết giữa UI MainPage.xaml và các module partial khác (Audio/Map/Offline/Search/Geofence).
public partial class MainPage : ContentPage
{
    // Mỗi ngôn ngữ gồm mã, tên hiển thị, nhãn picker và emoji cờ.
    private readonly record struct LanguageOption(string Code, string DisplayName, string PickerLabel, string FlagEmoji);

    // Danh sách ngôn ngữ được hỗ trợ trên giao diện.
    private static readonly IReadOnlyList<LanguageOption> SupportedLanguages = new[]
    {
        new LanguageOption("vi-VN", "Tiếng Việt", "🇻🇳 Tiếng Việt", "🇻🇳"),
        new LanguageOption("en-US", "English",    "🇺🇸 English",     "🇺🇸"),
        new LanguageOption("zh-CN", "中文",        "🇨🇳 中文",        "🇨🇳"),
        new LanguageOption("ja-JP", "日本語",      "🇯🇵 日本語",      "🇯🇵"),
        new LanguageOption("fr-FR", "Français",   "🇫🇷 Français",    "🇫🇷"),
        new LanguageOption("ko-KR", "한국어",      "🇰🇷 한국어",      "🇰🇷"),
    };

    // ════════════════════════════════════════════════════════════════════════
    //  CONSTRUCTOR
    // ════════════════════════════════════════════════════════════════════════
    public MainPage()
    {
        // 1) Khôi phục ngôn ngữ đã lưu và áp dụng cho tài nguyên hiện tại.
        _currentLanguageCode = Preferences.Get("AppLanguage", "vi-VN");
        SetAppLanguage(_currentLanguageCode);

        // 2) Khởi tạo XAML và setup các phần UI phụ thuộc ngôn ngữ.
        InitializeComponent();
        UpdateLocalizedPoiDetailTexts();
        InitializePoiSearchUi();

        // 3) Đăng ký message cho vòng đời app (sleep/resume).
        WeakReferenceMessenger.Default.Register<AppSleepMessage>(this,
            (r, m) => PauseForInterruption());
        WeakReferenceMessenger.Default.Register<AppResumeMessage>(this,
            (r, m) => ResumeFromInterruption());

        // 4) Nhận message chọn tour và render tour trên map.
        WeakReferenceMessenger.Default.Register<SelectTourMessage>(this, (r, m) =>
        {
            var tourDetail = m.Value;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(500);
                await RenderTourOnMap(tourDetail, isInitialLoad: true);
            });
        });

        // 5) Khởi tạo service và đồng bộ trạng thái nút ngôn ngữ.
        _apiService = new PoiApiService();
        UpdateLanguageButtonUI();

        // 6) Khởi tạo bản đồ nền OSM và layer geofence mặc định.
        var map = new Mapsui.Map();
        map.Layers.Add(OpenStreetMap.CreateTileLayer());

        _geofenceLayer = new MemoryLayer { Name = "Geofences", Style = null };
        map.Layers.Add(_geofenceLayer);

        // 7) Gắn map vào control và đăng ký các sự kiện tương tác map.
        mapView.Map = map;
        Mapsui.Logging.Logger.LogDelegate = (level, message, ex) =>
            System.Diagnostics.Debug.WriteLine($"[Mapsui] {message}");

        mapView.PinClicked += OnPinClicked;
        mapView.MapClicked += OnMapClicked_SimulateWalk;
        mapView.MyLocationLayer.Enabled = true;

        // 8) Theo dõi thay đổi kết nối để bật/tắt offline mode.
        RegisterConnectivityChanged();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VÒNG ĐỜI TRANG
    // ════════════════════════════════════════════════════════════════════════
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 1) Kích hoạt luồng deep link và đảm bảo đủ quyền cần thiết.
        RegisterDeepLinkHandler();
        await CheckPermissions();
        await PreWarmAudioAsync();

        // 2) Lấy vị trí ban đầu nếu không ở chế độ giả lập.
        if (!_isManualLocationOverride)
            await LoadCurrentLocation();

        // 3) Tải POI theo chiến lược online/offline.
        await LoadPoisWithOfflineFallbackAsync();
        RegisterConnectivityChanged();

        // ── GPS real-time: thay thế GetLastKnownLocationAsync() polling ────
        await StartLocationListeningAsync();

        // ── Timer 3 giây: geofence + highlight dự phòng ───────────────────
        if (_geofenceTimer == null)
        {
            _geofenceTimer = Application.Current!.Dispatcher.CreateTimer();
            _geofenceTimer.Interval = TimeSpan.FromSeconds(3);
            _geofenceTimer.Tick += (s, e) =>
            {
                CheckGeofences();
                UpdateNearestPoiHighlight();
            };
        }

        if (!_geofenceTimer.IsRunning)
            _geofenceTimer.Start();

        // 4) Xử lý deep link chờ sẵn (cold start).
        if (App.PendingDeepLinkPoiId.HasValue)
        {
            int poiId = App.PendingDeepLinkPoiId.Value;
            bool autoPlay = App.PendingDeepLinkAutoPlay;
            App.PendingDeepLinkPoiId = null;
            await Task.Delay(500);
            await HandleDeepLinkPoiAsync(poiId, autoPlay);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Hủy đăng ký deep link khi rời trang.
        UnregisterDeepLinkHandler();

        // Dừng GPS khi rời trang để tiết kiệm pin
        StopLocationListening();

        // Dừng heartbeat timer khi rời trang.
        _geofenceTimer?.Stop();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  NGÔN NGỮ
    // ════════════════════════════════════════════════════════════════════════
    private void SetAppLanguage(string langCode)
    {
        // Áp dụng culture cho thread hiện tại và mặc định toàn app.
        var culture = new System.Globalization.CultureInfo(langCode);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        AppResources.Culture = culture;
    }

    private void UpdateLanguageButtonUI()
        => btnLanguage.Text = GetLanguageOption(_currentLanguageCode).PickerLabel;

    private void UpdateLocalizedPoiDetailTexts()
    {
        // Đồng bộ text tĩnh trong popup POI theo resource hiện tại.
        if (lblPoiDetailTitle != null) lblPoiDetailTitle.Text = AppResources.PoiDetailTitle;
        if (lblDescriptionHeader != null) lblDescriptionHeader.Text = AppResources.PoiDescriptionLabel;
    }

    private async void OnChangeLanguageClicked(object? sender, EventArgs e)
    {
        // 1) Dừng audio hiện tại trước khi đổi ngôn ngữ để tránh trạng thái lỡ dở.
        StopAudio();

        // 2) Mở action sheet để người dùng chọn ngôn ngữ.
        string action = await DisplayActionSheetAsync(
            "Chọn ngôn ngữ / Select Language", "Hủy/Cancel", null,
            SupportedLanguages.Select(x => x.PickerLabel).ToArray());

        // 3) Xác định mã ngôn ngữ được chọn.
        string selectedCode = _currentLanguageCode;
        var selected = SupportedLanguages.FirstOrDefault(x => x.PickerLabel == action);
        if (!string.IsNullOrWhiteSpace(selected.Code))
            selectedCode = selected.Code;

        // 4) Nếu có thay đổi thật thì lưu setting và khởi tạo lại MainPage.
        if (selectedCode != _currentLanguageCode &&
            action != "Hủy/Cancel" && !string.IsNullOrEmpty(action))
        {
            _currentLanguageCode = selectedCode;
            Preferences.Set("AppLanguage", _currentLanguageCode);
            SetAppLanguage(_currentLanguageCode);
            UpdateLocalizedPoiDetailTexts();
            UpdateLanguageButtonUI();

            if (Application.Current?.Windows.Count > 0)
                Application.Current.Windows[0].Page = new MainPage();
            else if (this.Window != null)
                this.Window.Page = new MainPage();
        }
    }

    private static LanguageOption GetLanguageOption(string langCode)
    {
        // Trả về ngôn ngữ tương ứng, fallback về phần tử đầu nếu không tìm thấy.
        var opt = SupportedLanguages.FirstOrDefault(x => x.Code == langCode);
        return string.IsNullOrWhiteSpace(opt.Code) ? SupportedLanguages[0] : opt;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BẢN ĐỒ & POI
    // ════════════════════════════════════════════════════════════════════════
    private async Task CheckPermissions()
    {
        // Kiểm tra quyền vị trí và yêu cầu nếu chưa được cấp.
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
    }

    private void MoveMapToDefaultLocation(double resolution = 2)
    {
        // Fallback camera khi chưa lấy được GPS: đưa về tọa độ mặc định.
        var smc = SphericalMercator.FromLonLat(DefaultLon, DefaultLat);
        var mPoint = new MPoint(smc.x, smc.y);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            mapView.MyLocationLayer.UpdateMyLocation(
                new Mapsui.UI.Maui.Position(DefaultLat, DefaultLon));
            mapView.Map?.Navigator.CenterOnAndZoomTo(mPoint, resolution, duration: 500);
        });
    }

    private async Task SendLocationIfNeededAsync(double latitude, double longitude)
    {
        try
        {
            // Gửi tracking cho mọi lần gọi (listener đang chạy theo chu kỳ 3 giây).
            // Dùng lock có chờ để không bỏ sót lần gửi khi request trước chưa xong.
            await _locationSendLock.WaitAsync();
            try
            {
                await _apiService.SendLocationAsync(latitude, longitude, _deviceId);
            }
            finally { _locationSendLock.Release(); }
        }
        catch { }
    }

    /// <summary>
    /// Lấy vị trí GPS lần đầu (khi app mở) để center bản đồ.
    /// Sau đó vị trí sẽ được cập nhật real-time qua OnLocationChanged.
    /// </summary>
    private async Task LoadCurrentLocation()
    {
        try
        {
            // 1) Lấy GPS tại thời điểm mở app để center lần đầu.
            var location = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(15)));

            if (location != null)
            {
                // 2) Cập nhật vị trí cục bộ và camera map.
                _currentUserLocation = location;
                _hasGpsFix = true;
                var smc = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
                var mPoint = new MPoint(smc.x, smc.y);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    mapView.MyLocationLayer.UpdateMyLocation(
                        new Mapsui.UI.Maui.Position(location.Latitude, location.Longitude));
                    mapView.Map?.Navigator.CenterOnAndZoomTo(mPoint, 1.5, duration: 500);
                });

                // 3) Gửi vị trí đầu tiên lên server.
                await SendLocationIfNeededAsync(location.Latitude, location.Longitude);
                System.Diagnostics.Debug.WriteLine(
                    $"✅ GPS khởi tạo: {location.Latitude:F5}, {location.Longitude:F5}");
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ GPS lỗi lúc khởi tạo: {ex.Message}");
        }

        // 4) Nếu thất bại thì quay về tọa độ mặc định.
        MoveMapToDefaultLocation(resolution: 2);
    }

    private async void OnCenterMyLocationClicked(object? sender, EventArgs e)
    {
        // 1) Hiệu ứng nhấn nút ngắn để phản hồi thao tác.
        if (sender is View view)
        {
            await view.ScaleToAsync(0.9, 100, Easing.CubicOut);
            await view.ScaleToAsync(1.0, 100, Easing.CubicIn);
        }

        _isManualLocationOverride = false; // Thoát chế độ giả lập

        // 2) Center bản đồ về vị trí hiện tại của người dùng.
        var current = _currentUserLocation;
        var smc = SphericalMercator.FromLonLat(current.Longitude, current.Latitude);
        var mPoint = new MPoint(smc.x, smc.y);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            mapView.MyLocationLayer.UpdateMyLocation(
                new Mapsui.UI.Maui.Position(current.Latitude, current.Longitude));
            mapView.Map?.Navigator.CenterOnAndZoomTo(mPoint, 1.5, duration: 500);
        });
    }

    private void ClearMapLayers(params string[] layerNames)
    {
        // Xóa các layer theo tên để tránh trùng lớp khi vẽ lại.
        var layersToRemove = mapView.Map.Layers.Where(l => layerNames.Contains(l.Name)).ToList();
        foreach (var layer in layersToRemove)
        {
            mapView.Map.Layers.Remove(layer);
        }
    }

    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        // Reset chế độ tour và tải lại dữ liệu POI theo online/offline hiện tại.
        TourInfoPanel.IsVisible = false;
        ClearMapLayers("TourRoute");
        _currentTour = null;
        _lastTourRenderLocation = null; // Reset ngưỡng re-render khi reload
        await LoadPoisWithOfflineFallbackAsync();
    }

    private void OnPinClicked(object? sender, PinClickedEventArgs e)
    {
        // Click pin POI -> mở popup chi tiết.
        if (e.Pin?.Tag is PoiModel poi) { ShowPoiDetail(poi); e.Handled = true; }
    }

    private void OnClosePopupClicked(object? sender, EventArgs e)
    {
        // Đóng popup chi tiết và dừng audio đang phát (nếu có).
        StopAudio();
        DetailPopup.IsVisible = false;
    }

    private async void OnShowToursClicked(object? sender, EventArgs e)
        // Mở trang danh sách tour ở dạng modal.
        => await Navigation.PushModalAsync(new ToursPage(this));
}