using SmartTourGuide.Mobile.Models;

namespace SmartTourGuide.Mobile.Services;

/// <summary>
/// Tải trước và quản lý cache cho ảnh, audio, map tile.
/// Tất cả file lưu tại FileSystem.CacheDirectory.
/// </summary>
public class CacheService
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly string _imgDir;
    private readonly string _audioDir;

    public event Action<int, int>? ProgressChanged; // (done, total)

    public CacheService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("User-Agent", "SmartTourGuide/1.0");

        _cacheDir = FileSystem.CacheDirectory;
        _imgDir = Path.Combine(_cacheDir, "images");
        _audioDir = Path.Combine(_cacheDir, "audio");

        Directory.CreateDirectory(_imgDir);
        Directory.CreateDirectory(_audioDir);
    }

    // ── ẢNH ──────────────────────────────────────────────────────────────────

    /// <summary>Lấy đường dẫn ảnh local; download nếu chưa có.</summary>
    public async Task<string?> GetLocalImagePathAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            string fileName = SanitizeFileName(url);
            string localPath = Path.Combine(_imgDir, fileName);

            if (File.Exists(localPath)) return localPath;

            var data = await _http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, data);
            return localPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cache/Image] {ex.Message}");
            return null;
        }
    }

    public bool IsImageCached(string url)
    {
        string localPath = Path.Combine(_imgDir, SanitizeFileName(url));
        return File.Exists(localPath);
    }

    // ── AUDIO ─────────────────────────────────────────────────────────────────

    /// <summary>Lấy đường dẫn audio local; download nếu chưa có.</summary>
    public async Task<string?> GetLocalAudioPathAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            string fileName = SanitizeFileName(url);
            string localPath = Path.Combine(_audioDir, fileName);

            if (File.Exists(localPath)) return localPath;

            var data = await _http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, data);
            return localPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cache/Audio] {ex.Message}");
            return null;
        }
    }

    public bool IsAudioCached(string url)
    {
        string localPath = Path.Combine(_audioDir, SanitizeFileName(url));
        return File.Exists(localPath);
    }

    // ── PRE-CACHE TẤT CẢ POI ─────────────────────────────────────────────────

    /// <summary>
    /// Tải trước toàn bộ ảnh + audio của tất cả POI.
    /// Chạy background — không block UI.
    /// </summary>
    public async Task PreCacheAllAsync(List<PoiModel> pois, string baseApiUrl,
                                       CancellationToken ct = default)
    {
        // Thu thập tất cả URL cần download
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

        // Download song song tối đa 3 file cùng lúc
        var semaphore = new SemaphoreSlim(3);

        var downloads = tasks.Select(async t =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (t.type == "img")
                    await GetLocalImagePathAsync(t.url);
                else
                    await GetLocalAudioPathAsync(t.url);

                Interlocked.Increment(ref done);
                ProgressChanged?.Invoke(done, total);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(downloads);
    }

    // ── MAP TILE ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Mapsui tự cache tile vào thư mục temp sau lần đầu load.
    /// Hàm này cấu hình tile cache folder để persist qua sessions.
    /// Gọi một lần trong MauiProgram hoặc constructor MainPage.
    /// </summary>
    public static void ConfigureMapTileCache()
    {
        string tileDir = Path.Combine(FileSystem.CacheDirectory, "maptiles");
        Directory.CreateDirectory(tileDir);

    }

    // ── TIỆN ÍCH ─────────────────────────────────────────────────────────────

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
        foreach (var f in Directory.GetFiles(_imgDir)) File.Delete(f);
        foreach (var f in Directory.GetFiles(_audioDir)) File.Delete(f);
    }

    // ── PRIVATE ───────────────────────────────────────────────────────────────

    private static string SanitizeFileName(string url)
    {
        // Lấy phần filename từ URL, bỏ query string
        string name = Path.GetFileName(url.Split('?')[0]);
        // Loại bỏ ký tự không hợp lệ
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
