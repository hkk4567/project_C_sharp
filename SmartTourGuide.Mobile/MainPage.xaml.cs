using AppResources = global::SmartTourGuide.Mobile.Resources.Strings.AppResources;

namespace SmartTourGuide.Mobile;

public partial class MainPage : ContentPage
{
    private readonly record struct LanguageOption(string Code, string DisplayName, string PickerLabel, string FlagEmoji);

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
        _currentLanguageCode = Preferences.Get("AppLanguage", "vi-VN");
        SetAppLanguage(_currentLanguageCode);

        InitializeComponent();
        UpdateLocalizedPoiDetailTexts();
        InitializePoiSearchUi();

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
                await RenderTourOnMap(tourDetail, isInitialLoad: true);
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
        RegisterDeepLinkHandler();
        await CheckPermissions();
        await PreWarmAudioAsync();

        if (!_isManualLocationOverride)
            await LoadCurrentLocation();

        await LoadPoisWithOfflineFallbackAsync();
        RegisterConnectivityChanged();

        // ── GPS real-time: thay thế GetLastKnownLocationAsync() polling ────
        await StartLocationListeningAsync();

        // ── Timer chỉ còn nhiệm vụ check geofence (GPS đã tách riêng) ──────
        if (_geofenceTimer == null)
        {
            _geofenceTimer = Application.Current!.Dispatcher.CreateTimer();
            _geofenceTimer.Interval = TimeSpan.FromSeconds(3);
            _geofenceTimer.Tick += (s, e) =>
            {
                // GPS cập nhật real-time qua OnLocationChanged.
                // Timer chỉ giữ lại check geofence + highlight làm dự phòng
                // (ví dụ khi thiết bị không bắn LocationChanged đủ nhanh)
                CheckGeofences();
                UpdateNearestPoiHighlight();
            };
            _geofenceTimer.Start();
        }

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
        UnregisterDeepLinkHandler();

        // Dừng GPS khi rời trang để tiết kiệm pin
        StopLocationListening();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  NGÔN NGỮ
    // ════════════════════════════════════════════════════════════════════════
    private void SetAppLanguage(string langCode)
    {
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
        if (lblPoiDetailTitle != null) lblPoiDetailTitle.Text = AppResources.PoiDetailTitle;
        if (lblDescriptionHeader != null) lblDescriptionHeader.Text = AppResources.PoiDescriptionLabel;
    }

    private async void OnChangeLanguageClicked(object? sender, EventArgs e)
    {
        StopAudio();
        string action = await DisplayActionSheetAsync(
            "Chọn ngôn ngữ / Select Language", "Hủy/Cancel", null,
            SupportedLanguages.Select(x => x.PickerLabel).ToArray());

        string selectedCode = _currentLanguageCode;
        var selected = SupportedLanguages.FirstOrDefault(x => x.PickerLabel == action);
        if (!string.IsNullOrWhiteSpace(selected.Code))
            selectedCode = selected.Code;

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
        var opt = SupportedLanguages.FirstOrDefault(x => x.Code == langCode);
        return string.IsNullOrWhiteSpace(opt.Code) ? SupportedLanguages[0] : opt;
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
                    _lastReportedLocation, currentLocation,
                    DistanceUnits.Kilometers) * 1000;

                if (elapsed < TimeSpan.FromSeconds(10) && distanceMeters < 25)
                    return;
            }

            if (!await _locationSendLock.WaitAsync(0)) return;
            try
            {
                _lastReportedLocation = currentLocation;
                _lastReportedLocationAt = DateTime.UtcNow;
                _ = _apiService.SendLocationAsync(latitude, longitude, _deviceId);
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
            var location = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(15)));

            if (location != null)
            {
                _currentUserLocation = location;
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
                    $"✅ GPS khởi tạo: {location.Latitude:F5}, {location.Longitude:F5}");
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ GPS lỗi lúc khởi tạo: {ex.Message}");
        }

        MoveMapToDefaultLocation(resolution: 2);
    }

    private async void OnCenterMyLocationClicked(object? sender, EventArgs e)
    {
        if (sender is View view)
        {
            await view.ScaleToAsync(0.9, 100, Easing.CubicOut);
            await view.ScaleToAsync(1.0, 100, Easing.CubicIn);
        }

        _isManualLocationOverride = false; // Thoát chế độ giả lập

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
        var layersToRemove = mapView.Map.Layers.Where(l => layerNames.Contains(l.Name)).ToList();
        foreach (var layer in layersToRemove)
        {
            mapView.Map.Layers.Remove(layer);
        }
    }

    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        TourInfoPanel.IsVisible = false;
        ClearMapLayers("TourRoute");
        _currentTour = null;
        _lastTourRenderLocation = null; // Reset ngưỡng re-render khi reload
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