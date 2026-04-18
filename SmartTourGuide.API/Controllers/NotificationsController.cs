using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.Shared.Enums;

namespace SmartTourGuide.API.Controllers;

// File này quản lý thông báo của hệ thống.
// - Lấy danh sách thông báo cho chủ gian hàng và admin
// - Đánh dấu thông báo đã đọc
// - Có endpoint debug để kiểm tra user/admin hiện tại

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    // DbContext để truy vấn user và các bảng thông báo.
    private readonly AppDbContext _context;

    public NotificationsController(AppDbContext context)
    {
        _context = context;
    }

    // Lấy username hiện tại từ Identity hoặc header dự phòng.
    private string GetCurrentUsername()
    {
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        var headerName = HttpContext.Request.Headers["X-User-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerName)) return headerName;

        return "Unknown";
    }

    // Lấy danh sách thông báo dành cho chủ gian hàng hiện tại.
    [HttpGet("owner")]
    public async Task<IActionResult> GetOwnerNotifications()
    {
        // Phải xác định được người dùng trước khi truy vấn dữ liệu riêng.
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        // Chỉ cho phép user có role BoothOwner truy cập nhóm thông báo này.
        var owner = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.BoothOwner);

        if (owner == null) return NotFound("Không tìm thấy chủ gian hàng.");

        // Đếm số thông báo chưa đọc để hiển thị badge trên UI.
        var unreadCount = await _context.OwnerNotifications
            .CountAsync(n => n.OwnerId == owner.Id && !n.IsRead);

        // Chỉ lấy 20 thông báo mới nhất để trả về cho frontend.
        var items = await _context.OwnerNotifications
            .Where(n => n.OwnerId == owner.Id)
            .OrderByDescending(n => n.CreatedAt)
            .Take(20)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Message,
                n.AdminUsername,
                n.PoiId,
                n.CreatedAt,
                n.IsRead
            })
            .ToListAsync();

        return Ok(new
        {
            unreadCount,
            items
        });
    }

    // Đánh dấu một thông báo của owner là đã đọc.
    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        // Kiểm tra người dùng và vai trò trước khi cập nhật.
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        var owner = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.BoothOwner);

        if (owner == null) return NotFound("Không tìm thấy chủ gian hàng.");

        // Chỉ cập nhật thông báo thuộc đúng owner hiện tại.
        var notification = await _context.OwnerNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.OwnerId == owner.Id);

        if (notification == null) return NotFound("Thông báo không tồn tại.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Đã đánh dấu đã đọc." });
    }

    [HttpPut("owner/read-all")]
    public async Task<IActionResult> MarkAllOwnerNotificationsAsRead()
    {
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        var owner = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.BoothOwner);

        if (owner == null) return NotFound("Không tìm thấy chủ gian hàng.");

        var notifications = await _context.OwnerNotifications
            .Where(n => n.OwnerId == owner.Id && !n.IsRead)
            .ToListAsync();

        if (notifications.Count > 0)
        {
            foreach (var item in notifications)
            {
                item.IsRead = true;
            }

            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Đã đánh dấu tất cả thông báo là đã đọc." });
    }

    [HttpDelete("owner/all")]
    public async Task<IActionResult> DeleteAllOwnerNotifications()
    {
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        var owner = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.BoothOwner);

        if (owner == null) return NotFound("Không tìm thấy chủ gian hàng.");

        var notifications = await _context.OwnerNotifications
            .Where(n => n.OwnerId == owner.Id)
            .ToListAsync();

        if (notifications.Count > 0)
        {
            _context.OwnerNotifications.RemoveRange(notifications);
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Đã xóa tất cả thông báo của owner." });
    }

    // Lấy danh sách thông báo dành cho admin.
    [HttpGet("admin")]
    public async Task<IActionResult> GetAdminNotifications()
    {
        // Chỉ truy cập khi xác định được user hiện tại.
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        // Chỉ cho phép tài khoản role Admin.
        var admin = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.Admin);

        if (admin == null) return NotFound("Không tìm thấy quản trị viên.");

        // Đếm số thông báo chưa đọc để hiển thị trạng thái mới.
        var unreadCount = await _context.AdminNotifications
            .CountAsync(n => n.AdminId == admin.Id && !n.IsRead);

        // Lấy tối đa 20 thông báo mới nhất.
        var items = await _context.AdminNotifications
            .Where(n => n.AdminId == admin.Id)
            .OrderByDescending(n => n.CreatedAt)
            .Take(20)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Message,
                n.OwnerUsername,
                n.PoiId,
                n.CreatedAt,
                n.IsRead
            })
            .ToListAsync();

        return Ok(new
        {
            unreadCount,
            items
        });
    }

    // Đánh dấu thông báo của admin là đã đọc.
    [HttpPut("admin/{id}/read")]
    public async Task<IActionResult> MarkAdminNotificationAsRead(int id)
    {
        // Kiểm tra tài khoản admin hiện tại trước khi cập nhật.
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        var admin = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.Admin);

        if (admin == null) return NotFound("Không tìm thấy quản trị viên.");

        // Đảm bảo chỉ sửa đúng thông báo thuộc admin đang đăng nhập.
        var notification = await _context.AdminNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.AdminId == admin.Id);

        if (notification == null) return NotFound("Thông báo không tồn tại.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Đã đánh dấu đã đọc." });
    }

    [HttpPut("admin/read-all")]
    public async Task<IActionResult> MarkAllAdminNotificationsAsRead()
    {
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        var admin = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.Admin);

        if (admin == null) return NotFound("Không tìm thấy quản trị viên.");

        var notifications = await _context.AdminNotifications
            .Where(n => n.AdminId == admin.Id && !n.IsRead)
            .ToListAsync();

        if (notifications.Count > 0)
        {
            foreach (var item in notifications)
            {
                item.IsRead = true;
            }

            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Đã đánh dấu tất cả thông báo là đã đọc." });
    }

    [HttpDelete("admin/all")]
    public async Task<IActionResult> DeleteAllAdminNotifications()
    {
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        var admin = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.Admin);

        if (admin == null) return NotFound("Không tìm thấy quản trị viên.");

        var notifications = await _context.AdminNotifications
            .Where(n => n.AdminId == admin.Id)
            .ToListAsync();

        if (notifications.Count > 0)
        {
            _context.AdminNotifications.RemoveRange(notifications);
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Đã xóa tất cả thông báo của admin." });
    }

    // Debug endpoint để xem admin hiện tại là ai
    [HttpGet("debug/admin-info")]
    public async Task<IActionResult> GetCurrentAdminInfo()
    {
        // Dùng cho debug: xem user hiện tại và trạng thái role của họ.
        var username = GetCurrentUsername();

        // Tìm user theo username hiện tại để kiểm tra mapping thông báo.
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user == null)
            return Ok(new
            {
                username,
                message = "Không tìm thấy user nào",
                // Trả toàn bộ user để debug dữ liệu đầu vào khi cần đối chiếu.
                allUsers = await _context.Users.Select(u => new { u.Id, u.Username, u.Role }).ToListAsync()
            });

        // Xác định xem user hiện tại có phải admin hay không.
        var isAdmin = user.Role == UserRole.Admin;
        var notificationCount = await _context.AdminNotifications
            .CountAsync(n => n.AdminId == user.Id);

        // Lấy tất cả admin hiện có để kiểm tra số thông báo của từng tài khoản.
        var allAdmins = await _context.Users
            .Where(u => u.Role == UserRole.Admin)
            .Select(u => new { u.Id, u.Username })
            .ToListAsync();

        // Gom số lượng thông báo của từng admin thành danh sách debug.
        var adminNotificationsList = new List<object>();
        foreach (var admin in allAdmins)
        {
            var count = await _context.AdminNotifications
                .CountAsync(n => n.AdminId == admin.Id);
            adminNotificationsList.Add(new { admin.Id, admin.Username, NotificationCount = count });
        }

        return Ok(new
        {
            username,
            userId = user.Id,
            role = user.Role.ToString(),
            isAdmin,
            adminNotificationCount = notificationCount,
            allAdminUsers = adminNotificationsList
        });
    }
}
