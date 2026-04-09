using SQLite;
using SmartTourGuide.Mobile.Models;
using System.Text.Json;

namespace SmartTourGuide.Mobile.Services;

/// <summary>
/// Quản lý SQLite local — lưu POI + Tour để dùng khi offline.
/// File DB nằm tại: FileSystem.AppDataDirectory/smarttour.db
/// </summary>
public class LocalDatabase
{
    private SQLiteAsyncConnection? _db;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    // ── KHỞI TẠO ─────────────────────────────────────────────────────────────
    public async Task InitAsync()
    {
        if (_db != null) return;

        await _initLock.WaitAsync();
        try
        {
            if (_db != null) return; // double-check sau khi lấy lock

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
        await InitAsync();

        var locals = pois.Select(p => new PoiLocalModel
        {
            Id            = p.Id,
            Name          = p.Name ?? "",
            Description   = p.Description ?? "",
            Address       = p.Address ?? "",
            Latitude      = p.Latitude,
            Longitude     = p.Longitude,
            TriggerRadius = p.TriggerRadius,
            Priority      = p.Priority,
            AudioUrlsJson = JsonSerializer.Serialize(p.AudioUrls ?? new()),
            ImageUrlsJson = JsonSerializer.Serialize(p.ImageUrls ?? new()),
            CachedAt      = DateTime.UtcNow
        }).ToList();

        await _db!.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<PoiLocalModel>();
            foreach (var local in locals)
                conn.Insert(local);
        });
    }

    public async Task<List<PoiModel>> GetPoisAsync()
    {
        await InitAsync();

        var locals = await _db!.Table<PoiLocalModel>().ToListAsync();

        return locals.Select(l => new PoiModel
        {
            Id            = l.Id,
            Name          = l.Name,
            Description   = l.Description,
            Address       = l.Address,
            Latitude      = l.Latitude,
            Longitude     = l.Longitude,
            TriggerRadius = l.TriggerRadius,
            Priority      = l.Priority,
            AudioUrls     = JsonSerializer.Deserialize<List<string>>(l.AudioUrlsJson ?? "[]") ?? new(),
            ImageUrls     = JsonSerializer.Deserialize<List<string>>(l.ImageUrlsJson ?? "[]") ?? new(),
        }).ToList();
    }

    public async Task<bool> HasCachedPoisAsync()
    {
        await InitAsync();
        return await _db!.Table<PoiLocalModel>().CountAsync() > 0;
    }

    // ── TOUR ──────────────────────────────────────────────────────────────────
    public async Task SaveToursAsync(List<TourModel> tours)
    {
        await InitAsync();

        var locals = tours.Select(t => new TourLocalModel
        {
            Id          = t.Id,
            Name        = t.Name ?? "",
            Description = t.Description ?? "",
            PoisJson    = JsonSerializer.Serialize(t.Pois ?? new()),
            CachedAt    = DateTime.UtcNow
        }).ToList();

        await _db!.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<TourLocalModel>();
            conn.InsertAll(locals);
        });
    }

    public async Task<List<TourModel>> GetToursAsync()
    {
        await InitAsync();

        var locals = await _db!.Table<TourLocalModel>().ToListAsync();

        return locals.Select(l => new TourModel
        {
            Id          = l.Id,
            Name        = l.Name,
            Description = l.Description,
            Pois        = JsonSerializer.Deserialize<List<TourDetailModel>>(l.PoisJson ?? "[]") ?? new(),
        }).ToList();
    }

    public async Task<bool> HasCachedToursAsync()
    {
        await InitAsync();
        return await _db!.Table<TourLocalModel>().CountAsync() > 0;
    }

    // ── SYNC METADATA ────────────────────────────────────────────────────────
    public async Task<DateTime?> GetLastSyncTimeAsync()
    {
        await InitAsync();
        var meta = await _db!.Table<SyncMetadata>().FirstOrDefaultAsync();
        return meta?.LastSync;
    }

    public async Task UpdateSyncTimeAsync()
    {
        await InitAsync();
        await _db!.DeleteAllAsync<SyncMetadata>();
        await _db.InsertAsync(new SyncMetadata { LastSync = DateTime.UtcNow });
    }
}

// ── LOCAL MODEL CLASSES ───────────────────────────────────────────────────────

[Table("Pois")]
public class PoiLocalModel
{
    [PrimaryKey]
    public int      Id            { get; set; }
    public string   Name          { get; set; } = "";
    public string   Description   { get; set; } = "";
    public string   Address       { get; set; } = "";
    public double   Latitude      { get; set; }
    public double   Longitude     { get; set; }
    public double   TriggerRadius { get; set; }
    public int      Priority      { get; set; }
    public string?  AudioUrlsJson { get; set; }
    public string?  ImageUrlsJson { get; set; }
    public DateTime CachedAt      { get; set; }
}

[Table("Tours")]
public class TourLocalModel
{
    [PrimaryKey]
    public int     Id          { get; set; }
    public string  Name        { get; set; } = "";
    public string  Description { get; set; } = "";
    public string? PoisJson    { get; set; }
    public DateTime CachedAt  { get; set; }
}

[Table("SyncMetadata")]
public class SyncMetadata
{
    [PrimaryKey, AutoIncrement]
    public int      Id       { get; set; }
    public DateTime LastSync { get; set; }
}
