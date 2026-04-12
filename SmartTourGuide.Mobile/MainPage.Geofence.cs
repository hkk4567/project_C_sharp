namespace SmartTourGuide.Mobile;

// File này xử lý geofence: 
//chọn POI trong vùng, phát audio theo quy tắc và quản lý trạng thái.
public partial class MainPage
{
    private Label? StatusLabelCtrl => this.FindByName<Label>("statusLabel");

    // ════════════════════════════════════════════════════════════════════════
    //  GEOFENCE ENGINE
    // ════════════════════════════════════════════════════════════════════════
    // Luồng chính:
    // 1) Xác định POI đang nằm trong bán kính geofence.
    // 2) Ưu tiên POI có Priority cao nhất nếu cùng lúc có nhiều POI.
    // 3) Đổi vùng thì dừng audio cũ ngay, sau đó xét cooldown của vùng mới.
    // 4) Mỗi POI có cooldown riêng; chưa hết cooldown thì chỉ hiển thị trạng thái chờ.
    // 5) Hết cooldown thì phát queue audio của POI, đồng thời ghi log lượt nghe.

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

            // Không còn POI nào trong vùng: dừng phát và reset trạng thái phiên hiện tại.
            if (poisInRange.Count == 0)
            {
                if (_currentlyPlayingGeofencePoi != null)
                {
                    // Chỉ log khi phiên geofence vẫn active để tránh cộng trùng lượt nghe.
                    if (_isGeofenceVisitActive)
                        _ = LogAudioPlaybackAsync(_currentlyPlayingGeofencePoi.Id, _playStartTime);

                    StopAudio();
                    _currentlyPlayingGeofencePoi = null;
                    _isGeofenceVisitActive = false;

                    // Xóa bộ chống trùng để lần vào vùng sau ghi log bình thường.
                    _loggedPoisInCurrentGeofenceVisit.Clear();

                    _statusPriority = 0;
                    SetStatus(AppRes.StatusGeofenceLeft, priority: 0, autoRevertMs: 2000);
                }
                return;
            }

            var highestPriPoi = poisInRange.OrderByDescending(p => p.Priority).First();

            // Nếu đổi sang vùng POI khác thì dừng audio cũ ngay, kể cả POI mới đang cooldown.
            if (_currentlyPlayingGeofencePoi != null && _currentlyPlayingGeofencePoi.Id != highestPriPoi.Id)
            {
                System.Diagnostics.Debug.WriteLine($"[Logic] Đổi vùng từ {_currentlyPlayingGeofencePoi.Name} sang {highestPriPoi.Name}. Dừng audio cũ.");

                StopAudio(); // Dừng POI cũ ngay lập tức
                _loggedPoisInCurrentGeofenceVisit.Clear();
                _currentlyPlayingGeofencePoi = null; // Xóa POI đang phát cũ
                await Task.Delay(200); // Chờ audio pipeline giải phóng tài nguyên
            }

