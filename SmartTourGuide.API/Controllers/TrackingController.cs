using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TrackingController : ControllerBase
{
    private readonly AppDbContext _context;

    public TrackingController(AppDbContext context)
    {
        _context = context;
    }

    // 1. API Gửi vị trí hiện tại (Gọi liên tục từ Mobile)
    // POST: api/tracking
    [HttpPost]
    public async Task<IActionResult> ReportLocation([FromBody] LocationLogDto dto)
    {
        // Không check UserExists nữa vì ta cho phép khách vãng lai

        var log = new UserLocationLog
        {
            // Nếu dto.UserId <= 0 thì lưu null
            UserId = dto.UserId > 0 ? dto.UserId : null,
            DeviceId = dto.DeviceId, // Frontend gửi UUID của máy lên
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            Timestamp = dto.Timestamp == default ? DateTime.Now : dto.Timestamp
        };

        _context.UserLocationLogs.Add(log);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã cập nhật vị trí" });
    }

    // 2. API Xem lịch sử di chuyển của 1 User (Dành cho Admin/Chủ gian hàng)
    // GET: api/tracking/history/5?date=2023-10-20
    [HttpGet("history/{userId}")]
    public async Task<ActionResult<IEnumerable<LocationLogDto>>> GetHistory(int userId, [FromQuery] DateTime? date)
    {
        var query = _context.UserLocationLogs.Where(x => x.UserId == userId);

        if (date.HasValue)
            query = query.Where(x => x.Timestamp.Date == date.Value.Date);

        var logs = await query
            .OrderByDescending(x => x.Timestamp)
            .Take(100)
            .Select(x => new LocationLogDto
            {
                // Sửa lỗi ép kiểu: Nếu UserId trong DB là null thì trả về 0
                UserId = x.UserId ?? 0,
                DeviceId = x.DeviceId,
                Latitude = x.Latitude,
                Longitude = x.Longitude,
                Timestamp = x.Timestamp
            })
            .ToListAsync();

        return Ok(logs);
    }

    // 3. API Xóa lịch sử cũ (Dọn dẹp Database)
    // DELETE: api/tracking/cleanup
    [HttpDelete("cleanup")]
    public async Task<IActionResult> CleanupOldLogs()
    {
        // Xóa log cũ hơn 30 ngày
        var limitDate = DateTime.Now.AddDays(-30);

        var oldLogs = _context.UserLocationLogs.Where(x => x.Timestamp < limitDate);
        _context.UserLocationLogs.RemoveRange(oldLogs);

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã dọn dẹp lịch sử cũ." });
    }
}