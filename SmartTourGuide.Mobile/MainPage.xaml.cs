namespace SmartTourGuide.Mobile;

public partial class MainPage : ContentPage
{
    // ════════════════════════════════════════════════════════════════════════
    //  CONSTRUCTOR
    // ════════════════════════════════════════════════════════════════════════
    public MainPage()
    {
        _currentLanguageCode = Preferences.Get("AppLanguage", "vi-VN");
        SetAppLanguage(_currentLanguageCode);

        InitializeComponent();

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
        RegisterDeepLinkHandler();
        await CheckPermissions();
        await PreWarmAudioAsync();
        await LoadCurrentLocation();

        // ← SỬA: dùng hàm mới thay LoadPoisOnMap()
        await LoadPoisWithOfflineFallbackAsync();
        if (App.PendingDeepLinkPoiId.HasValue)
        {
            int targetPoiId = App.PendingDeepLinkPoiId.Value;
            bool autoPlay = App.PendingDeepLinkAutoPlay;

            // Xóa ID để không bị lặp lại khi chuyển qua lại các trang
            App.PendingDeepLinkPoiId = null;

            // Gọi hàm hiển thị Popup và phát nhạc
            await HandleDeepLinkPoiAsync(targetPoiId, autoPlay);
        }
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
                        _ = _apiService.SendLocationAsync(
                            location.Latitude,
                            location.Longitude,
                            _deviceId);
                    }
                }
                catch { }
            };
            _geofenceTimer.Start();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Hủy đăng ký Deep Link để tránh lỗi bộ nhớ (Memory Leak)
        UnregisterDeepLinkHandler();

        // Nếu muốn dừng hẳn nhạc khi thoát trang, có thể gọi thêm:
        // StopAudio();
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
        SmartTourGuide.Mobile.Resources.Strings.AppResources.Culture = culture;
    }

    private void UpdateLanguageButtonUI()
    {
        btnLanguage.Text = _currentLanguageCode == "en-US" ? "🇺🇸 English" : "🇻🇳 Tiếng Việt";
    }

    private async void OnChangeLanguageClicked(object? sender, EventArgs e)
    {
        StopAudio();
        string action = await DisplayActionSheetAsync(
            "Chọn ngôn ngữ / Select Language", "Hủy/Cancel", null,
            "🇻🇳 Tiếng Việt", "🇺🇸 English");

        string selectedCode = _currentLanguageCode;
        if (action == "🇻🇳 Tiếng Việt") selectedCode = "vi-VN";
        else if (action == "🇺🇸 English") selectedCode = "en-US";

        if (selectedCode != _currentLanguageCode &&
            action != "Hủy/Cancel" && !string.IsNullOrEmpty(action))
        {
            _currentLanguageCode = selectedCode;
            Preferences.Set("AppLanguage", _currentLanguageCode);
            SetAppLanguage(_currentLanguageCode);

            if (Application.Current?.Windows.Count > 0)
                Application.Current.Windows[0].Page = new MainPage();
            else if (this.Window != null)
                this.Window.Page = new MainPage();
        }
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