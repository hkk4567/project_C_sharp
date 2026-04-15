using SmartTourGuide.Mobile.Services;
using SmartTourGuide.Mobile.Models;
using MauiColor = Microsoft.Maui.Graphics.Color;
namespace SmartTourGuide.Mobile;

/// <summary>
/// Mục đích file:
/// 1) Quyết định dữ liệu POI lấy từ online hay offline.
/// 2) Đồng bộ cache cục bộ khi có mạng trở lại.
/// 3) Tải ảnh/audio theo chiến lược local trước, remote sau.
/// 4) Cập nhật banner trạng thái mạng để người dùng hiểu tình trạng hiện tại.
///
/// Đây là partial class, được ghép với MainPage ở thời điểm build.
/// </summary>
public partial class MainPage
{
    // ── SERVICES ─────────────────────────────────────────────────────────────
    private readonly LocalDatabase _localDb = new();
    private readonly CacheService _cacheService = new();

    // Trạng thái mạng hiện tại (true = đang offline).
    private bool _isOffline = false;

    // ════════════════════════════════════════════════════════════════════════
    //  KIỂM TRA MẠNG
    // ════════════════════════════════════════════════════════════════════════

    private static bool IsInternetAvailable()
        => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    /// <summary>
    /// Đăng ký lắng nghe thay đổi kết nối mạng.
    /// Gọi trong constructor MainPage.
    /// </summary>
    private void RegisterConnectivityChanged()
    {
        if (_isConnectivityRegistered)
            return;

        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        _isConnectivityRegistered = true;
    }

    private void UnregisterConnectivityChanged()
    {
        if (!_isConnectivityRegistered)
            return;

        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        _isConnectivityRegistered = false;
    }

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        // 1) Đọc trạng thái mạng mới từ hệ điều hành.
        bool nowOnline = e.NetworkAccess == NetworkAccess.Internet;

        // 2) Trường hợp từ offline -> online: tắt banner offline và đồng bộ ngầm.
        if (nowOnline && _isOffline)
        {
            // Vừa có mạng lại → sync ngầm
            _isOffline = false;
            UpdateOfflineBanner(isOffline: false);
            await SyncFromServerAsync();
        }
        // 3) Trường hợp từ online -> offline: bật banner offline.
        else if (!nowOnline && !_isOffline)
        {
            _isOffline = true;
            UpdateOfflineBanner(isOffline: true);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LOAD POI — ONLINE TRƯỚC, OFFLINE FALLBACK
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Thay thế hoàn toàn LoadPoisOnMap() cũ.
    /// Gọi từ OnAppearing và OnReloadClicked.
    /// </summary>
    private async Task LoadPoisWithOfflineFallbackAsync()
    {
        // 1) Hiển thị trạng thái tải dữ liệu.
        SetStatus(AppRes.StatusLoading, priority: 2, force: true);

        try
        {
            // 2) Có mạng thì đi nhánh online, mất mạng thì dùng cache local.
            if (IsInternetAvailable())
            {
                await LoadPoisOnlineAsync();
            }
            else
            {
                await LoadPoisOfflineAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Offline] LoadPois lỗi: {ex.Message}");
            // 3) Fallback cuối cùng: luôn cố tải từ local để không trắng màn hình.
            await LoadPoisOfflineAsync();
        }
    }

    private async Task LoadPoisOnlineAsync()
    {
        // Nếu đang ở chế độ tour, không reload POI để tránh phá route hiện tại.
        if (_currentTour != null)
        {
            System.Diagnostics.Debug.WriteLine("[Online] Skip update - Tour đang hiển thị");
            return;
        }

        try
        {
            // 1) Lấy dữ liệu POI mới nhất từ server.
            var pois = await _apiService.GetPoisAsync(_currentLanguageCode);

            // 2) Lưu dữ liệu vào SQLite để dùng khi offline.
            await _localDb.SavePoisAsync(pois);
            await _localDb.UpdateSyncTimeAsync();

            // 3) Cập nhật UI theo chế độ online.
            _isOffline = false;
            UpdateOfflineBanner(isOffline: false);
            RenderPoisOnMap(pois);

            SetStatus(
                string.Format(AppRes.StatusLoaded, pois.Count),
                priority: 2, autoRevertMs: 3000, force: true);

            // 4) Tải trước ảnh/audio ở nền để lần mở sau nhanh hơn.
            _ = Task.Run(() => _cacheService.PreCacheAllAsync(pois, BaseApiUrl));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Online] API lỗi: {ex.Message}");
            // API lỗi dù có mạng -> chuyển sang local cache.
            await LoadPoisOfflineAsync(apiError: true);
        }
    }

