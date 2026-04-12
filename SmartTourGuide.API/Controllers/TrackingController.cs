using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

// File này quản lý dữ liệu tracking vị trí người dùng.
// - Ghi nhận vị trí hiện tại từ mobile
// - Xem lịch sử di chuyển theo user hoặc theo ngày
// - Xóa các log cũ để dọn dẹp database

[Route("api/[controller]")]
[ApiController]
public class TrackingController : ControllerBase
{
    // DbContext dùng để lưu và truy vấn lịch sử vị trí người dùng.
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
        // Không bắt buộc UserId hợp lệ vì hệ thống cho phép khách vãng lai gửi vị trí.

        // Tạo record log vị trí mới để lưu vào database.
        var log = new UserLocationLog
        {
            // Nếu UserId không hợp lệ thì lưu null để biểu diễn khách vãng lai.
            UserId = dto.UserId > 0 ? dto.UserId : null,
            // DeviceId là mã định danh thiết bị do frontend gửi lên.
            DeviceId = dto.DeviceId,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            // Nếu client không truyền timestamp thì dùng thời gian hiện tại.
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
        // Bắt đầu query theo đúng user cần xem lịch sử.
        var query = _context.UserLocationLogs.Where(x => x.UserId == userId);

        // Nếu có truyền ngày thì chỉ lấy log trong ngày đó.
        if (date.HasValue)
            query = query.Where(x => x.Timestamp.Date == date.Value.Date);

        // Sắp xếp mới nhất lên đầu và giới hạn 100 bản ghi gần nhất.
        var logs = await query
            .OrderByDescending(x => x.Timestamp)
            .Take(100)
            .Select(x => new LocationLogDto
            {
                // Nếu UserId trong DB là null thì trả về 0 để DTO luôn có giá trị số.
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
        // Xóa các log cũ hơn 30 ngày để giảm dung lượng database.
        var limitDate = DateTime.Now.AddDays(-30);

        // Lấy toàn bộ log cũ rồi xóa hàng loạt.
        var oldLogs = _context.UserLocationLogs.Where(x => x.Timestamp < limitDate);
        _context.UserLocationLogs.RemoveRange(oldLogs);

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã dọn dẹp lịch sử cũ." });
    }
}