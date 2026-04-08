using System.Text.Json;

namespace SmartTourGuide.Mobile.Services;

/// <summary>
/// Gọi OSRM để lấy tuyến đường thực tế, cache kết quả vào file JSON.
///
/// Chiến lược cache:
///   Online  → gọi OSRM → lưu cache → trả về route đường thực
///   Offline → đọc cache → trả về route cũ (không fallback đường thẳng)
///   Offline + không có cache → mới fallback đường thẳng
///
/// Cache key được tạo từ tọa độ các POI (làm tròn 4 chữ số thập phân),
/// bỏ qua vị trí người dùng (thay đổi liên tục) để cache ổn định hơn.
///
/// File lưu tại: FileSystem.CacheDirectory/routes/{key}.json
/// </summary>
public class RouteService
{
    // ── Cấu hình ─────────────────────────────────────────────────────────────
    private const string OsrmBaseUrl = "https://router.project-osrm.org/route/v1/foot";
    private const int MaxWaypointsPerReq = 25;       // Giới hạn của OSRM public API
    private const int CoordPrecision = 4;        // Làm tròn khi tạo cache key (≈11 m)
    private const double MaxCacheAgeDays = 30;       // Sau 30 ngày sẽ thử refresh nếu online

    private readonly HttpClient _http;
    private readonly string _routeCacheDir;

    public RouteService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        _routeCacheDir = Path.Combine(FileSystem.CacheDirectory, "routes");
        Directory.CreateDirectory(_routeCacheDir);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API CHÍNH
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Trả về danh sách tọa độ bám theo đường đi thực tế.
    ///
    /// <paramref name="allWaypoints"/>      — toàn bộ điểm routing (gồm vị trí người dùng).
    /// <paramref name="waypointsForCache"/> — chỉ các POI, dùng để tạo cache key ổn định.
    /// </summary>
    public async Task<RouteResult> GetRoadRouteAsync(
        IEnumerable<MauiLocation.Location> allWaypoints,
        IEnumerable<MauiLocation.Location> waypointsForCache,
        CancellationToken cancellationToken = default)
    {
        var all = allWaypoints.ToList();
        var forKey = waypointsForCache.ToList();

        if (all.Count < 2)
            return RouteResult.StraightLine(all);

        var cacheKey = BuildCacheKey(forKey);
        var cacheFile = Path.Combine(_routeCacheDir, $"{cacheKey}.json");

        // ── 1. Đọc cache hiện có (nếu có) ────────────────────────────────
        var cached = await TryLoadCacheAsync(cacheFile);

        bool cacheIsStale = cached == null ||
                            (DateTime.UtcNow - cached.SavedAt).TotalDays > MaxCacheAgeDays;

        // ── 2. Nếu cache còn mới → dùng ngay, không gọi OSRM ────────────
        if (!cacheIsStale)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[RouteService] Cache mới → {cached!.Points.Count} điểm (key={cacheKey})");
            return new RouteResult(cached.Points, RouteSource.Cache);
        }

        // ── 3. Cache cũ hoặc chưa có → thử gọi OSRM ─────────────────────
        try
        {
            var osrmPoints = await FetchFromOsrmAsync(all, cancellationToken);
            await SaveCacheAsync(cacheFile, osrmPoints);

            System.Diagnostics.Debug.WriteLine(
                $"[RouteService] OSRM OK → {osrmPoints.Count} điểm, lưu cache (key={cacheKey})");

            return new RouteResult(osrmPoints, RouteSource.Osrm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RouteService] OSRM lỗi: {ex.Message}");
        }

        // ── 4. OSRM thất bại → dùng cache cũ nếu có ─────────────────────
        if (cached != null)
        {
            double ageDays = Math.Round((DateTime.UtcNow - cached.SavedAt).TotalDays, 1);
            System.Diagnostics.Debug.WriteLine(
                $"[RouteService] Offline → dùng cache cũ ({ageDays} ngày, key={cacheKey})");
            return new RouteResult(cached.Points, RouteSource.CacheFallback);
        }

        // ── 5. Không có gì → đường thẳng ─────────────────────────────────
        System.Diagnostics.Debug.WriteLine("[RouteService] Không có cache → vẽ đường thẳng.");
        return RouteResult.StraightLine(all);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CACHE — ĐỌC / GHI
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tạo cache key từ tọa độ POI (làm tròn 4 chữ số) rồi hash thành chuỗi hex ngắn.
    /// Ví dụ: "route_3f8a1b2c"
    /// </summary>
    private static string BuildCacheKey(IEnumerable<MauiLocation.Location> pts)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string raw = string.Join("|", pts.Select(p =>
            $"{Math.Round(p.Latitude, CoordPrecision).ToString(inv)}," +
            $"{Math.Round(p.Longitude, CoordPrecision).ToString(inv)}"));