    // ── Offline path ──────────────────────────────────────────────────────────
    private async Task LoadPoisOfflineAsync(bool apiError = false)
    {
        // Nếu đang ở chế độ tour, không reload POI để giữ route hiện tại.
        if (_currentTour != null)
        {
            System.Diagnostics.Debug.WriteLine("[Offline] Skip update - Tour đang hiển thị");
            return;
        }

        // 1) Kiểm tra local cache có dữ liệu hay chưa.
        bool hasCached = await _localDb.HasCachedPoisAsync();

        if (!hasCached)
        {
            // 2) Không có cache: báo lỗi offline không dữ liệu.
            _isOffline = true;
            UpdateOfflineBanner(isOffline: true, hasNoData: true);
            SetStatus(AppRes.StatusNoNetworkNoData, priority: 4, force: true);
            return;
        }

        // 3) Có cache: render POI từ SQLite.
        var pois = await _localDb.GetPoisAsync();

        _isOffline = true;
        UpdateOfflineBanner(isOffline: true);
        RenderPoisOnMap(pois);

        // 4) Tạo thông điệp trạng thái có kèm thời điểm sync gần nhất.
        var lastSync = await _localDb.GetLastSyncTimeAsync();
        string syncText = lastSync.HasValue
            ? string.Format(AppRes.StatusSyncedAt, lastSync.Value.ToString("dd/MM HH:mm"))
            : AppRes.StatusNeverSynced;

        string prefix = apiError ? AppRes.PrefixServerError : AppRes.PrefixOffline;
        SetStatus(string.Format(AppRes.StatusOfflineWithCount, prefix, syncText, pois.Count),
                  priority: 2, autoRevertMs: 5000, force: true);
    }

    // ── Sync khi mạng trở lại ────────────────────────────────────────────────
    private async Task SyncFromServerAsync()
    {
        // Nếu đang ở chế độ tour, hoãn sync để tránh thay đổi state giữa chừng.
        if (_currentTour != null)
        {
            System.Diagnostics.Debug.WriteLine("[Sync] Skip - Tour đang hiển thị");
            return;
        }

        try
        {
            // 1) Báo trạng thái đang đồng bộ.
            SetStatus(AppRes.StatusSyncing, priority: 2, force: true);

            // 2) Lấy dữ liệu mới và ghi lại cache local.
            var pois = await _apiService.GetPoisAsync(_currentLanguageCode);
            await _localDb.SavePoisAsync(pois);
            await _localDb.UpdateSyncTimeAsync();

            // 3) Render lại bản đồ theo dữ liệu mới nhất.
            RenderPoisOnMap(pois);

            SetStatus(string.Format(AppRes.StatusSyncDone, pois.Count),
                      priority: 2, autoRevertMs: 3000, force: true);

            // 4) Cập nhật cache media ở nền.
            _ = Task.Run(() => _cacheService.PreCacheAllAsync(pois, BaseApiUrl));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] Lỗi: {ex.Message}");
            SetStatus(AppRes.StatusSyncFailed, priority: 2, autoRevertMs: 3000);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  RENDER POI LÊN BẢN ĐỒ (tách riêng để dùng chung online/offline)
    // ════════════════════════════════════════════════════════════════════════

    private void RenderPoisOnMap(List<PoiModel> pois)
    {
        // 1) Cập nhật cache dùng chung cho các chức năng khác (detail/geofence/tour).
        _allPoisCache = pois;
        _nearestHighlightedPoi = null;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // 2) Xóa pin cũ trước khi vẽ dữ liệu mới.
            mapView.Pins.Clear();
            List<PoiModel> poisToRender;
            if (_currentTour != null)
            {
                // Chế độ tour: chỉ vẽ các POI thuộc tour.
                var tourPoiIds = _currentTour.Pois.Select(p => p.PoiId).ToHashSet();
                poisToRender = pois.Where(p => tourPoiIds.Contains(p.Id)).ToList();
            }
            else
            {
                // Chế độ bình thường: vẽ toàn bộ POI.
                poisToRender = pois;
            }

            // 3) Vẽ lại geofence và pin POI.
            ClearMapLayers("Geofences");
            mapView.Map.Layers.Insert(1, CreateGeofenceLayer(poisToRender));

            foreach (var poi in poisToRender)
            {
                mapView.Pins.Add(new Mapsui.UI.Maui.Pin(mapView)
                {
                    Position = new Mapsui.UI.Maui.Position(poi.Latitude, poi.Longitude),
                    Type = Mapsui.UI.Maui.PinType.Pin,
                    Label = poi.Name,
                    Address = poi.Address,
                    Color = MauiColor.FromArgb("#F44336"), // Red
                    Scale = 0.5f,
                    Tag = poi
                });
            }

            mapView.RefreshGraphics();
        });

