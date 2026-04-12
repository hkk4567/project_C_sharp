using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.DTOs;
using BC = BCrypt.Net.BCrypt;

namespace SmartTourGuide.API.Controllers;

// File này quản lý người dùng trong hệ thống.
// - Xem, tạo, cập nhật, khóa/mở khóa tài khoản
// - Đổi mật khẩu cho user
// - Ghi activity log cho các thao tác quản trị

[Route("api/[controller]")]
[ApiController]
public class UsersController : ControllerBase
{
    // DbContext dùng để thao tác với Users và ActivityLogs.
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context)
    {
        _context = context;
    }

    // 👉 HÀM HELPER LẤY USERNAME CỦA NGƯỜI ĐANG THỰC HIỆN THAO TÁC
    // Ưu tiên: JWT Claims -> Header X-User-Name -> "Unknown"
    private string GetCurrentUsername()
    {
        // Ưu tiên lấy từ JWT/Identity nếu request đã xác thực.
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        // Fallback: đọc từ header X-User-Name khi client chưa dùng JWT đầy đủ.
        var headerName = HttpContext.Request.Headers["X-User-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerName)) return headerName;

        return "Unknown";
    }

    // 👉 HÀM HELPER DÙNG CHUNG ĐỂ LƯU LOG (Đã xử lý IP an toàn)
    private void AddActivityLog(string activityType, string description, string username)
    {
        // Ưu tiên IP thật từ proxy nếu có.
        var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (string.IsNullOrEmpty(ip))
        {
            // Nếu không có proxy thì lấy IP trực tiếp của request.
            ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        }
        else
        {
            // Nếu qua proxy thì chỉ lấy IP đầu tiên trong chuỗi.
            ip = ip.Split(',')[0].Trim();
        }

        ip ??= "Unknown";
        // Cắt ngắn để tránh lỗi vượt quá giới hạn độ dài của cột trong DB.
        if (ip.Length > 50) ip = ip.Substring(0, 50);

        // Ghi một activity log mới, SaveChanges sẽ gọi ở hàm nghiệp vụ.
        _context.ActivityLogs.Add(new ActivityLog
        {
            ActivityType = activityType,
            Description = description,
            UserName = username,
            Timestamp = DateTime.Now,
            IpAddress = ip
        });
    }

    // 1. Lấy thông tin chi tiết User
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProfile(int id)
    {
        // Chỉ lấy các trường cần thiết để trả về cho client.
        var user = await _context.Users
            .Select(u => new { u.Id, u.Username, u.FullName, u.Email, u.Role })
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound("Người dùng không tồn tại.");
        return Ok(user);
    }

    // 2. Cập nhật User
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateUpdateUserDto dto)
    {
        // Tìm user cần cập nhật.
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Cập nhật các trường cơ bản.
        user.FullName = dto.FullName;
        user.Email = dto.Email;
        user.Role = dto.Role;

        // Ghi log theo người thực hiện thao tác, không phải user bị sửa.
        var actor = GetCurrentUsername();
        AddActivityLog("UpdateUser", $"[{actor}] cập nhật thông tin người dùng: {user.Username}", actor);

        await _context.SaveChangesAsync();
        return Ok(new { message = "Cập nhật thành công" });
    }

    // 3. Đổi mật khẩu
    [HttpPut("{id}/change-password")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto dto)
    {
        // Tìm tài khoản cần đổi mật khẩu.
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Xác thực mật khẩu cũ trước khi cho phép đổi.
        if (!BC.Verify(dto.OldPassword, user.PasswordHash))
        {
            return BadRequest("Mật khẩu cũ không chính xác.");
        }

        // Hash lại mật khẩu mới trước khi lưu vào DB.
        user.PasswordHash = BC.HashPassword(dto.NewPassword);

        // Xác định ai là người thực hiện thao tác để ghi log phù hợp.
        var actor = GetCurrentUsername();
        var logActor = string.IsNullOrEmpty(actor) || actor == "Unknown" ? user.Username : actor;
        AddActivityLog("ChangePassword", $"[{logActor}] thay đổi mật khẩu tài khoản: {user.Username}", logActor);

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đổi mật khẩu thành công!" });
    }

    // 4. Lấy danh sách User
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll()
    {
        // Lấy toàn bộ user và map sang DTO để tránh trả dư dữ liệu nhạy cảm.
        var users = await _context.Users.ToListAsync();
        return users.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            FullName = u.FullName,
            Email = u.Email,
            Role = u.Role,
            IsLocked = u.IsLocked
        }).ToList();
    }

    // 5. Tạo User mới (Dành cho Admin)
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUpdateUserDto dto)
    {
        // Không cho tạo trùng username.
        if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
            return BadRequest("Tên đăng nhập đã tồn tại!");

        // Tạo tài khoản mới và hash mật khẩu ngay khi lưu.
        var user = new User
        {
            Username = dto.Username,
            FullName = dto.FullName,
            Email = dto.Email,
            Role = dto.Role,
            PasswordHash = BC.HashPassword(dto.Password),
            IsLocked = false
        };

        _context.Users.Add(user);

        // Ghi log theo admin đang thực hiện thao tác tạo user.
        var actor = GetCurrentUsername();
        AddActivityLog("CreateUser", $"[{actor}] tạo user mới: {user.Username}", actor);

        // Gọi SaveChanges một lần để lưu cả user và log cùng lúc.
        await _context.SaveChangesAsync();

        return Ok(new { message = "Tạo user thành công" });
    }

    // 6. Khóa / Mở khóa User
    [HttpPut("{id}/lock")]
    public async Task<IActionResult> ToggleLock(int id)
    {
        // Tìm user cần khóa/mở khóa.
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Đảo trạng thái IsLocked hiện tại.
        user.IsLocked = !user.IsLocked;

        // Ghi log thao tác khóa/mở khóa tài khoản.
        string actionText = user.IsLocked ? "Khóa" : "Mở khóa";
        var actor = GetCurrentUsername();
        AddActivityLog("ToggleLock", $"[{actor}] {actionText.ToLower()} tài khoản: {user.Username}", actor);

        await _context.SaveChangesAsync();
        return Ok(new { message = user.IsLocked ? "Đã khóa tài khoản" : "Đã mở khóa tài khoản" });
    }

    // 7. Lấy user theo Username
    [HttpGet("by-username/{username}")]
    public async Task<IActionResult> GetByUsername(string username)
    {
        // Tìm user theo username để lấy thông tin hiển thị trên UI.
        var user = await _context.Users
            .Where(u => u.Username == username)
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound();

        return Ok(new
        {
            fullName = user.FullName ?? string.Empty,
            email = user.Email ?? string.Empty
        });
    }
}