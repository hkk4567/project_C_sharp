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
        SetStatus(SmartTourGuide.Mobile.Resources.Strings.AppResources.StatusLoading, priority: 2, force: true);

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
            await _localDb.UpdateSyncTimeAsync();

            // Cập nhật UI
            _isOffline = false;
            UpdateOfflineBanner(isOffline: false);
            RenderPoisOnMap(pois);

            SetStatus(
                string.Format(SmartTourGuide.Mobile.Resources.Strings.AppResources.StatusLoaded, pois.Count),
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
            SetStatus("❌ Không có mạng và chưa có dữ liệu offline",
                      priority: 4, force: true);
            return;
        }

        var pois = await _localDb.GetPoisAsync();

        _isOffline = true;
        UpdateOfflineBanner(isOffline: true);
        RenderPoisOnMap(pois);

        // Thông báo nhẹ
        var lastSync = await _localDb.GetLastSyncTimeAsync();
        string syncText = lastSync.HasValue
            ? $"Đồng bộ: {lastSync.Value:dd/MM HH:mm}"
            : "Chưa đồng bộ";

        string prefix = apiError ? "⚠️ Server lỗi · " : "📴 Offline · ";
        SetStatus($"{prefix}{syncText} · {pois.Count} địa điểm",
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
            SetStatus("🔄 Đang đồng bộ...", priority: 2, force: true);

            var pois = await _apiService.GetPoisAsync(_currentLanguageCode);
            await _localDb.SavePoisAsync(pois);
            await _localDb.UpdateSyncTimeAsync();

            RenderPoisOnMap(pois);

            SetStatus($"✅ Đồng bộ xong · {pois.Count} địa điểm",
                      priority: 2, autoRevertMs: 3000, force: true);

            // Cache ảnh + audio mới ngầm
            _ = Task.Run(() => _cacheService.PreCacheAllAsync(pois, BaseApiUrl));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] Lỗi: {ex.Message}");
            SetStatus("⚠️ Đồng bộ thất bại", priority: 2, autoRevertMs: 3000);
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
            
            // Chỉ xóa TourRoute nếu không có tour đang hiển thị
            // Nếu tour đang active, giữ route layer để user xem lâu hơn
            if (_currentTour == null)
            {
                ClearMapLayers("Geofences", "TourRoute");
            }
            else
            {
                ClearMapLayers("Geofences"); // Chỉ xóa Geofences, giữ TourRoute
            }
            mapView.Map.Layers.Insert(1, CreateGeofenceLayer(pois));

            foreach (var poi in pois)
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
        // Gom TẤT CẢ thao tác liên quan đến UI vào MainThread
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Disable/Enable nút
            btnLanguage.IsEnabled = !isOffline;
            btnShowTours.IsEnabled = !isOffline;
            btnLanguage.Opacity = isOffline ? 0.4 : 1.0;
            btnShowTours.Opacity = isOffline ? 0.4 : 1.0;

            if (!isOffline)
            {
                await OfflineBanner.FadeToAsync(0, 300);
                OfflineBanner.IsVisible = false;
                return;
            }

            OfflineBanner.IsVisible = true;

            if (hasNoData)
            {
                OfflineBannerLabel.Text = "📴 Không có mạng — Chưa có dữ liệu offline";
                OfflineBanner.BackgroundColor = MauiColor.FromArgb("#B71C1C"); 
            }
            else
            {
                var lastSync = await _localDb.GetLastSyncTimeAsync();
                string syncText = lastSync.HasValue
                    ? lastSync.Value.ToString("dd/MM HH:mm")
                    : "chưa rõ";

                OfflineBannerLabel.Text = $"📴 Đang offline · Dữ liệu: {syncText}";
                OfflineBanner.BackgroundColor = MauiColor.FromArgb("#E65100"); 
            }

            OfflineBanner.Opacity = 0;
            await OfflineBanner.FadeToAsync(1, 300);
        });
    }
}
