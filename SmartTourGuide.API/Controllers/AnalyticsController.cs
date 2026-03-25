using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
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
    public AnalyticsController(AppDbContext context) => _context = context;

    // 1. Ghi nhận sự kiện nghe audio
    // POST: api/analytics/poi-listen
    [HttpPost("poi-listen")]
    public async Task<IActionResult> LogListen([FromBody] PoiListenLogDto dto)
    {
        _context.PoiListenLogs.Add(new PoiListenLog
        {
            PoiId = dto.PoiId,
            DeviceId = dto.DeviceId,         // 0 = ẩn danh (không cần đăng nhập)
            ListenDurationSec = dto.ListenDurationSec,
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return Ok();
    }

    // 2. Top POI — Đếm số record theo PoiId, lấy nhiều nhất lên đầu
    // GET: api/analytics/top-pois?top=10
    [HttpGet("top-pois")]
    public async Task<ActionResult<List<TopPoiDto>>> GetTopPois([FromQuery] int top = 10)
    {
        var result = await _context.PoiListenLogs
            .GroupBy(l => l.PoiId)
            .Select(g => new {
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
            .Select(g => new {
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
                UserId = 0,          // Ẩn danh — không trả về UserId
                Latitude = x.Latitude,
                Longitude = x.Longitude,
                Timestamp = x.Timestamp
            })
            .ToListAsync();
        return Ok(points);
    }
}