        // 4) Đồng bộ gợi ý tìm kiếm theo danh sách POI hiện tại.
        MainThread.BeginInvokeOnMainThread(() => UpdatePoiSearchSuggestions(PoiSearchBarCtrl?.Text));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ẢNH POI — LOCAL TRƯỚC, REMOTE FALLBACK
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gọi trong ShowPoiDetail() thay cho ImageSource.FromUri trực tiếp.
    /// </summary>
    private async Task LoadPoiImageAsync(PoiModel poi)
    {
        // 1) Không có ảnh thì ẩn container ngay.
        if (poi.ImageUrls == null || poi.ImageUrls.Count == 0)
        {
            MainThread.BeginInvokeOnMainThread(() => ImageContainer.IsVisible = false);
            return;
        }

        string rawUrl = poi.ImageUrls[0].Replace("\\", "/").TrimStart('/');
        string fullUrl = $"{BaseApiUrl.TrimEnd('/')}/{rawUrl}";

        // 2) Hiển thị container và xóa ảnh cũ trước khi nạp ảnh mới.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ImageContainer.IsVisible = true;
            imgPoi.Source = null; // clear cũ
        });

        // 3) Ưu tiên ảnh local cache.
        string? localPath = await _cacheService.GetLocalImagePathAsync(fullUrl);

        // 4) Nếu local không có thì fallback ảnh remote.
        ImageSource source = localPath != null && File.Exists(localPath)
            ? ImageSource.FromFile(localPath)
            : ImageSource.FromUri(new Uri(fullUrl)); // fallback remote

        MainThread.BeginInvokeOnMainThread(() => imgPoi.Source = source);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  AUDIO — LOCAL TRƯỚC, REMOTE FALLBACK (thay GetLocalAudioPathAsync cũ)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Override GetLocalAudioPathAsync cũ bằng CacheService.
    /// Dùng trong PlayRemoteAudioAndWaitAsync.
    /// </summary>
    private Task<string?> GetCachedAudioPathAsync(string url)
        => _cacheService.GetLocalAudioPathAsync(url);

    // ════════════════════════════════════════════════════════════════════════
    //  OFFLINE BANNER UI
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cập nhật banner offline ở đầu màn hình.
    /// </summary>
    private void UpdateOfflineBanner(bool isOffline, bool hasNoData = false)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // 1) Chặn thao tác UI khi page đã bị dispose/detach.
            if (this.Window == null || !this.IsLoaded) return;

            try
            {
                // 2) Khóa các nút cần mạng khi offline.
                btnLanguage.IsEnabled = !isOffline;
                btnShowTours.IsEnabled = !isOffline;
                btnLanguage.Opacity = isOffline ? 0.4 : 1.0;
                btnShowTours.Opacity = isOffline ? 0.4 : 1.0;

                if (!isOffline)
                {
                    // 3) Online: ẩn banner bằng animation mượt.
                    if (this.Window == null || !this.IsLoaded) return;
                    await OfflineBanner.FadeToAsync(0, 300);
                    OfflineBanner.IsVisible = false;
                    return;
                }

                // 4) Offline: bật banner và đặt nội dung theo trạng thái dữ liệu.
                OfflineBanner.IsVisible = true;

                if (hasNoData)
                {
                    OfflineBannerLabel.Text = AppRes.StatusBannerNoNetwork;
                    OfflineBanner.BackgroundColor = MauiColor.FromArgb("#B71C1C");
                }
                else
                {
                    var lastSync = await _localDb.GetLastSyncTimeAsync();
                    string syncText = lastSync.HasValue
                        ? lastSync.Value.ToString("dd/MM HH:mm")
                        : AppRes.StatusBannerUnknownSync;

                    OfflineBannerLabel.Text = string.Format(AppRes.StatusBannerOffline, syncText);
                    OfflineBanner.BackgroundColor = MauiColor.FromArgb("#E65100");
                }

                OfflineBanner.Opacity = 0;

                // 5) Fade-in banner sau khi cập nhật nội dung.
                if (this.Window == null || !this.IsLoaded) return;
                await OfflineBanner.FadeToAsync(1, 300);
            }
            catch (ObjectDisposedException)
            {
                // App đang shutdown: bỏ qua để tránh crash.
            }
            catch (InvalidOperationException)
            {
                // MAUI có thể ném lỗi khi UI đã detach khỏi visual tree.
            }
        });
    }
}