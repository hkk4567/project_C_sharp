using SmartTourGuide.Mobile.Models;
using System.Collections.Concurrent;

namespace SmartTourGuide.Mobile.Services;
// CacheService.cs
// Chức năng chính:
// Cache ảnh và audio vào bộ nhớ tạm.

// Pre-cache hàng loạt ở background.

// Chống tải trùng, hủy task cũ, ghi file an toàn qua temp file.

// Quản lý/xóa cache và thống kê dung lượng cache.

// Tạo thư mục cache map tile.

// Tính tuyến đường và cache route
// <summary>
//Mục đích file:
// 1) Quản lý cache ảnh, audio và map tile trong bộ nhớ tạm của thiết bị.
// 2) Hỗ trợ pre-cache chạy nền để tăng tốc trải nghiệm khi mở POI.
//3) Đảm bảo an toàn luồng: chống tải trùng, hủy đợt cũ, ghi file kiểu atomic.
// </summary>
public class CacheService
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly string _imgDir;
    private readonly string _audioDir;

    // Token điều khiển vòng đời một đợt pre-cache.
    private CancellationTokenSource? _precacheCts;

    // Lưu các tác vụ tải đang chạy để nhiều nơi có thể chờ chung, không tải trùng URL.
    private readonly ConcurrentDictionary<string, Task<string?>> _activeDownloads = new();

    public event Action<int, int>? ProgressChanged; // (done, total)

    public CacheService()
    {
        // 1) Cấu hình HttpClient cho tải file media.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Add("User-Agent", "SmartTourGuide/1.0");

        _cacheDir = FileSystem.CacheDirectory;
        _imgDir = Path.Combine(_cacheDir, "images");
        _audioDir = Path.Combine(_cacheDir, "audio");

        // 2) Tạo thư mục cache con cho ảnh và audio.
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
        // 1) Hủy đợt pre-cache cũ trước khi bắt đầu đợt mới.
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _precacheCts, newCts);
        oldCts?.Cancel();
        oldCts?.Dispose();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(newCts.Token, externalCt);
        var ct = linkedCts.Token;

        // 2) Thu thập toàn bộ URL cần tải (ảnh + audio).
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

        // 3) Giới hạn tải song song để giảm nghẽn mạng và tránh quá tải thiết bị.
        var semaphore = new SemaphoreSlim(3);

        var downloads = tasks.Select(async t =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();

                // Truyền token vào hàm tải để có thể dừng HTTP ngay khi bị cancel.
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
                // Luôn trả semaphore dù thành công hay lỗi.
                semaphore.Release();
            }
        });

        // 4) Chờ toàn bộ tác vụ kết thúc hoặc bị hủy.
        try { await Task.WhenAll(downloads); }
        catch (OperationCanceledException) { /* Bị hủy bởi đợt sync mới */ }
    }

    // ── CORE DOWNLOAD ENGINE (AN TOÀN & CHỐNG LỖI CHẠM FILE) ──────────────────

    private Task<string?> DownloadSafelyAsync(string url, string targetDir, CancellationToken ct)
    {
        // 1) Guard đầu vào.
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<string?>(null);

        string fileName = SanitizeFileName(url);
        string localPath = Path.Combine(targetDir, fileName);

        // 2) Nếu đã có file cache hoàn chỉnh thì dùng lại ngay.
        if (File.Exists(localPath)) return Task.FromResult<string?>(localPath);

        // 3) Chống tải trùng theo URL: tái sử dụng task đang chạy nếu có.
        return _activeDownloads.GetOrAdd(url, async key =>
        {
            // Dùng file .tmp để tránh đọc file dở khi đang ghi.
            string tempPath = localPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                // 4) Tải dạng stream để tiết kiệm RAM (quan trọng với file audio lớn).
                using var response = await _http.GetAsync(key, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                using var streamToReadFrom = await response.Content.ReadAsStreamAsync(ct);
                using var streamToWriteTo = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                await streamToReadFrom.CopyToAsync(streamToWriteTo, ct);
                streamToWriteTo.Close(); // Bắt buộc đóng File tạm trước khi Move

                // 5) Atomic write: ghi xong 100% mới đổi tên sang file chính thức.
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
                // Dọn file tạm nếu còn sót do lỗi/hủy giữa chừng.
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }

                // Gỡ URL khỏi danh sách tải đang chạy.
                _activeDownloads.TryRemove(key, out _);
            }
        });
    }

    // ── MAP TILE & TIỆN ÍCH ──────────────────────────────────────────────────

    public static void ConfigureMapTileCache()
    {
        // Tạo thư mục cache tile map để dùng chung trong app.
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
        // 1) Hủy mọi tiến trình tải ngầm trước khi xóa file.
        var oldCts = Interlocked.Exchange(ref _precacheCts, null);
        oldCts?.Cancel();
        oldCts?.Dispose();

        // 2) Xóa file cache theo kiểu an toàn, không làm văng app nếu file đang bị khóa.
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
    // Thống kê số lượng và dung lượng cache theo loại dữ liệu.
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