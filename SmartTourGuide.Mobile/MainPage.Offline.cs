using SmartTourGuide.Mobile.Services;
using SmartTourGuide.Mobile.Models;
using MauiColor = Microsoft.Maui.Graphics.Color;
namespace SmartTourGuide.Mobile;

/// <summary>
/// MainPage.Offline.cs — Xử lý toàn bộ logic online/offline fallback.
/// Partial class — ghép chung với MainPage.xaml.cs khi build.
/// </summary>
public partial class MainPage
{
    // ── SERVICES ─────────────────────────────────────────────────────────────
    private readonly LocalDatabase _localDb = new();
    private readonly CacheService _cacheService = new();

    // Trạng thái mạng hiện tại
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
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        bool nowOnline = e.NetworkAccess == NetworkAccess.Internet;

        if (nowOnline && _isOffline)
        {
            // Vừa có mạng lại → sync ngầm
            _isOffline = false;
            UpdateOfflineBanner(isOffline: false);
            await SyncFromServerAsync();
        }
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
        SetStatus(AppRes.StatusLoading, priority: 2, force: true);

        try
        {
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
            // Fallback cuối cùng
            await LoadPoisOfflineAsync();
        }
    }

    private async Task LoadPoisOnlineAsync()
    {
        // Nếu tour đang hiển thị, skip update để giữ tour route
        if (_currentTour != null)
        {
            System.Diagnostics.Debug.WriteLine("[Online] Skip update - Tour đang hiển thị");
            return;
        }

        try
        {
            var pois = await _apiService.GetPoisAsync(_currentLanguageCode);

            // Lưu vào SQLite
            await _localDb.SavePoisAsync(pois);

            // Lưu Tours vào SQLite
            try
            {
                var tours = await _apiService.GetToursAsync();
                await _localDb.SaveToursAsync(tours);
            }
            catch (Exception exTour)
            {
                System.Diagnostics.Debug.WriteLine($"[Online] Lỗi cache tour: {exTour.Message}");
            }

            await _localDb.UpdateSyncTimeAsync();

            // Cập nhật UI
            _isOffline = false;
            UpdateOfflineBanner(isOffline: false);
            RenderPoisOnMap(pois);

            SetStatus(
                string.Format(AppRes.StatusLoaded, pois.Count),
                priority: 2, autoRevertMs: 3000, force: true);

            // Pre-cache ảnh + audio ngầm (không chờ)
            _ = Task.Run(() => _cacheService.PreCacheAllAsync(pois, BaseApiUrl));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Online] API lỗi: {ex.Message}");
            // API lỗi dù có mạng → thử offline
            await LoadPoisOfflineAsync(apiError: true);
        }
    }

    // ── Offline path ──────────────────────────────────────────────────────────
    private async Task LoadPoisOfflineAsync(bool apiError = false)
    {
        // Nếu tour đang hiển thị, skip update để giữ tour route
        if (_currentTour != null)
        {
            System.Diagnostics.Debug.WriteLine("[Offline] Skip update - Tour đang hiển thị");
            return;
        }

        bool hasCached = await _localDb.HasCachedPoisAsync();

        if (!hasCached)
        {
            // Chưa có cache lần nào
            _isOffline = true;
            UpdateOfflineBanner(isOffline: true, hasNoData: true);
            SetStatus(AppRes.StatusNoNetworkNoData, priority: 4, force: true);
            return;
        }

        var pois = await _localDb.GetPoisAsync();

        _isOffline = true;
        UpdateOfflineBanner(isOffline: true);
        RenderPoisOnMap(pois);

        // Thông báo nhẹ
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
        // Nếu tour đang hiển thị, skip sync để giữ tour route
        if (_currentTour != null)
        {
            System.Diagnostics.Debug.WriteLine("[Sync] Skip - Tour đang hiển thị");
            return;
        }

        try
        {
            SetStatus(AppRes.StatusSyncing, priority: 2, force: true);

            var pois = await _apiService.GetPoisAsync(_currentLanguageCode);
            await _localDb.SavePoisAsync(pois);

            // Sync Tours
            try
            {
                var tours = await _apiService.GetToursAsync();
                await _localDb.SaveToursAsync(tours);
            }
            catch (Exception exTour)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Lỗi sync tour: {exTour.Message}");
            }

            await _localDb.UpdateSyncTimeAsync();

            RenderPoisOnMap(pois);

            SetStatus(string.Format(AppRes.StatusSyncDone, pois.Count),
                      priority: 2, autoRevertMs: 3000, force: true);

            // Cache ảnh + audio mới ngầm
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
        _allPoisCache = pois;
        _nearestHighlightedPoi = null;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            mapView.Pins.Clear();
            List<PoiModel> poisToRender;
            if (_currentTour != null)
            {
                var tourPoiIds = _currentTour.Pois.Select(p => p.PoiId).ToHashSet();
                poisToRender = pois.Where(p => tourPoiIds.Contains(p.Id)).ToList();
            }
            else
            {
                poisToRender = pois;
            }
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
        if (poi.ImageUrls == null || poi.ImageUrls.Count == 0)
        {
            MainThread.BeginInvokeOnMainThread(() => ImageContainer.IsVisible = false);
            return;
        }

        string rawUrl = poi.ImageUrls[0].Replace("\\", "/").TrimStart('/');
        string fullUrl = $"{BaseApiUrl.TrimEnd('/')}/{rawUrl}";

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ImageContainer.IsVisible = true;
            imgPoi.Source = null; // clear cũ
        });

        // Thử local cache trước
        string? localPath = await _cacheService.GetLocalImagePathAsync(fullUrl);

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
            // ✅ Lớp 1: Kiểm tra page còn gắn vào window không
            // Nếu app đang shutdown hoặc navigate away thì dừng luôn
            if (this.Window == null || !this.IsLoaded) return;

            try
            {
                btnLanguage.IsEnabled = !isOffline;
                btnShowTours.IsEnabled = !isOffline;
                btnLanguage.Opacity = isOffline ? 0.4 : 1.0;
                btnShowTours.Opacity = isOffline ? 0.4 : 1.0;

                if (!isOffline)
                {
                    // ✅ Kiểm tra lại trước mỗi animation
                    if (this.Window == null || !this.IsLoaded) return;
                    await OfflineBanner.FadeToAsync(0, 300);
                    OfflineBanner.IsVisible = false;
                    return;
                }

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

                // ✅ Kiểm tra lại trước animation cuối
                if (this.Window == null || !this.IsLoaded) return;
                await OfflineBanner.FadeToAsync(1, 300);
            }
            catch (ObjectDisposedException)
            {
                // ✅ Lớp 2: App đang shutdown, bỏ qua animation — không crash
            }
            catch (InvalidOperationException)
            {
                // ✅ MAUI đôi khi throw InvalidOperationException thay vì ObjectDisposedException
                // khi handler được gọi sau khi page detach khỏi visual tree
            }
        });
    }
}