        // Hash đơn giản, ổn định giữa các lần chạy (không dùng string.GetHashCode())
        uint hash = 2166136261u;
        foreach (char c in raw)
        {
            hash ^= c;
            hash *= 16777619u; // FNV-1a
        }
        return $"route_{hash:x8}";
    }

    private async Task<RouteCache?> TryLoadCacheAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<RouteCache>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RouteService] Đọc cache lỗi: {ex.Message}");
            return null;
        }
    }

    private async Task SaveCacheAsync(string filePath, List<MauiLocation.Location> points)
    {
        try
        {
            var json = JsonSerializer.Serialize(new RouteCache
            {
                SavedAt = DateTime.UtcNow,
                Points = points
            });
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RouteService] Ghi cache lỗi: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  OSRM — GỌI API
    // ════════════════════════════════════════════════════════════════════════

    private async Task<List<MauiLocation.Location>> FetchFromOsrmAsync(
        List<MauiLocation.Location> pts,
        CancellationToken cancellationToken)
    {
        if (pts.Count <= MaxWaypointsPerReq)
            return await FetchSegmentAsync(pts, cancellationToken);

        // Chia batch nếu > MaxWaypointsPerReq điểm
        var combined = new List<MauiLocation.Location>();
        int step = MaxWaypointsPerReq - 1; // -1 để điểm cuối đoạn trước = điểm đầu đoạn sau

        for (int i = 0; i < pts.Count - 1; i += step)
        {
            int end = Math.Min(i + MaxWaypointsPerReq, pts.Count);
            var segment = pts.GetRange(i, end - i);
            var result = await FetchSegmentAsync(segment, cancellationToken);

            if (combined.Count > 0 && result.Count > 0)
                result.RemoveAt(0); // bỏ điểm trùng ở đầu đoạn tiếp theo

            combined.AddRange(result);
        }
        return combined;
    }

    private async Task<List<MauiLocation.Location>> FetchSegmentAsync(
        List<MauiLocation.Location> pts,
        CancellationToken cancellationToken)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        // OSRM yêu cầu thứ tự: longitude,latitude (không phải lat,lon)
        var coordStr = string.Join(";", pts.Select(p =>
            $"{p.Longitude.ToString("F6", inv)},{p.Latitude.ToString("F6", inv)}"));

        var url = $"{OsrmBaseUrl}/{coordStr}?overview=full&geometries=geojson&steps=false";
        System.Diagnostics.Debug.WriteLine($"[RouteService] GET {url}");

        var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseOsrmGeoJson(json);
    }

    private static List<MauiLocation.Location> ParseOsrmGeoJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var code) && code.GetString() != "Ok")
            throw new Exception($"OSRM code={code.GetString()}");

        var routes = root.GetProperty("routes");
        if (routes.GetArrayLength() == 0)
            throw new Exception("OSRM không tìm thấy tuyến đường.");

        var coords = routes[0].GetProperty("geometry").GetProperty("coordinates");
        var result = new List<MauiLocation.Location>();

        foreach (var coord in coords.EnumerateArray())
            result.Add(new MauiLocation.Location(coord[1].GetDouble(), coord[0].GetDouble()));

        return result;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TIỆN ÍCH
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Xóa toàn bộ cache tuyến đường.</summary>
    public void ClearRouteCache()
    {
        foreach (var f in Directory.GetFiles(_routeCacheDir, "*.json"))
            File.Delete(f);
        System.Diagnostics.Debug.WriteLine("[RouteService] Đã xóa toàn bộ route cache.");
    }

    /// <summary>Kích thước cache tuyến đường (MB).</summary>
    public double GetCacheSizeMb()
    {
        if (!Directory.Exists(_routeCacheDir)) return 0;
        long bytes = Directory.GetFiles(_routeCacheDir, "*.json")
                              .Sum(f => new FileInfo(f).Length);
        return bytes / 1024.0 / 1024.0;
    }
}

// ════════════════════════════════════════════════════════════════════════════
//  DATA CLASSES
// ════════════════════════════════════════════════════════════════════════════

/// <summary>Dữ liệu lưu vào file JSON cache.</summary>
public class RouteCache
{
    public DateTime SavedAt { get; set; }
    public List<MauiLocation.Location> Points { get; set; } = new();
}

/// <summary>Nguồn gốc của route — dùng để hiện thị trạng thái cho user.</summary>
public enum RouteSource
{
    Osrm,           // Lấy trực tiếp từ OSRM (online, mới nhất)
    Cache,          // Cache còn mới (≤ MaxCacheAgeDays ngày)
    CacheFallback,  // OSRM lỗi → dùng cache cũ (offline mode)
    StraightLine    // Không có OSRM lẫn cache → đường thẳng
}

/// <summary>Kết quả trả về từ RouteService.</summary>
public class RouteResult
{
    public List<MauiLocation.Location> Points { get; }
    public RouteSource Source { get; }

    public RouteResult(List<MauiLocation.Location> points, RouteSource source)
    {
        Points = points;
        Source = source;
    }

    public static RouteResult StraightLine(List<MauiLocation.Location> pts)
        => new(pts, RouteSource.StraightLine);

    /// <summary>Thông báo trạng thái hiện lên UI cho user.</summary>
    public string StatusMessage => Source switch
    {
        RouteSource.Osrm => "🗺️ Tuyến đường mới nhất",
        RouteSource.Cache => "🗺️ Tuyến đường từ cache",
        RouteSource.CacheFallback => "📵 Offline — đang dùng tuyến đường đã lưu",
        RouteSource.StraightLine => "⚠️ Offline — không có cache, hiển thị đường thẳng",
        _ => ""
    };
}