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
    // Trong file MainPage.Geofence.cs

    private async void CheckGeofences()
    {
        try
        {
            if (_isCheckingGeofences || _allPoisCache.Count == 0) return;
            _isCheckingGeofences = true;

            var poisToCheck = _currentTour != null
                            ? _allPoisCache.Where(p => _currentTour.Pois.Any(tp => tp.PoiId == p.Id)).ToList()
                            : _allPoisCache;

            var now = DateTime.UtcNow;
            var poisInRange = new List<PoiModel>();

            foreach (var poi in poisToCheck)
            {
                var poiLoc = new MauiLocation.Location(poi.Latitude, poi.Longitude);
                double dist = MauiLocation.Location.CalculateDistance(_currentUserLocation, poiLoc, DistanceUnits.Kilometers) * 1000;
                double radius = poi.TriggerRadius > 0 ? poi.TriggerRadius : 50;
                if (dist <= radius) poisInRange.Add(poi);
            }

            // Kịch bản rời vùng
            if (poisInRange.Count == 0)
            {
                if (_currentlyPlayingGeofencePoi != null)
                {
                    _ = LogAudioPlaybackAsync(_currentlyPlayingGeofencePoi.Id, _playStartTime);
                    StopAudio();
                    _currentlyPlayingGeofencePoi = null;
                    _isGeofenceVisitActive = false;
                    _statusPriority = 0;
                    SetStatus("👋 Đã rời vùng phát", priority: 0, autoRevertMs: 2000);
                }
                return;
            }

            var highestPriPoi = poisInRange.OrderByDescending(p => p.Priority).First();

            // ✅ LOGIC MỚI: NẾU ĐỔI VÙNG THÌ DỪNG NGAY ÂM THANH CŨ
            // Bất kể POI mới có đang CD hay không
            if (_currentlyPlayingGeofencePoi != null && _currentlyPlayingGeofencePoi.Id != highestPriPoi.Id)
            {
                System.Diagnostics.Debug.WriteLine($"[Logic] Đổi vùng từ {_currentlyPlayingGeofencePoi.Name} sang {highestPriPoi.Name}. Dừng audio cũ.");

                StopAudio(); // Dừng POI 1 ngay lập tức
                _loggedPoisInCurrentGeofenceVisit.Clear();
                _currentlyPlayingGeofencePoi = null; // Xóa trạng thái POI đang phát cũ
                await Task.Delay(200); // Chờ một chút để hệ thống audio giải phóng
            }

            // ✅ CẬP NHẬT POPUP (Như đã làm ở câu trước)
            if (_currentSelectedPoi?.Id != highestPriPoi.Id)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _currentSelectedPoi = highestPriPoi;
                    if (DetailPopupCtrl != null) DetailPopupCtrl.IsVisible = true;
                    UpdatePopupContentOnly(highestPriPoi);
                });
            }

            // ✅ KIỂM TRA COOLDOWN
            int effectiveCd = highestPriPoi.CooldownInSeconds > 0 ? highestPriPoi.CooldownInSeconds : 5;
            if (_poiLastTriggerAt.TryGetValue(highestPriPoi.Id, out var lastFinishTime))
            {
                var elapsed = (DateTime.UtcNow - lastFinishTime).TotalSeconds;
                if (elapsed < effectiveCd)
                {
                    // TÍNH SỐ GIÂY CÒN LẠI
                    int remaining = (int)(effectiveCd - elapsed);

                    // HIỂN THỊ CD VỚI ƯU TIÊN CAO (Priority 2)
                    // Dùng force: true để đảm bảo nó hiện ra kể cả khi vừa StopAudio
                    SetStatus($"⏳ {highestPriPoi.Name} · Chờ phát lại: {remaining}s", priority: 2, force: true);
                    // Nếu đang CD thì thoát, không phát nhạc POI 2
                    return;
                }
            }

            // ✅ PHÁT NHẠC POI 2 (Khi đã hết CD)
            if (!_isPlaying) // Lúc này _isPlaying chắc chắn là false vì đã StopAudio ở trên
            {
                _currentlyPlayingGeofencePoi = highestPriPoi;
                _isGeofenceVisitActive = true;
                _ = TriggerGeofenceAudioQueue(highestPriPoi);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AudioQueue] Lỗi: {ex.Message}"); }
        finally { _isCheckingGeofences = false; }
    }

    private async Task TriggerGeofenceAudioQueue(PoiModel poi)
    {
        // Lấy danh sách audio urls cho ngôn ngữ hiện tại
        var urls = poi.AudioUrls;
        if (urls == null || urls.Count == 0) return;

        // Lấy index đang phát dở của POI này từ Dictionary
        if (!_poiAudioIndex.TryGetValue(poi.Id, out int currentIndex))
            currentIndex = 0;

        _isPlaying = true;
        _currentSelectedPoi = poi;
        _queueCts?.Cancel();
        _queueCts = new CancellationTokenSource();
        var ct = _queueCts.Token;

        try
        {
            // Duyệt qua hàng đợi audio bắt đầu từ vị trí cũ
            for (int i = currentIndex; i < urls.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                int displayIdx = i + 1;
                int total = urls.Count;
                var fileStartTime = DateTime.Now;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SetStatus($"🎵 {poi.Name} ({displayIdx}/{total})", priority: 3);
                    if (btnPlayAudio != null) btnPlayAudio.Text = $"⏹️ Dừng ({displayIdx}/{total})";
                });

                string rawPath = urls[i].Replace("\\", "/").TrimStart('/');
                string fullUrl = $"{BaseApiUrl.TrimEnd('/')}/{rawPath}";

                try
                {
                    await PlayRemoteAudioAndWaitAsync(fullUrl, ct);

                    // ✅ PHÁT XONG 1 FILE -> Cập nhật index ngay
                    currentIndex = i + 1;
                    _poiAudioIndex[poi.Id] = currentIndex;

                    // Ghi log thời gian nghe
                    _ = LogAudioPlaybackAsync(poi.Id, fileStartTime);
                }
                catch (OperationCanceledException)
                {
                    _ = LogAudioPlaybackAsync(poi.Id, fileStartTime);
                    throw;
                }
            }

            // ✅ KIỂM TRA NẾU ĐÃ PHÁT XONG TẤT CẢ FILE TRONG HÀNG ĐỢI
            if (currentIndex >= urls.Count)
            {
                // 1. Reset index về 0 để vòng lặp sau quay lại từ đầu
                _poiAudioIndex[poi.Id] = 0;

                // 2. Ghi nhận thời điểm kết thúc để tính CD (Lấy giá trị CD từ DB ở vòng check sau)
                _poiLastTriggerAt[poi.Id] = DateTime.UtcNow;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    int cdSeconds = poi.CooldownInSeconds;
                    SetStatus($"✅ Xong {poi.Name}. Nghỉ {cdSeconds}s", priority: 2, autoRevertMs: 3000);
                    if (btnPlayAudio != null) btnPlayAudio.Text = "🔊 Nghe lại";
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Rời vùng -> i giữ nguyên để lần sau quay lại phát tiếp file này
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioQueue] Lỗi: {ex.Message}");
        }
        finally
        {
            _isPlaying = false;
            _isGeofenceVisitActive = false;
        }
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
