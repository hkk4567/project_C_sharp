using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.Shared.DTOs; // Đảm bảo DTO này giống bên Frontend

namespace SmartTourGuide.API.Controllers;
// DTO là Data Transfer Object, dùng để định nghĩa cấu trúc dữ
// liệu trả về cho frontend mà không cần phải expose toàn bộ entity 
//của database. Điều này giúp bảo mật và tối ưu hóa dữ liệu truyền 
//qua API. ActivityLogDto sẽ chỉ chứa các trường cần thiết để hiển
// thị log trên giao diện quản trị, ví dụ như Id, ActivityType, 
//Description, UserName, Timestamp và IpAddress.

// File này dùng để lấy và lọc nhật ký hoạt động của hệ thống.
// - Hỗ trợ lọc theo loại, người dùng và khoảng thời gian
// - Trả về danh sách ActivityLog dưới dạng DTO cho frontend
[Route("api/[controller]")]
[ApiController]
public class ActivityLogsController : ControllerBase
{
    // DbContext dùng để truy vấn bảng ActivityLogs.
    private readonly AppDbContext _context;

    public ActivityLogsController(AppDbContext context)
    {
        _context = context;
    }

    // API lấy danh sách nhật ký hoạt động, hỗ trợ lọc theo loại, người dùng và khoảng thời gian.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ActivityLogDto>>> GetLogs(
        [FromQuery] string? type,
        [FromQuery] string? user,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        // Bắt đầu từ tập dữ liệu gốc để ghép điều kiện lọc động theo query string.
        var query = _context.ActivityLogs.AsQueryable();

        // Lọc theo Loại (Login, Register, POI...)
        if (!string.IsNullOrWhiteSpace(type))
        {
            // Lọc theo loại hoạt động (ví dụ: Login, Register, POI...).
            query = query.Where(l => l.ActivityType.Contains(type));
        }

        // Lọc theo Username (Tìm kiếm gần đúng)
        if (!string.IsNullOrWhiteSpace(user))
        {
            // Lọc theo tên người dùng (không phân biệt hoa thường, tìm gần đúng).
            query = query.Where(l =>
                l.UserName.ToLower().Contains(user.ToLower()));
        }

        // Lọc theo Từ ngày
        if (from.HasValue)
        {
            // Lấy từ đầu ngày 'from' để không bỏ sót bản ghi trong ngày.
            query = query.Where(l => l.Timestamp >= from.Value.Date);
        }

        // Lọc theo Đến ngày (Cộng thêm 1 ngày để lấy trọn vẹn ngày đó)
        if (to.HasValue)
        {
            // Dùng mốc < ngày kế tiếp để lấy trọn vẹn ngày 'to'.
            var toDate = to.Value.Date.AddDays(1);
            query = query.Where(l => l.Timestamp < toDate);
        }

        // Sắp xếp mới nhất lên đầu
        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            // Chỉ map các trường cần thiết sang DTO để trả về cho client.
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

        // Trả về danh sách log đã lọc và sắp xếp.
        return Ok(logs);
    }
}