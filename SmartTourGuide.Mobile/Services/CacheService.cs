using SmartTourGuide.Mobile.Models;
using System.Collections.Concurrent;

namespace SmartTourGuide.Mobile.Services;

/// <summary>
/// Tải trước và quản lý cache cho ảnh, audio, map tile.
/// Tất cả file lưu tại FileSystem.CacheDirectory.
/// Đã được tối ưu Thread-safe, Cancel task cũ và Atomic file write qua .tmp
/// </summary>
public class CacheService
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly string _imgDir;
    private readonly string _audioDir;

    // Quản lý Token (Cách của bạn rất chuẩn xác để chống rò rỉ bộ nhớ)
    private CancellationTokenSource? _precacheCts;

    // Dictionary lưu các Task đang chạy để UI và Background dùng chung, không tải trùng
    private readonly ConcurrentDictionary<string, Task<string?>> _activeDownloads = new();

    public event Action<int, int>? ProgressChanged; // (done, total)

    public CacheService()
    {
        // Tăng timeout lên 60s vì audio có thể nặng
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Add("User-Agent", "SmartTourGuide/1.0");

        _cacheDir = FileSystem.CacheDirectory;
        _imgDir = Path.Combine(_cacheDir, "images");
        _audioDir = Path.Combine(_cacheDir, "audio");

        Directory.CreateDirectory(_imgDir);
        Directory.CreateDirectory(_audioDir);
    }

    // ── ẢNH ──────────────────────────────────────────────────────────────────

    public Task<string?> GetLocalImagePathAsync(string url, CancellationToken ct = default)
        => DownloadSafelyAsync(url, _imgDir, ct);

    public bool IsImageCached(string url)
        => File.Exists(Path.Combine(_imgDir, SanitizeFileName(url)));

    // ── AUDIO ─────────────────────────────────────────────────────────────────

    public Task<string?> GetLocalAudioPathAsync(string url, CancellationToken ct = default)
        => DownloadSafelyAsync(url, _audioDir, ct);

    public bool IsAudioCached(string url)
        => File.Exists(Path.Combine(_audioDir, SanitizeFileName(url)));

    // ── PRE-CACHE TẤT CẢ POI ─────────────────────────────────────────────────

    /// <summary>
    /// Tải trước toàn bộ ảnh + audio của tất cả POI.
    /// Chạy background — không block UI.
    /// </summary>
    public async Task PreCacheAllAsync(List<PoiModel> pois, string baseApiUrl, CancellationToken externalCt = default)
    {
        // 1. Hủy đợt tải trước (nếu có) trước khi bắt đầu đợt mới (Logic của bạn)
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _precacheCts, newCts);
        oldCts?.Cancel();
        oldCts?.Dispose();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(newCts.Token, externalCt);
        var ct = linkedCts.Token;

        // 2. Thu thập tất cả URL cần download
        var tasks = new List<(string url, string type)>();

        foreach (var poi in pois)
        {
            if (poi.ImageUrls != null)
                foreach (var img in poi.ImageUrls)
                    tasks.Add(($"{baseApiUrl.TrimEnd('/')}/{img.TrimStart('/')}", "img"));

            if (poi.AudioUrls != null)
                foreach (var audio in poi.AudioUrls)
                    tasks.Add(($"{baseApiUrl.TrimEnd('/')}/{audio.TrimStart('/')}", "audio"));
        }

        int total = tasks.Count;
        int done = 0;

        // Download song song tối đa 3 file cùng lúc để tránh nghẽn mạng
        var semaphore = new SemaphoreSlim(3);

        var downloads = tasks.Select(async t =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();

                // Bắt buộc truyền Token (ct) vào để cắt đứt HTTP ngay lập tức nếu bị Cancel
                if (t.type == "img")
                    await GetLocalImagePathAsync(t.url, ct);
                else
                    await GetLocalAudioPathAsync(t.url, ct);

                Interlocked.Increment(ref done);
                ProgressChanged?.Invoke(done, total);
            }
            catch (OperationCanceledException) { /* Đợt tải bị hủy, bỏ qua */ }
            finally
            {
                semaphore.Release();
            }
        });

        try { await Task.WhenAll(downloads); }
        catch (OperationCanceledException) { /* Bị hủy bởi đợt sync mới */ }
    }

    // ── CORE DOWNLOAD ENGINE (AN TOÀN & CHỐNG LỖI CHẠM FILE) ──────────────────

    private Task<string?> DownloadSafelyAsync(string url, string targetDir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<string?>(null);

        string fileName = SanitizeFileName(url);
        string localPath = Path.Combine(targetDir, fileName);

        // Đã có file hoàn chỉnh thì trả về luôn
        if (File.Exists(localPath)) return Task.FromResult<string?>(localPath);

        // Chống tải trùng: Nếu URL này đang được tải bởi ai đó, lấy luôn Task đó để chờ chung
        return _activeDownloads.GetOrAdd(url, async key =>
        {
            // Dùng file .tmp để tránh lỗi file đang ghi dở bị app khác đọc hoặc app crash
            string tempPath = localPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                // Dùng HttpStream thay vì Byte Array để tiết kiệm RAM điện thoại (rất tốt cho Audio)
                using var response = await _http.GetAsync(key, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                using var streamToReadFrom = await response.Content.ReadAsStreamAsync(ct);
                using var streamToWriteTo = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                await streamToReadFrom.CopyToAsync(streamToWriteTo, ct);
                streamToWriteTo.Close(); // Bắt buộc đóng File tạm trước khi Move

                // ATOMIC WRITE: Ghi xong 100% mới đổi tên file temp thành file chính thức
                File.Move(tempPath, localPath, overwrite: true);

                return localPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cache Error] Lỗi tải {key}: {ex.Message}");
                return null; // Trả về null để UI tự fallback sang load online
            }
            finally
            {
                // Dọn dẹp file temp nếu tải thất bại / bị cancel giữa chừng
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }

                // Tải xong thì gỡ khỏi danh sách đang tải
                _activeDownloads.TryRemove(key, out _);
            }
        });
    }

    // ── MAP TILE & TIỆN ÍCH ──────────────────────────────────────────────────

    public static void ConfigureMapTileCache()
    {
        string tileDir = Path.Combine(FileSystem.CacheDirectory, "maptiles");
        Directory.CreateDirectory(tileDir);
    }

    public CacheInfo GetCacheInfo()
    {
        long imgSize = DirSize(_imgDir);
        long audioSize = DirSize(_audioDir);
        long tileDir = DirSize(Path.Combine(_cacheDir, "maptiles"));

        return new CacheInfo
        {
            ImageCount = Directory.GetFiles(_imgDir).Length,
            AudioCount = Directory.GetFiles(_audioDir).Length,
            ImageSizeMb = imgSize / 1024.0 / 1024.0,
            AudioSizeMb = audioSize / 1024.0 / 1024.0,
            MapTileSizeMb = tileDir / 1024.0 / 1024.0,
        };
    }

    public void ClearCache()
    {
        // Hủy mọi tiến trình tải ngầm trước khi tiến hành xóa file
        var oldCts = Interlocked.Exchange(ref _precacheCts, null);
        oldCts?.Cancel();
        oldCts?.Dispose();

        // Bọc try-catch để lỡ file nào đang bị OS khoá thì không văng app
        foreach (var f in Directory.GetFiles(_imgDir)) { try { File.Delete(f); } catch { } }
        foreach (var f in Directory.GetFiles(_audioDir)) { try { File.Delete(f); } catch { } }
    }

    private static string SanitizeFileName(string url)
    {
        string name = Path.GetFileName(url.Split('?')[0]);
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static long DirSize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                        .Sum(f => new FileInfo(f).Length);
    }
}

public class CacheInfo
{
    public int ImageCount { get; set; }
    public int AudioCount { get; set; }
    public double ImageSizeMb { get; set; }
    public double AudioSizeMb { get; set; }
    public double MapTileSizeMb { get; set; }

    public override string ToString() =>
        $"Ảnh: {ImageCount} file ({ImageSizeMb:F1} MB) | " +
        $"Audio: {AudioCount} file ({AudioSizeMb:F1} MB) | " +
        $"Tile: {MapTileSizeMb:F1} MB";
}