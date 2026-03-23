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
        await CheckPermissions();
        await PreWarmAudioAsync();
        await LoadCurrentLocation();

        // ← SỬA: dùng hàm mới thay LoadPoisOnMap()
        await LoadPoisWithOfflineFallbackAsync();

        // ← THÊM: lắng nghe thay đổi mạng
        RegisterConnectivityChanged();

        if (_geofenceTimer == null)
        {
            _geofenceTimer = Application.Current!.Dispatcher.CreateTimer();
            _geofenceTimer.Interval = TimeSpan.FromSeconds(3);
            _geofenceTimer.Tick += (s, e) =>
            {
                CheckGeofences();
                UpdateNearestPoiHighlight();
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
        MoveMapToDefaultLocation(resolution: 2);
        await Task.CompletedTask;
    }

    private async void OnCenterMyLocationClicked(object? sender, EventArgs e)
    {
        if (sender is View view)
        {
            await view.ScaleToAsync(0.9, 100, Easing.CubicOut);
            await view.ScaleToAsync(1, 100, Easing.CubicIn);
        }
        MoveMapToDefaultLocation(resolution: 1.5);
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
        // ── THÊM: đóng tour panel nếu đang mở ────────────────────────
        if (TourInfoPanel.IsVisible)
        {
            TourInfoPanel.IsVisible = false;
            _currentTour = null;
        }
        // ─────────────────────────────────────────────────────────────

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
    private static int DecodePolylineChunk(string encoded, ref int index)
    {
        int result = 0;
        int shift = 0;
        int b;
        do
        {
            b = encoded[index++] - 63;
            result |= (b & 0x1F) << shift;
            shift += 5;
        } while (b >= 0x20);

        return (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
    }
}