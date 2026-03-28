namespace SmartTourGuide.Mobile;

public partial class MainPage
{
    private Label? StatusLabelCtrl => this.FindByName<Label>("statusLabel");

    // CheckGeofences, TriggerAutoAudio
    // ════════════════════════════════════════════════════════════════════════
    //  GEOFENCE ENGINE
    // ════════════════════════════════════════════════════════════════════════
    private async void CheckGeofences()
    {
        try
        {
            // Đang xem tour → không trigger geofence tự động
            if (_currentTour != null) return;
            if (_isCheckingGeofences || _allPoisCache.Count == 0) return;
            _isCheckingGeofences = true;

            var now = DateTime.UtcNow;

            var poisInRange = new List<PoiModel>();
            foreach (var poi in _allPoisCache)
            {
                var poiLoc = new MauiLocation.Location(poi.Latitude, poi.Longitude);
                double dist = MauiLocation.Location.CalculateDistance(
                    _currentUserLocation, poiLoc, DistanceUnits.Kilometers) * 1000;
                double radius = poi.TriggerRadius > 0 ? poi.TriggerRadius : 50;
                if (dist <= radius) poisInRange.Add(poi);
            }

            if (poisInRange.Count > 0)
            {
                _lastGeofenceInsideAt = now;
            }

            // Kịch bản A: Ra khỏi tất cả vùng
            if (poisInRange.Count == 0)
            {
                if (_currentlyPlayingGeofencePoi != null)
                {
                    var leftPoiName = _currentlyPlayingGeofencePoi.Name;
                    StopAudio();
                    _currentlyPlayingGeofencePoi = null;
                    SetStatus($"👋 Rời vùng: {leftPoiName}", priority: 2, autoRevertMs: 2000);
                }
                return;
            }

            // Kịch bản C: Ra khỏi vùng đang phát, còn vùng khác
            // FIX #2 & #3: Khi chuyển POI 1 → POI 2, KHÔNG để cooldown POI cũ chặn POI mới.
            // Reset _lastGeofenceTriggerAt để POI mới trigger được ngay lập tức.
            if (_currentlyPlayingGeofencePoi != null)
            {
                bool stillInZone = poisInRange.Any(p => p.Id == _currentlyPlayingGeofencePoi.Id);
                if (!stillInZone)
                {
                    StopAudio();
                    _currentlyPlayingGeofencePoi = null;
                    // Reset để bỏ qua cooldown POI cũ khi đã sang vùng POI mới
                    _lastGeofenceTriggerAt = DateTime.MinValue;
                    await Task.Delay(300);
                }
            }

            var highestPri = poisInRange.OrderByDescending(p => p.Priority).First();
            // FIX #2: Lấy cooldown riêng của POI mới (highestPri), không dùng _geofenceTriggerCooldown cũ
            var cooldownSeconds = highestPri.CooldownInSeconds > 0 ? highestPri.CooldownInSeconds : 5;
            var cooldownForThisPoi = TimeSpan.FromSeconds(cooldownSeconds);

            // Kịch bản B: Bật / đổi nhạc
            if (_currentlyPlayingGeofencePoi == null)
            {
                // Chỉ check CD khi POI này đã từng phát xong trước đó (có entry trong dict).
                // Nếu chưa có → chưa bao giờ phát → trigger ngay, không cần đợi CD.
                if (_poiLastTriggerAt.TryGetValue(highestPri.Id, out var lastTrigger)
                    && now - lastTrigger < cooldownForThisPoi)
                {
                    var remaining = (cooldownForThisPoi - (now - lastTrigger)).TotalSeconds;
                    System.Diagnostics.Debug.WriteLine(
                        $"[Geofence] '{highestPri.Name}' đang CD, còn {remaining:F0}s.");
                    return;
                }

                _currentlyPlayingGeofencePoi = highestPri;
                TriggerAutoAudio(highestPri);
            }
            else if (_currentlyPlayingGeofencePoi.Id != highestPri.Id)
            {
                if (highestPri.Priority > _currentlyPlayingGeofencePoi.Priority || !_isPlaying)
                {
                    StopAudio();
                    await Task.Delay(300);
                    _currentlyPlayingGeofencePoi = highestPri;
                    TriggerAutoAudio(highestPri);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi Geofence: {ex.Message}");
        }
        finally
        {
            _isCheckingGeofences = false;
        }
    }

    private void TriggerAutoAudio(PoiModel poi)
    {
        // KHÔNG ghi _poiLastTriggerAt ở đây.
        // CD chỉ được ghi sau khi phát HẾT tự nhiên (IsCompletedSuccessfully).
        // Nếu bị cancel (rời vùng, sang POI khác) thì KHÔNG tính CD — POI đó vẫn "tươi".
        _geofenceTriggerCooldown = TimeSpan.FromSeconds(
            poi.CooldownInSeconds > 0 ? poi.CooldownInSeconds : 5);

        _queueCts?.Cancel();
        _queueCts = new CancellationTokenSource();
        var capturedCts = _queueCts;

        _currentSelectedPoi = poi;
        _isPlaying = true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var btnPlayAudio = BtnPlayAudioCtrl;
            var detailPopup = DetailPopupCtrl;

            if (btnPlayAudio != null) btnPlayAudio.Text = "⏹️ Dừng phát";
            // Cập nhật popup nếu đang mở, không gọi ShowPoiDetail (sẽ StopAudio)
            if (detailPopup != null && detailPopup.IsVisible)
            {
                UpdatePopupContentOnly(poi);
            }
        });

        _ = PlayAudioQueueAsync(poi, capturedCts.Token).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                var ex = t.Exception?.GetBaseException();
                System.Diagnostics.Debug.WriteLine($"[TriggerAudio] Lỗi phát ngầm '{poi.Name}': {ex?.Message}");

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var btnPlayAudio = BtnPlayAudioCtrl;
                    if (btnPlayAudio != null)
                        btnPlayAudio.Text = poi.AudioUrls?.Count > 0 ? "🔊 Nghe File Ghi Âm" : "🗣️ Đọc Tự Động (TTS)";
                    SetStatus("Lỗi phát âm thanh tự động", priority: 2, autoRevertMs: 3000);
                });
            }
            else if (t.IsCompletedSuccessfully)
            {
                // ✅ Chỉ ghi CD khi phát HẾT toàn bộ queue (không bị cancel, không bị lỗi).
                // Nếu bị cancel giữa chừng (rời vùng / sang POI khác), t.IsCanceled = true
                // → không vào nhánh này → POI đó KHÔNG bị tính CD → lần sau vào lại phát bình thường.
                _poiLastTriggerAt[poi.Id] = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine(
                    $"[TriggerAudio] ✅ Phát xong '{poi.Name}', bắt đầu CD {poi.CooldownInSeconds}s.");
            }
            else if (t.IsCanceled)
            {
                // Bị cancel (rời vùng hoặc sang POI mới) → KHÔNG tính CD.
                // POI này vẫn có thể phát lại ngay khi quay lại vùng.
                System.Diagnostics.Debug.WriteLine(
                    $"[TriggerAudio] ⏹️ Bị huỷ '{poi.Name}', KHÔNG tính CD.");
            }
        });
    }

    // UpdateNearestPoiHighlight
    // ════════════════════════════════════════════════════════════════════════
    //  HIGHLIGHT POI GẦN NHẤT
    // ════════════════════════════════════════════════════════════════════════
    private void UpdateNearestPoiHighlight()
    {
        var mapView = MapViewCtrl;
        if (_allPoisCache.Count == 0 || mapView?.Pins == null || mapView.Pins.Count == 0) return;
        
        // Đang hiển thị Tour → KHÔNG làm gì cả, giữ nguyên tuyến đường
        if (_currentTour != null) return;

        PoiModel? nearestPoi = null;
        double minDistanceM = double.MaxValue;

        foreach (var poi in _allPoisCache)
        {
            var poiLoc = new MauiLocation.Location(poi.Latitude, poi.Longitude);
            double dist = MauiLocation.Location.CalculateDistance(
                _currentUserLocation, poiLoc, DistanceUnits.Kilometers) * 1000;
            if (dist < minDistanceM) { minDistanceM = dist; nearestPoi = poi; }
        }

        if (nearestPoi == null) return;

        var distanceText = minDistanceM >= 1000
            ? $"{minDistanceM / 1000:F1} km"
            : $"{minDistanceM:F0} m";

        SetStatus($"📍 Gần nhất: {nearestPoi.Name} · {distanceText}", priority: 0);

        if (_nearestHighlightedPoi?.Id != nearestPoi.Id)
        {
            _nearestHighlightedPoi = nearestPoi;
            var capturedNearest = nearestPoi;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var pin in mapView.Pins)
                {
                    bool isNearest = pin.Tag is PoiModel p && p.Id == capturedNearest.Id;
                    pin.Color = isNearest
                        ? Microsoft.Maui.Graphics.Colors.DeepSkyBlue
                        : Microsoft.Maui.Graphics.Colors.Red;
                    pin.Scale = isNearest ? 0.85f : 0.5f;
                }
                mapView.RefreshGraphics();
            });
        }
    }

    // SetStatus, ShowIdleStatus
    // ════════════════════════════════════════════════════════════════════════
    //  STATUS BAR MANAGER
    // ════════════════════════════════════════════════════════════════════════
    private void SetStatus(string text, int priority, int autoRevertMs = 0, bool force = false)
    {
        if (!force && priority < _statusPriority) return;

        _statusRevertCts?.Cancel();
        _statusRevertCts = null;
        _statusPriority = priority;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var statusLabel = StatusLabelCtrl;
            if (statusLabel != null) statusLabel.Text = text;
        });

        if (autoRevertMs > 0)
        {
            var cts = new CancellationTokenSource();
            _statusRevertCts = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(autoRevertMs, cts.Token);
                    _statusPriority = 0;
                    MainThread.BeginInvokeOnMainThread(ShowIdleStatus);
                }
                catch (OperationCanceledException) { }
            });
        }
    }

    private void ShowIdleStatus()
    {
        if (_isPlaying || _nearestHighlightedPoi == null) return;
        var statusLabel = StatusLabelCtrl;
        if (statusLabel == null) return;

        var poiLoc = new MauiLocation.Location(
            _nearestHighlightedPoi.Latitude, _nearestHighlightedPoi.Longitude);
        double dist = MauiLocation.Location.CalculateDistance(
            _currentUserLocation, poiLoc, DistanceUnits.Kilometers) * 1000;
        string distText = dist >= 1000 ? $"{dist / 1000:F1} km" : $"{dist:F0} m";
        statusLabel.Text = $"📍 Gần nhất: {_nearestHighlightedPoi.Name} · {distText}";
    }
}
