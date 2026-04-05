using AppResources = global::SmartTourGuide.Mobile.Resources.Strings.AppResources;

namespace SmartTourGuide.Mobile;

public partial class MainPage : ContentPage
{
    private readonly record struct LanguageOption(string Code, string DisplayName, string PickerLabel, string FlagEmoji);

    private static readonly IReadOnlyList<LanguageOption> SupportedLanguages = new[]
    {
        new LanguageOption("vi-VN", "Tiếng Việt", "🇻🇳 Tiếng Việt", "🇻🇳"),
        new LanguageOption("en-US", "English", "🇺🇸 English", "🇺🇸"),
        new LanguageOption("zh-CN", "中文", "🇨🇳 中文", "🇨🇳"),
        new LanguageOption("ja-JP", "日本語", "🇯🇵 日本語", "🇯🇵"),
        new LanguageOption("fr-FR", "Français", "🇫🇷 Français", "🇫🇷"),
        new LanguageOption("ko-KR", "한국어", "🇰🇷 한국어", "🇰🇷"),
    };

    // ════════════════════════════════════════════════════════════════════════
    //  CONSTRUCTOR
    // ════════════════════════════════════════════════════════════════════════
    public MainPage()
    {
        _currentLanguageCode = Preferences.Get("AppLanguage", "vi-VN");
        SetAppLanguage(_currentLanguageCode);

        InitializeComponent();
        UpdateLocalizedPoiDetailTexts();
        InitializePoiSearchUi();

        // Tạm dừng / tiếp tục audio khi có cuộc gọi hoặc app vào background
        WeakReferenceMessenger.Default.Register<AppSleepMessage>(this,
            (r, m) => PauseForInterruption());
        WeakReferenceMessenger.Default.Register<AppResumeMessage>(this,
            (r, m) => ResumeFromInterruption());

        WeakReferenceMessenger.Default.Register<SelectTourMessage>(this, (r, m) =>
        {
            var tourDetail = m.Value;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(500);
                await RenderTourOnMap(tourDetail);
            });
        });

        _apiService = new PoiApiService();
        UpdateLanguageButtonUI();

        var map = new Mapsui.Map();
        map.Layers.Add(OpenStreetMap.CreateTileLayer());

        _geofenceLayer = new MemoryLayer { Name = "Geofences", Style = null };
        map.Layers.Add(_geofenceLayer);

        mapView.Map = map;
        Mapsui.Logging.Logger.LogDelegate = (level, message, ex) =>
            System.Diagnostics.Debug.WriteLine($"[Mapsui] {message}");

        mapView.PinClicked += OnPinClicked;
        mapView.MapClicked += OnMapClicked_SimulateWalk;
        mapView.MyLocationLayer.Enabled = true;
        RegisterConnectivityChanged();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VÒNG ĐỜI TRANG
    // ════════════════════════════════════════════════════════════════════════
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CheckPermissions();
        await PreWarmAudioAsync();

        // Giữ nguyên vị trí A nếu user đã chọn thủ công, tránh bị kéo về vị trí mặc định
        if (!_isManualLocationOverride)
            await LoadCurrentLocation();

        // ← SỬA: dùng hàm mới thay LoadPoisOnMap()
        await LoadPoisWithOfflineFallbackAsync();

        // ← THÊM: lắng nghe thay đổi mạng
        RegisterConnectivityChanged();

        if (_geofenceTimer == null)
        {
            _geofenceTimer = Application.Current!.Dispatcher.CreateTimer();
            _geofenceTimer.Interval = TimeSpan.FromSeconds(3);
            _geofenceTimer.Tick += async (s, e) =>
            {
                CheckGeofences();
                UpdateNearestPoiHighlight();

                // Ghi GPS thật lên server (luôn chạy dù có tour hay không)
                try
                {
                    if (_isManualLocationOverride)
                        return;

                    var location = await Geolocation.GetLastKnownLocationAsync();
                    if (location != null)
                    {
                        _currentUserLocation = location;
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            mapView.MyLocationLayer.UpdateMyLocation(
                                new Mapsui.UI.Maui.Position(
                                    location.Latitude,
                                    location.Longitude));
                        });
                        _ = SendLocationIfNeededAsync(location.Latitude, location.Longitude);
                    }
                }
                catch { }
            };
            _geofenceTimer.Start();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  NGÔN NGỮ
    // ════════════════════════════════════════════════════════════════════════
    private void SetAppLanguage(string langCode)
    {
        var culture = new CultureInfo(langCode);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        AppResources.Culture = culture;
    }

    private void UpdateLanguageButtonUI()
    {
        btnLanguage.Text = GetLanguageOption(_currentLanguageCode).PickerLabel;
    }

    private void UpdateLocalizedPoiDetailTexts()
    {
        if (lblPoiDetailTitle != null)
            lblPoiDetailTitle.Text = AppResources.PoiDetailTitle;

        if (lblDescriptionHeader != null)
            lblDescriptionHeader.Text = AppResources.PoiDescriptionLabel;
    }

    private async void OnChangeLanguageClicked(object? sender, EventArgs e)
    {
        StopAudio();
        string action = await DisplayActionSheetAsync(
            "Chọn ngôn ngữ / Select Language", "Hủy/Cancel", null,
            SupportedLanguages.Select(x => x.PickerLabel).ToArray());

        string selectedCode = _currentLanguageCode;
        var selectedLanguage = SupportedLanguages.FirstOrDefault(x => x.PickerLabel == action);
        if (!string.IsNullOrWhiteSpace(selectedLanguage.Code))
        {
            selectedCode = selectedLanguage.Code;
        }

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
        var option = SupportedLanguages.FirstOrDefault(x => x.Code == langCode);
        return string.IsNullOrWhiteSpace(option.Code) ? SupportedLanguages[0] : option;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BẢN ĐỒ & POI
    // ════════════════════════════════════════════════════════════════════════
    private async Task CheckPermissions()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
    }

    private void MoveMapToDefaultLocation(double resolution = 2)
    {
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
            var currentLocation = new MauiLocation.Location(latitude, longitude);

            if (_lastReportedLocation != null)
            {
                var elapsed = DateTime.UtcNow - _lastReportedLocationAt;
                var distanceMeters = MauiLocation.Location.CalculateDistance(
                    _lastReportedLocation,
                    currentLocation,
                    DistanceUnits.Kilometers) * 1000;

                // Chặn bắn log liên tục nếu vị trí gần như không đổi
                if (elapsed < TimeSpan.FromSeconds(10) && distanceMeters < 25)
                    return;
            }

            if (!await _locationSendLock.WaitAsync(0))
                return;

            try
            {
                _lastReportedLocation = currentLocation;
                _lastReportedLocationAt = DateTime.UtcNow;

                _ = _apiService.SendLocationAsync(latitude, longitude, _deviceId);
            }
            finally
            {
                _locationSendLock.Release();
            }
        }
        catch
        {
            // Không ảnh hưởng luồng UI nếu log lỗi
        }
    }

    private async Task LoadCurrentLocation()
    {
        try
        {
            // Lấy vị trí GPS thực tế (chờ tối đa 15 giây để emulator kịp lock)
            var location = await Geolocation.GetLocationAsync(
                new GeolocationRequest(
                    GeolocationAccuracy.Best,
                    TimeSpan.FromSeconds(15)));

            if (location != null)
            {
                _currentUserLocation = location;

                // Cập nhật bản đồ vào vị trí user
                var smc = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
                var mPoint = new MPoint(smc.x, smc.y);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    mapView.MyLocationLayer.UpdateMyLocation(
                        new Mapsui.UI.Maui.Position(location.Latitude, location.Longitude));
                    mapView.Map?.Navigator.CenterOnAndZoomTo(mPoint, 1.5, duration: 500);
                });

                await SendLocationIfNeededAsync(location.Latitude, location.Longitude);

                System.Diagnostics.Debug.WriteLine(
                    $"✅ GPS: {location.Latitude}, {location.Longitude}");
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ GPS lỗi: {ex.Message}");
        }

        // Fallback nếu GPS lỗi
        MoveMapToDefaultLocation(resolution: 2);
    }

    private async void OnCenterMyLocationClicked(object? sender, EventArgs e)
    {
        if (sender is View view)
        {
            await view.ScaleToAsync(0.9, 100, Easing.CubicOut);
            await view.ScaleToAsync(1, 100, Easing.CubicIn);
        }

        var current = _currentUserLocation;
        var smc = SphericalMercator.FromLonLat(current.Longitude, current.Latitude);
        var mPoint = new MPoint(smc.x, smc.y);

        _isManualLocationOverride = false;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            mapView.MyLocationLayer.UpdateMyLocation(
                new Mapsui.UI.Maui.Position(current.Latitude, current.Longitude));
            mapView.Map?.Navigator.CenterOnAndZoomTo(mPoint, 1.5, duration: 500);
        });
    }
    private void ClearMapLayers(params string[] layerNames)
    {
        foreach (var name in layerNames)
        {
            var layer = mapView.Map.Layers.FirstOrDefault(l => l.Name == name);
            if (layer != null) mapView.Map.Layers.Remove(layer);
        }
    }
    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        // ✅ FIX: luôn reset tour bất kể panel có visible hay không
        TourInfoPanel.IsVisible = false;
        ClearMapLayers("TourRoute");
        _currentTour = null;

        await LoadPoisWithOfflineFallbackAsync();
    }

    private void OnPinClicked(object? sender, PinClickedEventArgs e)
    {
        if (e.Pin?.Tag is PoiModel poi) { ShowPoiDetail(poi); e.Handled = true; }
    }

    private void OnClosePopupClicked(object? sender, EventArgs e)
    {
        StopAudio();
        DetailPopup.IsVisible = false;
    }

    private async void OnShowToursClicked(object? sender, EventArgs e)
        => await Navigation.PushModalAsync(new ToursPage(this));
}