            // Cập nhật popup theo POI ưu tiên hiện tại.
            if (_currentSelectedPoi?.Id != highestPriPoi.Id)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _currentSelectedPoi = highestPriPoi;
                    if (DetailPopupCtrl != null) DetailPopupCtrl.IsVisible = true;
                    UpdatePopupContentOnly(highestPriPoi);
                });
            }

            // Kiểm tra cooldown của POI ưu tiên.
            int effectiveCd = highestPriPoi.CooldownInSeconds > 0 ? highestPriPoi.CooldownInSeconds : 5;
            if (_poiLastTriggerAt.TryGetValue(highestPriPoi.Id, out var lastFinishTime))
            {
                var elapsed = (DateTime.UtcNow - lastFinishTime).TotalSeconds;
                if (elapsed < effectiveCd)
                {
                    // Tính số giây còn lại.
                    int remaining = (int)(effectiveCd - elapsed);

                    // Hiển thị trạng thái chờ với ưu tiên cao.
                    SetStatus(string.Format(AppRes.StatusWaitReplay, highestPriPoi.Name, remaining),
                              priority: 2, force: true);
                    // Đang cooldown thì không phát audio.
                    return;
                }
            }

            // Hết cooldown thì bắt đầu phát queue audio của 
            // POI ưu tiên.
            if (!_isPlaying)
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
        // Lấy danh sách audio theo ngôn ngữ hiện tại.
        var urls = poi.AudioUrls;
        if (urls == null || urls.Count == 0) return;

        // Mỗi lần vào vùng tạo session mới để dedup phía server 
        // hoạt động đúng.
        PrepareListenSession(poi.Id, allowReuseCurrent: false);

        // Khôi phục vị trí đang phát dở của POI từ bộ nhớ tạm.
        if (!_poiAudioIndex.TryGetValue(poi.Id, out int currentIndex))
            currentIndex = 0;

        _isPlaying = true;
        _currentSelectedPoi = poi;
        _queueCts?.Cancel();
        _queueCts = new CancellationTokenSource();
        var ct = _queueCts.Token;

        try
        {
            // Duyệt queue audio bắt đầu từ vị trí đang dở.
            for (int i = currentIndex; i < urls.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                int displayIdx = i + 1;
                int total = urls.Count;
                var fileStartTime = DateTime.Now;
                _playStartTime = fileStartTime;
                _currentAudioPoiId = poi.Id;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SetStatus(string.Format(AppRes.StatusPlayingTrackCompact, poi.Name, displayIdx, total), priority: 3);
                    if (btnPlayAudio != null)
                        btnPlayAudio.Text = string.Format(AppRes.BtnStopWithCountCompact, displayIdx, total);
                });

                string rawPath = urls[i].Replace("\\", "/").TrimStart('/');
                string fullUrl = $"{BaseApiUrl.TrimEnd('/')}/{rawPath}";

                try
                {
                    await PlayRemoteAudioAndWaitAsync(fullUrl, ct);

                    // Phát xong 1 file thì cập nhật index ngay.
                    currentIndex = i + 1;
                    _poiAudioIndex[poi.Id] = currentIndex;

                    // Ghi log thời gian nghe.
                    _ = LogAudioPlaybackAsync(poi.Id, fileStartTime);
                }
                catch (OperationCanceledException)
                {
                    // Không log tại đây; nhánh rời vùng đã xử lý log để tránh cộng trùng.
                    throw;
                }
            }

            // Nếu phát hết queue thì reset index và
            //  bắt đầu tính cooldown.
            if (currentIndex >= urls.Count)
            {
                // 1) Reset index để vòng sau phát lại từ đầu.
                _poiAudioIndex[poi.Id] = 0;

                // 2) Ghi thời điểm kết thúc để tính cooldown.
                _poiLastTriggerAt[poi.Id] = DateTime.UtcNow;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    int cdSeconds = poi.CooldownInSeconds;
                    SetStatus(string.Format(AppRes.StatusGeofenceDoneWait, poi.Name, cdSeconds),
                              priority: 2, autoRevertMs: 3000);
                    if (btnPlayAudio != null) btnPlayAudio.Text = AppRes.BtnRelisten;
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Rời vùng: giữ index hiện tại để lần vào sau phát tiếp.
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

        SetStatus(string.Format(AppRes.StatusNearestPoi, nearestPoi.Name, distanceText), priority: 0);

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

    // ════════════════════════════════════════════════════════════════════════
    //  STATUS BAR MANAGER là thanh trạng thái ở trên cùng
    //  hiển thị thông tin POI gần nhất, trạng thái phát audio, cooldown...
    
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
    // Trạng thái idle sẽ hiển thị POI gần nhất nếu có, hoặc ẩn nếu không có POI nào.
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
        statusLabel.Text = string.Format(AppRes.StatusNearestPoi, _nearestHighlightedPoi.Name, distText);
    }
}