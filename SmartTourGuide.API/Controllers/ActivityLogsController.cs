using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.Shared.DTOs; // Đảm bảo DTO này giống bên Frontend

namespace SmartTourGuide.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ActivityLogsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ActivityLogsController(AppDbContext context)
    {
        _context = context;
    }

    // GET: api/activitylogs
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ActivityLogDto>>> GetLogs(
        [FromQuery] string? type, 
        [FromQuery] string? user, 
        [FromQuery] DateTime? from, 
        [FromQuery] DateTime? to)
    {
        var query = _context.ActivityLogs.AsQueryable();

        // Lọc theo Loại (Login, Register, POI...)
       if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(l => l.ActivityType.Contains(type));
        }

        // Lọc theo Username (Tìm kiếm gần đúng)
        if (!string.IsNullOrWhiteSpace(user))
        {
            query = query.Where(l => 
                l.UserName.ToLower().Contains(user.ToLower()));
        }

        // Lọc theo Từ ngày
        if (from.HasValue)
        {
            query = query.Where(l => l.Timestamp >= from.Value.Date);
        }

        // Lọc theo Đến ngày (Cộng thêm 1 ngày để lấy trọn vẹn ngày đó)
        if (to.HasValue)
        {
            var toDate = to.Value.Date.AddDays(1);
            query = query.Where(l => l.Timestamp < toDate);
        }

        // Sắp xếp mới nhất lên đầu
        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            .Select(l => new ActivityLogDto
            {
                Id = l.Id,
                ActivityType = l.ActivityType,
                Description = l.Description,
                UserName = l.UserName,
                Timestamp = l.Timestamp,
                IpAddress = l.IpAddress
            })
            .ToListAsync();

        return Ok(logs);
    }
}