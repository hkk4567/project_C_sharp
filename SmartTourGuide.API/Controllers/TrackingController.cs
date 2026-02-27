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
        // Kiểm tra User có tồn tại không (nếu cần thiết)
        // var userExists = await _context.Users.AnyAsync(u => u.Id == dto.UserId);
        // if (!userExists) return BadRequest("User không tồn tại");

        var log = new UserLocationLog
        {
            UserId = dto.UserId,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            Timestamp = dto.Timestamp // Nên lấy giờ từ Client để chính xác nhịp điệu
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
        var query = _context.UserLocationLogs
            .Where(x => x.UserId == userId);

        // Nếu có lọc theo ngày
        if (date.HasValue)
        {
            query = query.Where(x => x.Timestamp.Date == date.Value.Date);
        }

        // Lấy dữ liệu, sắp xếp mới nhất lên đầu
        var logs = await query
            .OrderByDescending(x => x.Timestamp)
            .Take(100) // Chỉ lấy 100 điểm gần nhất định để tránh lag bản đồ
            .Select(x => new LocationLogDto
            {
                UserId = x.UserId,
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