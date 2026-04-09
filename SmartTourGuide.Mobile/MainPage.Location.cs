namespace SmartTourGuide.Mobile;

/// <summary>
/// Quản lý toàn bộ logic GPS real-time.
///
/// Luồng hoạt động:
///   OnAppearing → StartLocationListeningAsync()
///     → Geolocation.LocationChanged event kích hoạt mỗi khi di chuyển
///       → Cập nhật dot vị trí trên bản đồ
///       → Gửi lên server (nếu đủ điều kiện)
///       → Kiểm tra geofence + highlight POI gần nhất
///       → Nếu đang xem Tour và đã đi xa ≥ TourRerouteThresholdMeters
///         → Vẽ lại tuyến đường từ vị trí mới (dùng OSRM cache → rất nhanh)
///   OnDisappearing → StopLocationListening()
/// </summary>
public partial class MainPage
{
    // ── Ngưỡng di chuyển để vẽ lại tuyến tour (tránh re-render liên tục) ───
    private const double TourRerouteThresholdMeters = 15.0;
    private const int timerefresh = 3;

    // ── Trạng thái listener ──────────────────────────────────────────────────
    private bool _isLocationListening = false;

    // ── Vị trí dùng lần cuối để vẽ tuyến tour (dùng để tính ngưỡng) ─────────
    private MauiLocation.Location? _lastTourRenderLocation = null;

    // ════════════════════════════════════════════════════════════════════════
    //  BẮT ĐẦU / DỪNG LẮNG NGHE GPS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Đăng ký lắng nghe GPS real-time.
    /// Gọi trong OnAppearing.
    /// </summary>
    public async Task StartLocationListeningAsync()
    {
        if (_isLocationListening) return;

        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                System.Diagnostics.Debug.WriteLine("[Location] Chưa có quyền GPS.");
                return;
            }

            var request = new GeolocationListeningRequest(
                GeolocationAccuracy.Best,
                minimumTime: TimeSpan.FromSeconds(timerefresh)); // cập nhật tối thiểu mỗi 1 giây

            var started = await Geolocation.StartListeningForegroundAsync(request);
            if (!started)
            {
                System.Diagnostics.Debug.WriteLine("[Location] Không thể bắt đầu lắng nghe GPS.");
                return;
            }

            Geolocation.LocationChanged += OnLocationChanged;
            Geolocation.ListeningFailed += OnListeningFailed;

            _isLocationListening = true;
            System.Diagnostics.Debug.WriteLine("[Location] Bắt đầu lắng nghe GPS real-time ✅");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Location] StartListening lỗi: {ex.Message}");
        }
    }

    /// <summary>
    /// Dừng lắng nghe GPS.
    /// Gọi trong OnDisappearing.
    /// </summary>
    public void StopLocationListening()
    {
        if (!_isLocationListening) return;

        Geolocation.LocationChanged -= OnLocationChanged;
        Geolocation.ListeningFailed -= OnListeningFailed;
        Geolocation.StopListeningForeground();

        _isLocationListening = false;
        System.Diagnostics.Debug.WriteLine("[Location] Dừng lắng nghe GPS.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  XỬ LÝ KHI GPS CẬP NHẬT
    // ════════════════════════════════════════════════════════════════════════

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        // Bỏ qua nếu user đang giả lập vị trí thủ công bằng cách tap lên bản đồ
        if (_isManualLocationOverride) return;

        var loc = e.Location;
        if (loc == null) return;

        System.Diagnostics.Debug.WriteLine(
            $"[Location] GPS: {loc.Latitude:F5}, {loc.Longitude:F5} " +
            $"±{loc.Accuracy:F0}m");

        _currentUserLocation = loc;

        // ── 1. Cập nhật dot vị trí trên bản đồ ──────────────────────────
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var mapView = MapViewCtrl;
            if (mapView == null) return;

            mapView.MyLocationLayer.UpdateMyLocation(
                new Mapsui.UI.Maui.Position(loc.Latitude, loc.Longitude));
            mapView.RefreshGraphics();
        });

        // ── 2. Gửi vị trí lên server (throttle trong SendLocationIfNeededAsync) ──
        _ = SendLocationIfNeededAsync(loc.Latitude, loc.Longitude);

        // ── 3. Kiểm tra geofence + highlight POI gần nhất ───────────────
        UpdateNearestPoiHighlight();
        if (!_isCheckingGeofences)
            CheckGeofences();

        // ── 4. Nếu đang xem Tour → kiểm tra xem có cần vẽ lại route không ──
        if (_currentTour != null)
            _ = MaybeRerenderTourRouteAsync(loc);
    }

    private void OnListeningFailed(object? sender, GeolocationListeningFailedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[Location] Listening thất bại: {e.Error}");
        _isLocationListening = false;

        // Thử khởi động lại sau 5 giây
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            await StartLocationListeningAsync();
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VẼ LẠI TUYẾN TOUR KHI USER ĐI XA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Chỉ vẽ lại tuyến đường Tour nếu user đã di chuyển ≥ TourRerouteThresholdMeters
    /// so với lần vẽ gần nhất.
    ///
    /// Vì OSRM route đã được cache theo POI, việc re-render chỉ cần đọc cache
    /// (không tốn băng thông) → đủ nhanh để gọi mỗi vài chục mét.
    /// </summary>
    private async Task MaybeRerenderTourRouteAsync(MauiLocation.Location newLocation)
    {
        if (_currentTour == null) return;

        // Tính khoảng cách từ lần vẽ cuối
        if (_lastTourRenderLocation != null)
        {
            double movedMeters = MauiLocation.Location.CalculateDistance(
                _lastTourRenderLocation, newLocation,
                DistanceUnits.Kilometers) * 1000;
            System.Diagnostics.Debug.WriteLine($"[Khoảng cách click]: {movedMeters} mét");
            if (movedMeters < TourRerouteThresholdMeters)
                return; // Chưa đủ xa → bỏ qua
        }

        // Lưu lại vị trí render hiện tại trước để tránh re-enter
        _lastTourRenderLocation = newLocation;

        System.Diagnostics.Debug.WriteLine(
            $"[Location] User đã di chuyển, vẽ lại tuyến tour từ vị trí mới.");

        // RenderTourOnMap sẽ tự đọc từ OSRM cache (không gọi API mạng)
        // nên chạy rất nhanh dù được gọi thường xuyên
        await RenderTourOnMap(_currentTour, isInitialLoad: false);
    }
}
