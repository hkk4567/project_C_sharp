using SQLite;
using SmartTourGuide.Mobile.Models;
using System.Text.Json;

namespace SmartTourGuide.Mobile.Services;
// LocalDatabase.cs
// Chức năng chính:
// Lưu và đọc POI trong SQLite.

// Kiểm tra có cache hay chưa.

// Lưu metadata thời gian sync gần nhất.

// Là nguồn fallback khi mất mạng.

// Quản lý cache media và tile map

// Mục đích file:
// 1) Quản lý SQLite local cho dữ liệu POI phục vụ offline.
// 2) Lưu thời điểm sync gần nhất để hiển thị trạng thái dữ liệu.
// 3) Là nguồn fallback khi API lỗi hoặc thiết bị mất mạng.

/// <summary>
/// Quản lý SQLite local — lưu POI + Tour để dùng khi offline.
/// File DB nằm tại: FileSystem.AppDataDirectory/smarttour.db
/// </summary>
public class LocalDatabase
{
    // Kết nối SQLite dùng lại trong suốt vòng đời service.
    private SQLiteAsyncConnection? _db;

    // Lock khởi tạo DB để tránh tạo kết nối/tạo bảng chồng nhau giữa nhiều luồng.
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    // ── KHỞI TẠO ─────────────────────────────────────────────────────────────
    public async Task InitAsync()
    {
        // 1) Đã khởi tạo rồi thì thoát sớm.
        if (_db != null) return;

        // 2) Đảm bảo chỉ một luồng được phép khởi tạo DB tại một thời điểm.
        await _initLock.WaitAsync();
        try
        {
            if (_db != null) return; // double-check sau khi lấy lock

            // 3) Tạo kết nối và đảm bảo các bảng cần thiết tồn tại.
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "smarttour.db");
            _db = new SQLiteAsyncConnection(dbPath);

            await _db.CreateTableAsync<PoiLocalModel>();
            await _db.CreateTableAsync<TourLocalModel>();
            await _db.CreateTableAsync<SyncMetadata>();
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── POI ───────────────────────────────────────────────────────────────────
    public async Task SavePoisAsync(List<PoiModel> pois)
    {
        // 1) Đảm bảo DB sẵn sàng.
        await InitAsync();

        // 2) Map model API -> model local, serialize list media sang JSON.
        var locals = pois.Select(p => new PoiLocalModel
        {
            Id = p.Id,
            Name = p.Name ?? "",
            Description = p.Description ?? "",
            Address = p.Address ?? "",
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            TriggerRadius = p.TriggerRadius,
            Priority = p.Priority,
            AudioUrlsJson = JsonSerializer.Serialize(p.AudioUrls ?? new()),
            ImageUrlsJson = JsonSerializer.Serialize(p.ImageUrls ?? new()),
            CachedAt = DateTime.UtcNow
        }).ToList();

        // 3) Ghi theo transaction: xóa bản cũ và chèn dữ liệu mới atomically.
        await _db!.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<PoiLocalModel>();
            foreach (var local in locals)
                conn.Insert(local);
        });
    }

    public async Task<List<PoiModel>> GetPoisAsync()
    {
        // 1) Đảm bảo DB sẵn sàng.
        await InitAsync();

        // 2) Đọc toàn bộ POI local.
        var locals = await _db!.Table<PoiLocalModel>().ToListAsync();

        // 3) Map local -> model dùng cho UI và deserialize media JSON.
        return locals.Select(l => new PoiModel
        {
            Id = l.Id,
            Name = l.Name,
            Description = l.Description,
            Address = l.Address,
            Latitude = l.Latitude,
            Longitude = l.Longitude,
            TriggerRadius = l.TriggerRadius,
            Priority = l.Priority,
            AudioUrls = JsonSerializer.Deserialize<List<string>>(l.AudioUrlsJson ?? "[]") ?? new(),
            ImageUrls = JsonSerializer.Deserialize<List<string>>(l.ImageUrlsJson ?? "[]") ?? new(),
        }).ToList();
    }

    public async Task<bool> HasCachedPoisAsync()
    {
        // Kiểm tra nhanh local cache có dữ liệu POI hay chưa.
        await InitAsync();
        return await _db!.Table<PoiLocalModel>().CountAsync() > 0;
    }

    // ── SYNC METADATA ────────────────────────────────────────────────────────
    public async Task<DateTime?> GetLastSyncTimeAsync()
    {
        // Lấy mốc thời gian sync gần nhất (nếu có).
        await InitAsync();
        var meta = await _db!.Table<SyncMetadata>().FirstOrDefaultAsync();
        return meta?.LastSync;
    }

    public async Task UpdateSyncTimeAsync()
    {
        // Lưu đè mốc sync mới nhất bằng thời gian hiện tại UTC.
        await InitAsync();
        await _db!.DeleteAllAsync<SyncMetadata>();
        await _db.InsertAsync(new SyncMetadata { LastSync = DateTime.UtcNow });
    }
}

// ── LOCAL MODEL CLASSES ───────────────────────────────────────────────────────

[Table("Pois")]
public class PoiLocalModel
{
    // Bảng lưu POI local để hiển thị offline.
    [PrimaryKey]
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Address { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double TriggerRadius { get; set; }
    public int Priority { get; set; }
    public string? AudioUrlsJson { get; set; }
    public string? ImageUrlsJson { get; set; }
    public DateTime CachedAt { get; set; }
}

[Table("Tours")]
public class TourLocalModel
{
    // Bảng dự phòng cho dữ liệu tour local (mở rộng về sau).
    [PrimaryKey]
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? PoisJson { get; set; }
    public DateTime CachedAt { get; set; }
}

[Table("SyncMetadata")]
public class SyncMetadata
{
    // Bảng lưu metadata đồng bộ dữ liệu.
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public DateTime LastSync { get; set; }
}
