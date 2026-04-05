using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.Enums;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;
/// <summary>
/// Controller xử lý toàn bộ 4 tính năng Analytics của slide:
/// 1. POST poi-listen     → Ghi nhận lượt nghe audio
/// 2. GET  top-pois       → Top địa điểm nghe nhiều nhất
/// 3. GET  avg-listen-time → Thời gian trung bình nghe 1 POI
/// 4. GET  heatmap        → Tọa độ vị trí người dùng để vẽ heatmap
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _context;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> RecentListenSessions = new();
    public AnalyticsController(AppDbContext context) => _context = context;

    private string GetCurrentUsername()
    {
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        var headerName = HttpContext.Request.Headers["X-User-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerName)) return headerName;

        return string.Empty;
    }

    private async Task<User?> GetRequesterAsync()
    {
        var username = GetCurrentUsername();
        if (string.IsNullOrWhiteSpace(username)) return null;

        return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
    }

    private static bool CanAccessOwnerData(User requester, int ownerId)
    {
        return requester.Role == UserRole.Admin || requester.Id == ownerId;
    }

    private sealed class OwnerPoiArea
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; }
    }

    private static double GetLatDelta(double meters) => meters / 111_320d;

    private static double GetLonDelta(double meters, double latitude)
    {
        var cos = Math.Cos(latitude * Math.PI / 180d);
        if (Math.Abs(cos) < 1e-6) cos = 1e-6;
        return meters / (111_320d * cos);
    }

    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6_371_000d;
        var dLat = (lat2 - lat1) * Math.PI / 180d;
        var dLon = (lon2 - lon1) * Math.PI / 180d;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180d) * Math.Cos(lat2 * Math.PI / 180d)
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadius * c;
    }

    private static bool IsInAnyOwnerArea(UserLocationLog log, IReadOnlyCollection<OwnerPoiArea> areas, double bufferMeters)
    {
        foreach (var area in areas)
        {
            var d = DistanceMeters(log.Latitude, log.Longitude, area.Latitude, area.Longitude);
            if (d <= area.RadiusMeters + bufferMeters)
                return true;
        }

        return false;
    }

    // 1. Ghi nhận sự kiện nghe audio
    // POST: api/analytics/poi-listen
    [HttpPost("poi-listen")]
    public async Task<IActionResult> LogListen([FromBody] PoiListenLogDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.SessionId))
        {
            var now = DateTime.UtcNow;
            if (RecentListenSessions.TryGetValue(dto.SessionId, out var lastSeen)
                && now - lastSeen < TimeSpan.FromMinutes(15))
            {
                return Ok(new { message = "Đã ghi nhận" });
            }

            RecentListenSessions[dto.SessionId] = now;
        }

        // Mỗi lần nghe hợp lệ sẽ tạo một record mới.
        _context.PoiListenLogs.Add(new PoiListenLog
        {
            PoiId = dto.PoiId,
            DeviceId = dto.DeviceId,
            ListenDurationSec = dto.ListenDurationSec,
            Timestamp = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return Ok(new { message = "Đã ghi nhận" });
    }

    // 2. Top POI — Đếm số record theo PoiId, lấy nhiều nhất lên đầu
    // GET: api/analytics/top-pois?top=10
    [HttpGet("top-pois")]
    public async Task<ActionResult<List<TopPoiDto>>> GetTopPois([FromQuery] int top = 10)
    {
        var result = await _context.PoiListenLogs
            .GroupBy(l => l.PoiId)
            .Select(g => new
            {
                PoiId = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .Join(_context.Pois,
                  stat => stat.PoiId,
                  poi => poi.Id,
                  (stat, poi) => new TopPoiDto
                  {
                      PoiId = stat.PoiId,
                      PoiName = poi.Name,
                      ListenCount = stat.Count
                  })
            .ToListAsync();
        return Ok(result);
    }

    // 3. Thời gian trung bình — AVG(ListenDurationSec) nhóm theo PoiId
    // GET: api/analytics/avg-listen-time
    [HttpGet("avg-listen-time")]
    public async Task<ActionResult<List<AvgListenTimeDto>>> GetAvgListenTime()
    {
        var result = await _context.PoiListenLogs
            .GroupBy(l => l.PoiId)
            .Select(g => new
            {
                PoiId = g.Key,
                Avg = g.Average(x => (double)x.ListenDurationSec)
            })
            .Join(_context.Pois,
                  stat => stat.PoiId,
                  poi => poi.Id,
                  (stat, poi) => new AvgListenTimeDto
                  {
                      PoiId = stat.PoiId,
                      PoiName = poi.Name,
                      AvgDurationSec = Math.Round(stat.Avg, 1)
                  })
            .ToListAsync();
        return Ok(result);
    }

    // 4.  Heatmap — Trả về danh sách tọa độ GPS ẩn danh (không có UserId)
    // GET: api/analytics/heatmap?hours=24
    [HttpGet("heatmap")]
    public async Task<ActionResult<List<LocationLogDto>>> GetHeatmap([FromQuery] int hours = 24)
    {
        var since = DateTime.UtcNow.AddHours(-hours);
        var points = await _context.UserLocationLogs
            .Where(x => x.Timestamp >= since)
            .Select(x => new LocationLogDto
            {
                UserId = x.UserId ?? 0, // Trả về 0 cho khách vãng lai
                DeviceId = x.DeviceId,
                Latitude = x.Latitude,
                Longitude = x.Longitude,
                Timestamp = x.Timestamp
            })
            .ToListAsync();
        return Ok(points);
    }

    // ===== OWNER ANALYTICS (lọc theo ownerId) =====

    [HttpGet("owner/{ownerId:int}/top-pois")]
    public async Task<ActionResult<List<TopPoiDto>>> GetOwnerTopPois(int ownerId, [FromQuery] int top = 10)
    {
        var requester = await GetRequesterAsync();
        if (requester == null) return Unauthorized("Chưa đăng nhập.");
        if (!CanAccessOwnerData(requester, ownerId)) return Forbid();

        var safeTop = Math.Clamp(top, 1, 100);

        var result = await _context.PoiListenLogs
            .Join(_context.Pois,
                log => log.PoiId,
                poi => poi.Id,
                (log, poi) => new { log, poi })
            .Where(x => x.poi.OwnerId == ownerId)
            .GroupBy(x => new { x.log.PoiId, x.poi.Name })
            .Select(g => new TopPoiDto
            {
                PoiId = g.Key.PoiId,
                PoiName = g.Key.Name,
                ListenCount = g.Count()
            })
            .OrderByDescending(x => x.ListenCount)
            .Take(safeTop)
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("owner/{ownerId:int}/summary")]
    public async Task<ActionResult<OwnerAnalyticsSummaryDto>> GetOwnerSummary(int ownerId)
    {
        var requester = await GetRequesterAsync();
        if (requester == null) return Unauthorized("Chưa đăng nhập.");
        if (!CanAccessOwnerData(requester, ownerId)) return Forbid();

        var now = DateTime.UtcNow;
        var sinceWeek = now.AddDays(-7);
        var sinceMonth = now.AddDays(-30);

        var ownerPoiIds = await _context.Pois
            .Where(p => p.OwnerId == ownerId)
            .Select(p => p.Id)
            .ToListAsync();

        if (ownerPoiIds.Count == 0)
        {
            return Ok(new OwnerAnalyticsSummaryDto { OwnerId = ownerId });
        }

        var weekCount = await _context.PoiListenLogs
            .Where(l => ownerPoiIds.Contains(l.PoiId) && l.Timestamp >= sinceWeek)
            .CountAsync();

        var monthQuery = _context.PoiListenLogs
            .Where(l => ownerPoiIds.Contains(l.PoiId) && l.Timestamp >= sinceMonth);

        var monthCount = await monthQuery.CountAsync();

        var avgDuration = await monthQuery
            .Select(x => (double?)x.ListenDurationSec)
            .AverageAsync() ?? 0d;

        var distinctPoisHeard = await _context.PoiListenLogs
            .Where(l => ownerPoiIds.Contains(l.PoiId))
            .Select(l => l.PoiId)
            .Distinct()
            .CountAsync();

        return Ok(new OwnerAnalyticsSummaryDto
        {
            OwnerId = ownerId,
            TotalPois = ownerPoiIds.Count,
            DistinctPoisHeard = distinctPoisHeard,
            TotalListensWeek = weekCount,
            TotalListensMonth = monthCount,
            AvgDurationSecMonth = Math.Round(avgDuration, 1)
        });
    }

    [HttpGet("admin/summary")]
    public async Task<ActionResult<AdminDashboardSummaryDto>> GetAdminSummary()
    {
        var sinceWeek = DateTime.UtcNow.Date.AddDays(-6);

        var totalPois = await _context.Pois.CountAsync();
        var pendingPois = await _context.Pois.CountAsync(p => p.Status == PoiStatus.Pending);
        var totalUsers = await _context.Users.CountAsync();
        var lockedUsers = await _context.Users.CountAsync(u => u.IsLocked);
        var totalTours = await _context.Tours.CountAsync();

        var weeklyGrouped = await _context.PoiListenLogs
            .Where(l => l.Timestamp >= sinceWeek)
            .GroupBy(l => l.Timestamp.Date)
            .Select(g => new AdminDailyCountDto
            {
                Date = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        var weeklyMap = weeklyGrouped.ToDictionary(x => x.Date, x => x.Count);
        var weeklySeries = new List<AdminDailyCountDto>();

        for (var date = sinceWeek; date <= DateTime.UtcNow.Date; date = date.AddDays(1))
        {
            weeklySeries.Add(new AdminDailyCountDto
            {
                Date = date,
                Count = weeklyMap.TryGetValue(date, out var count) ? count : 0
            });
        }

        var recentActivities = await _context.ActivityLogs
            .OrderByDescending(a => a.Timestamp)
            .Take(5)
            .Select(a => new ActivityLogDto
            {
                Id = a.Id,
                ActivityType = a.ActivityType,
                Description = a.Description,
                UserName = a.UserName,
                Timestamp = a.Timestamp,
                IpAddress = a.IpAddress
            })
            .ToListAsync();

        return Ok(new AdminDashboardSummaryDto
        {
            TotalPois = totalPois,
            PendingPois = pendingPois,
            TotalUsers = totalUsers,
            LockedUsers = lockedUsers,
            TotalTours = totalTours,
            TotalListenEventsWeek = weeklySeries.Sum(x => x.Count),
            WeeklyListenSeries = weeklySeries,
            RecentActivities = recentActivities
        });
    }

    [HttpGet("owner/{ownerId:int}/avg-listen-time")]
    public async Task<ActionResult<List<AvgListenTimeDto>>> GetOwnerAvgListenTime(int ownerId, [FromQuery] int top = 20)
    {
        var requester = await GetRequesterAsync();
        if (requester == null) return Unauthorized("Chưa đăng nhập.");
        if (!CanAccessOwnerData(requester, ownerId)) return Forbid();

        var safeTop = Math.Clamp(top, 1, 200);

        var result = await _context.PoiListenLogs
            .Join(_context.Pois,
                log => log.PoiId,
                poi => poi.Id,
                (log, poi) => new { log, poi })
            .Where(x => x.poi.OwnerId == ownerId)
            .GroupBy(x => new { x.log.PoiId, x.poi.Name })
            .Select(g => new AvgListenTimeDto
            {
                PoiId = g.Key.PoiId,
                PoiName = g.Key.Name,
                AvgDurationSec = Math.Round(g.Average(x => (double)x.log.ListenDurationSec), 1)
            })
            .OrderByDescending(x => x.AvgDurationSec)
            .Take(safeTop)
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("owner/{ownerId:int}/daily-listens")]
    public async Task<ActionResult<List<OwnerDailyListenDto>>> GetOwnerDailyListens(int ownerId, [FromQuery] int days = 7)
    {
        var requester = await GetRequesterAsync();
        if (requester == null) return Unauthorized("Chưa đăng nhập.");
        if (!CanAccessOwnerData(requester, ownerId)) return Forbid();

        var safeDays = Math.Clamp(days, 1, 90);
        var startDate = DateTime.UtcNow.Date.AddDays(-(safeDays - 1));
        var endDate = DateTime.UtcNow.Date;

        var grouped = await _context.PoiListenLogs
            .Join(_context.Pois,
                log => log.PoiId,
                poi => poi.Id,
                (log, poi) => new { log, poi })
            .Where(x => x.poi.OwnerId == ownerId && x.log.Timestamp >= startDate)
            .GroupBy(x => x.log.Timestamp.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync();

        var map = grouped.ToDictionary(x => x.Date, x => x.Count);

        var result = new List<OwnerDailyListenDto>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            result.Add(new OwnerDailyListenDto
            {
                Date = date,
                ListenCount = map.TryGetValue(date, out var c) ? c : 0
            });
        }

        return Ok(result);
    }

    [HttpGet("owner/{ownerId:int}/movement")]
    public async Task<ActionResult<List<LocationLogDto>>> GetOwnerMovement(
        int ownerId,
        [FromQuery] int hours = 24,
        [FromQuery] int maxPoints = 300,
        [FromQuery] double areaBufferMeters = 20)
    {
        var requester = await GetRequesterAsync();
        if (requester == null) return Unauthorized("Chưa đăng nhập.");
        if (!CanAccessOwnerData(requester, ownerId)) return Forbid();

        var safeHours = Math.Clamp(hours, 1, 720);
        var safeMaxPoints = Math.Clamp(maxPoints, 10, 2000);
        var safeBuffer = Math.Clamp(areaBufferMeters, 0, 500);

        var ownerAreas = await _context.Pois
            .Where(p => p.OwnerId == ownerId)
            .Select(p => new OwnerPoiArea
            {
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                RadiusMeters = p.GeofenceSetting != null && p.GeofenceSetting.TriggerRadiusInMeters > 0
                    ? p.GeofenceSetting.TriggerRadiusInMeters
                    : 50
            })
            .ToListAsync();

        if (ownerAreas.Count == 0) return Ok(new List<LocationLogDto>());

        var minLat = double.MaxValue;
        var maxLat = double.MinValue;
        var minLon = double.MaxValue;
        var maxLon = double.MinValue;

        foreach (var area in ownerAreas)
        {
            var latDelta = GetLatDelta(area.RadiusMeters + safeBuffer);
            var lonDelta = GetLonDelta(area.RadiusMeters + safeBuffer, area.Latitude);

            minLat = Math.Min(minLat, area.Latitude - latDelta);
            maxLat = Math.Max(maxLat, area.Latitude + latDelta);
            minLon = Math.Min(minLon, area.Longitude - lonDelta);
            maxLon = Math.Max(maxLon, area.Longitude + lonDelta);
        }

        var since = DateTime.UtcNow.AddHours(-safeHours);

        var rawPoints = await _context.UserLocationLogs
            .Where(x => x.Timestamp >= since
                        && x.Latitude >= minLat && x.Latitude <= maxLat
                        && x.Longitude >= minLon && x.Longitude <= maxLon)
            .OrderByDescending(x => x.Timestamp)
            .Take(20000)
            .ToListAsync();

        var filtered = rawPoints
            .Where(x => IsInAnyOwnerArea(x, ownerAreas, safeBuffer))
            .OrderBy(x => x.Timestamp)
            .ToList();

        if (filtered.Count == 0) return Ok(new List<LocationLogDto>());

        if (filtered.Count > safeMaxPoints)
        {
            var sampled = new List<UserLocationLog>(safeMaxPoints);
            var step = (double)(filtered.Count - 1) / (safeMaxPoints - 1);
            for (var i = 0; i < safeMaxPoints; i++)
            {
                var idx = (int)Math.Round(i * step);
                if (idx >= filtered.Count) idx = filtered.Count - 1;
                sampled.Add(filtered[idx]);
            }
            filtered = sampled;
        }

        var result = filtered.Select(x => new LocationLogDto
        {
            UserId = 0,
            Latitude = x.Latitude,
            Longitude = x.Longitude,
            Timestamp = x.Timestamp
        }).ToList();

        return Ok(result);
    }

    [HttpGet("owner/{ownerId:int}/heatmap")]
    public async Task<ActionResult<List<OwnerHeatmapPointDto>>> GetOwnerHeatmap(
        int ownerId,
        [FromQuery] int hours = 24,
        [FromQuery] int maxPoints = 50,
        [FromQuery] double areaBufferMeters = 20,
        [FromQuery] double cellSizeMeters = 80)
    {
        var requester = await GetRequesterAsync();
        if (requester == null) return Unauthorized("Chưa đăng nhập.");
        if (!CanAccessOwnerData(requester, ownerId)) return Forbid();

        var safeMaxPoints = Math.Clamp(maxPoints, 5, 500);
        var hotspots = await _context.Pois
            .Where(p => p.OwnerId == ownerId)
            .GroupJoin(
                _context.PoiListenLogs,
                poi => poi.Id,
                log => log.PoiId,
                (poi, logs) => new
                {
                    poi.Id,
                    poi.Name,
                    poi.Latitude,
                    poi.Longitude,
                    HitCount = logs.Count()
                })
            .OrderByDescending(x => x.HitCount)
            .ThenBy(x => x.Name)
            .Take(safeMaxPoints)
            .Select(x => new OwnerHeatmapPointDto
            {
                PoiId = x.Id,
                Name = x.Name,
                Latitude = x.Latitude,
                Longitude = x.Longitude,
                HitCount = x.HitCount
            })
            .ToListAsync();

        return Ok(hotspots);
    }
}