using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.Shared.Enums;

namespace SmartTourGuide.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationsController(AppDbContext context)
    {
        _context = context;
    }

    private string GetCurrentUsername()
    {
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        var headerName = HttpContext.Request.Headers["X-User-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerName)) return headerName;

        return "Unknown";
    }

    [HttpGet("owner")]
    public async Task<IActionResult> GetOwnerNotifications()
    {
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        var owner = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.BoothOwner);

        if (owner == null) return NotFound("Không tìm thấy chủ gian hàng.");

        var unreadCount = await _context.OwnerNotifications
            .CountAsync(n => n.OwnerId == owner.Id && !n.IsRead);

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

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        var owner = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.BoothOwner);

        if (owner == null) return NotFound("Không tìm thấy chủ gian hàng.");

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

    [HttpGet("admin")]
    public async Task<IActionResult> GetAdminNotifications()
    {
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        var admin = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.Admin);

        if (admin == null) return NotFound("Không tìm thấy quản trị viên.");

        var unreadCount = await _context.AdminNotifications
            .CountAsync(n => n.AdminId == admin.Id && !n.IsRead);

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

    [HttpPut("admin/{id}/read")]
    public async Task<IActionResult> MarkAdminNotificationAsRead(int id)
    {
        var username = GetCurrentUsername();
        if (username == "Unknown") return Unauthorized("Chưa xác định được người dùng.");

        var admin = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.Admin);

        if (admin == null) return NotFound("Không tìm thấy quản trị viên.");

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

    // Debug endpoint để xem admin hiện tại là ai
    [HttpGet("debug/admin-info")]
    public async Task<IActionResult> GetCurrentAdminInfo()
    {
        var username = GetCurrentUsername();
        
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user == null)
            return Ok(new
            {
                username,
                message = "Không tìm thấy user nào",
                allUsers = await _context.Users.Select(u => new { u.Id, u.Username, u.Role }).ToListAsync()
            });

        var isAdmin = user.Role == UserRole.Admin;
        var notificationCount = await _context.AdminNotifications
            .CountAsync(n => n.AdminId == user.Id);

        var allAdmins = await _context.Users
            .Where(u => u.Role == UserRole.Admin)
            .Select(u => new { u.Id, u.Username })
            .ToListAsync();

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
