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
public class ActivityLogsController(AppDbContext context) : ControllerBase
{
    private const int MaxPageSize = 100;

    // API lấy danh sách nhật ký hoạt động, hỗ trợ lọc theo loại, người dùng và khoảng thời gian.
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? type,
        [FromQuery] string? user,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        // Bắt đầu từ tập dữ liệu gốc để ghép điều kiện lọc động theo query string.
        var query = context.ActivityLogs.AsNoTracking().AsQueryable();

        // Lọc theo Loại (Login, Register, POI...)
        if (!string.IsNullOrWhiteSpace(type))
        {
            // Lọc theo loại hoạt động (ví dụ: Login, Register, POI...).
            query = query.Where(l => l.ActivityType.Contains(type));
        }

        // Lọc theo Username (Tìm kiếm gần đúng)
        if (!string.IsNullOrWhiteSpace(user))
        {
            // Dùng LIKE để tránh ToLower() trong truy vấn SQL và giữ hiệu năng.
            query = query.Where(l => EF.Functions.Like(l.UserName, $"%{user}%"));
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

        // Các thống kê được tính trên toàn bộ tập dữ liệu đã lọc.
        var totalCount = await query.CountAsync();
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var todayLogins = await query.CountAsync(l =>
            l.ActivityType == "Login" &&
            l.Timestamp >= today &&
            l.Timestamp < tomorrow);

        var totalChanges = await query.CountAsync(l => l.ActivityType != "Login");

        // Sắp xếp mới nhất lên đầu.
        var orderedQuery = query.OrderByDescending(l => l.Timestamp);

        // Nếu client truyền page/pageSize thì trả về kết quả phân trang.
        if (page.HasValue || pageSize.HasValue)
        {
            var currentPage = Math.Max(page.GetValueOrDefault(1), 1);
            var currentPageSize = Math.Clamp(pageSize.GetValueOrDefault(10), 1, MaxPageSize);

            var logs = await orderedQuery
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize)
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

            return Ok(new ActivityLogPageDto
            {
                Items = logs,
                TotalCount = totalCount,
                CurrentPage = currentPage,
                PageSize = currentPageSize,
                TodayLogins = todayLogins,
                TotalChanges = totalChanges
            });
        }

        var allLogs = await orderedQuery
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
        return Ok(allLogs);
    }
}