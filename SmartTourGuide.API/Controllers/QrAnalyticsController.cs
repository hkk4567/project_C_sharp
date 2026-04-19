using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Services;

using SmartTourGuide.Shared.DTOs;
using SmartTourGuide.Shared.Enums;

namespace SmartTourGuide.API.Controllers;

[ApiController]
[Route("api/qr-analytics")]
public class QrAnalyticsController : ControllerBase
{
    private readonly AppDbContext _context;

    public QrAnalyticsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("log-scan/{poiId}")]
    public async Task<IActionResult> LogScan(int poiId, [FromBody] LogScanRequest request)
    {
        var deviceId = request.DeviceId ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers.UserAgent.ToString();

        // Chạm chốt chặn tương tự web (1 giây) để chống spam từ app
        var recentLogExists = await _context.QrScanLogs
            .AnyAsync(log => log.PoiId == poiId
                          && log.DeviceId == deviceId
                          && log.ScannedAt >= VietnamTime.Now.AddSeconds(-1));

        if (!recentLogExists)
        {
            _context.QrScanLogs.Add(new Data.Entities.QrScanLog
            {
                PoiId = poiId,
                ScannedAt = VietnamTime.Now,
                DeviceId = deviceId,
                UserAgent = userAgent + " (MobileApp)"
            });
            await _context.SaveChangesAsync();
        }
        return Ok();
    }

    public class LogScanRequest
    {
        public string? DeviceId { get; set; }
    }

    [HttpGet("owner/qr-scan-stats")]
    public async Task<ActionResult<List<OwnerQrScanStatsDto>>> GetOwnerQrScanStats([FromHeader(Name = "X-User-Name")] string? headerUsername)
    {
        var username = headerUsername ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            return Unauthorized();

        var requester = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (requester == null)
            return Unauthorized();

        var logs = _context.QrScanLogs
            .Join(_context.Pois, l => l.PoiId, p => p.Id, (l, p) => new { l, p });

        if (requester.Role != UserRole.Admin)
        {
            logs = logs.Where(x => x.p.OwnerId == requester.Id);
        }

        var result = await logs
            .GroupBy(x => new { x.p.Id, x.p.Name })
            .Select(g => new OwnerQrScanStatsDto
            {
                PoiId = g.Key.Id,
                PoiName = g.Key.Name,
                TotalQrScans = g.Count()
            })
            .OrderByDescending(x => x.TotalQrScans)
            .ToListAsync();

        return Ok(result);
    }
}