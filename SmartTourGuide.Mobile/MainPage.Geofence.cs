namespace SmartTourGuide.Mobile;

public partial class MainPage
{
    private Label? StatusLabelCtrl => this.FindByName<Label>("statusLabel");

    // ════════════════════════════════════════════════════════════════════════
    //  GEOFENCE ENGINE
    // ════════════════════════════════════════════════════════════════════════
    //
    //  LOGIC AUDIO THEO VÙNG:
    //  - Mỗi POI có index audio riêng (ví dụ: 0/3, 1/3, 2/3).
    //  - Mỗi lần VÀO VÙNG → phát đúng 1 audio tại index hiện tại, rồi tăng index.
    //    Dù nghe hết hay bị ngắt (cancel) khi rời vùng → index vẫn tăng.
    //  - Khi index đã qua N/N (phát đủ vòng) VÀ người dùng RỜI VÙNG → bắt đầu CD, reset index.
    //  - Trong thời gian CD: vào lại vùng sẽ không phát, hiển thị trạng thái còn bao nhiêu giây.
    //  - CD là riêng biệt cho từng POI, không ảnh hưởng lẫn nhau.
    //
    private async void CheckGeofences()
    {
        try
        {
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
                _lastGeofenceInsideAt = now;

            // ── Kịch bản A: Ra khỏi tất cả vùng ──────────────────────────────
            if (poisInRange.Count == 0)
            {
                if (_currentlyPlayingGeofencePoi != null)
                {
                    var leftPoiName = _currentlyPlayingGeofencePoi.Name;
                    StopAudio(); // cancel audio → index KHÔNG tăng (đúng logic)
                    _currentlyPlayingGeofencePoi = null;
                    _isGeofenceVisitActive = false;
                    _loggedPoisInCurrentGeofenceVisit.Clear();
                    _currentListenSessionPoiId = 0;
                    _currentListenSessionId = string.Empty;
                    // CD KHÔNG tính ở đây vì bị cancel — chỉ tính khi phát xong tự nhiên N/N
                    SetStatus($"👋 Rời vùng: {leftPoiName}", priority: 2, autoRevertMs: 2000);
                }
                return;
            }

            // ── Kịch bản C: Rời vùng POI đang phát, bước vào vùng POI khác ───
            if (_currentlyPlayingGeofencePoi != null)
            {
                bool stillInZone = poisInRange.Any(p => p.Id == _currentlyPlayingGeofencePoi.Id);
                if (!stillInZone)
                {
                    StopAudio(); // cancel → index KHÔNG tăng, KHÔNG tính CD
                    _currentlyPlayingGeofencePoi = null;
                    await Task.Delay(300);
                }
            }

            if (_isGeofenceVisitActive)
                return;

            var highestPri = poisInRange.OrderByDescending(p => p.Priority).First();
            var cdSec = highestPri.CooldownInSeconds > 0 ? highestPri.CooldownInSeconds : 5;
            var cdSpan = TimeSpan.FromSeconds(cdSec);

            // ── Kịch bản B: Trigger POI mới ───────────────────────────────────
            if (_currentlyPlayingGeofencePoi == null)
            {
                // Kiểm tra CD riêng của POI này
                if (_poiLastTriggerAt.TryGetValue(highestPri.Id, out var lastTrigger)
                    && now - lastTrigger < cdSpan)
                {
                    var remaining = (cdSpan - (now - lastTrigger)).TotalSeconds;
                    SetStatus($"⏳ {highestPri.Name} · CD còn {remaining:F0}s", priority: 1);
                    System.Diagnostics.Debug.WriteLine(
                        $"[Geofence] '{highestPri.Name}' đang CD, còn {remaining:F0}s.");
                    return;
                }

                _isGeofenceVisitActive = true;
                _loggedPoisInCurrentGeofenceVisit.Clear();
                _currentlyPlayingGeofencePoi = highestPri;
                TriggerOneAudio(highestPri);
            }
            else if (_currentlyPlayingGeofencePoi.Id != highestPri.Id)
            {
                // Đang ở 2 vùng overlap, POI mới có priority cao hơn thì chiếm chỗ
                if (highestPri.Priority > _currentlyPlayingGeofencePoi.Priority || !_isPlaying)
                {
                    StopAudio();
                    await Task.Delay(300);
                    _isGeofenceVisitActive = true;
                    _loggedPoisInCurrentGeofenceVisit.Clear();
                    _currentlyPlayingGeofencePoi = highestPri;
                    TriggerOneAudio(highestPri);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Geofence] Lỗi: {ex.Message}");
        }
        finally
        {
            _isCheckingGeofences = false;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TRIGGER: Phát đúng 1 audio tại index hiện tại, rồi tăng index
    // ════════════════════════════════════════════════════════════════════════
    private void TriggerOneAudio(PoiModel poi)
    {
        var urls = poi.AudioUrls;
        var geofencePlayStartTime = DateTime.Now;  // ← NEW: Track start time for logging
        PrepareListenSession(poi.Id, allowReuseCurrent: false);

        // Không có audio file → TTS toàn bộ description (giữ nguyên hành vi cũ)
        if (urls == null || urls.Count == 0)
        {
            _currentSelectedPoi = poi;
            _isPlaying = true;
            _queueCts?.Cancel();
            _queueCts = new CancellationTokenSource();
            var ttsCts = _queueCts;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (BtnPlayAudioCtrl != null) BtnPlayAudioCtrl.Text = "⏹️ Dừng phát";
                if (DetailPopupCtrl?.IsVisible == true) UpdatePopupContentOnly(poi);
            });

            _ = SpeakDescription(poi.Description);
            return;
        }

        // Lấy index hiện tại của POI này
        if (!_poiAudioIndex.TryGetValue(poi.Id, out int index) || index >= urls.Count)
            index = 0;

        int displayIdx = index + 1;
        int total = urls.Count;

        System.Diagnostics.Debug.WriteLine(
            $"[Geofence] Trigger '{poi.Name}' audio {displayIdx}/{total}");

        _currentSelectedPoi = poi;
        _isPlaying = true;

        _queueCts?.Cancel();
        _queueCts = new CancellationTokenSource();
        var capturedCts = _queueCts;
        var capturedIndex = index;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (BtnPlayAudioCtrl != null)
                BtnPlayAudioCtrl.Text = total > 1 ? $"⏹️ Dừng ({displayIdx}/{total})" : "⏹️ Dừng phát";
            SetStatus($"🎵 {poi.Name}  ·  {displayIdx}/{total}", priority: 3);
            if (DetailPopupCtrl?.IsVisible == true) UpdatePopupContentOnly(poi);
        });

        string rawPath = urls[capturedIndex].Replace("\\", "/").TrimStart('/');
        string fullUrl = $"{BaseApiUrl.TrimEnd('/')}/{rawPath}";

        _ = PlayRemoteAudioAndWaitAsync(fullUrl, capturedCts.Token).ContinueWith(t =>
        {
            // ═══════════════════════════════════════════════════════════════
            //  QUAN TRỌNG: CHỈ tăng index khi phát XONG TỰ NHIÊN.
            //  Nếu bị cancel (rời vùng) → giữ nguyên index → lần vào lại
            //  sẽ phát đúng audio đó lại, không bỏ qua.
            // ═══════════════════════════════════════════════════════════════

            if (t.IsCanceled)
            {
                // ✅ NEW: Bị cancel do rời vùng → GHI LOG
                _ = LogAudioPlaybackAsync(poi.Id, geofencePlayStartTime);

                System.Diagnostics.Debug.WriteLine(
                    $"[Geofence] '{poi.Name}' audio {displayIdx}/{total} bị cancel, giữ index={capturedIndex}. ✅ Log requested.");
                MainThread.BeginInvokeOnMainThread(() => { _isPlaying = false; });
                return;
            }

            if (t.IsFaulted)
            {
                var ex = t.Exception?.GetBaseException();
                System.Diagnostics.Debug.WriteLine($"[Geofence] Lỗi audio '{poi.Name}': {ex?.Message}");
                MainThread.BeginInvokeOnMainThread(() => { _isPlaying = false; });
                return;
            }

            // ✅ NEW: Phát xong tự nhiên → GHI LOG
            _ = LogAudioPlaybackAsync(poi.Id, geofencePlayStartTime);

            // Phát xong tự nhiên → tăng index
            int nextIndex = capturedIndex + 1;
            bool allPlayed = (nextIndex >= total); // đã phát hết N/N

            if (allPlayed)
                nextIndex = 0; // reset về đầu cho vòng tiếp theo

            _poiAudioIndex[poi.Id] = nextIndex;

            var completedDuration = (int)(DateTime.Now - geofencePlayStartTime).TotalSeconds;
            System.Diagnostics.Debug.WriteLine(
                $"[Geofence] '{poi.Name}' audio {displayIdx}/{total} phát xong tự nhiên. " +
                $"nextIndex={nextIndex}, allPlayed={allPlayed}. ✅ Logged {completedDuration}s.");

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isPlaying = false;
                if (BtnPlayAudioCtrl != null)
                    BtnPlayAudioCtrl.Text = !allPlayed
                        ? $"🔊 Nghe audio ({nextIndex + 1}/{total})"
                        : "🔊 Nghe lại";

                if (allPlayed)
                {
                    // Phát đủ N/N → bắt đầu CD ngay, không cần đợi rời vùng
                    _poiLastTriggerAt[poi.Id] = DateTime.UtcNow;
                    _currentlyPlayingGeofencePoi = null;
                    _isGeofenceVisitActive = false;
                    _loggedPoisInCurrentGeofenceVisit.Clear();
                    SetStatus(
                        $"✅ {poi.Name} · Phát xong {total}/{total} · CD {poi.CooldownInSeconds}s",
                        priority: 2, autoRevertMs: 3000);
                    System.Diagnostics.Debug.WriteLine(
                        $"[Geofence] '{poi.Name}' ✅ đủ {total}/{total}, bắt đầu CD {poi.CooldownInSeconds}s.");
                }
                else
                {
                    // Phát xong audio này nhưng chưa đủ vòng → báo tiến độ
                    SetStatus($"✅ {poi.Name}  ·  {displayIdx}/{total} xong", priority: 2, autoRevertMs: 2000);
                }
            });
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
