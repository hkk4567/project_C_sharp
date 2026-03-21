using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.DTOs;
using BC = BCrypt.Net.BCrypt;

namespace SmartTourGuide.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context)
    {
        _context = context;
    }

    // 👉 HÀM HELPER LẤY USERNAME CỦA NGƯỜI ĐANG THỰC HIỆN THAO TÁC
    // Ưu tiên: JWT Claims -> Header X-User-Name -> "Unknown"
    private string GetCurrentUsername()
    {
        // 1. Thử lấy từ JWT (khi có xác thực)
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        // 2. Fallback: đọc header X-User-Name mà frontend gửi lên sau khi login
        var headerName = HttpContext.Request.Headers["X-User-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerName)) return headerName;

        return "Unknown";
    }

    // 👉 HÀM HELPER DÙNG CHUNG ĐỂ LƯU LOG (Đã xử lý IP an toàn)
    private void AddActivityLog(string activityType, string description, string username)
    {
        var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (string.IsNullOrEmpty(ip))
        {
            ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        }
        else
        {
            ip = ip.Split(',')[0].Trim(); // Lấy IP đầu tiên nếu qua proxy
        }

        ip ??= "Unknown";
        if (ip.Length > 50) ip = ip.Substring(0, 50); // Tránh lỗi vượt quá số ký tự của Cột DB

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
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.FullName = dto.FullName;
        user.Email = dto.Email;
        user.Role = dto.Role;

        // 👉 GHI LOG: dùng GetCurrentUsername() để lấy NGƯỜI THỰC HIỆN (admin), không phải target
        var actor = GetCurrentUsername();
        AddActivityLog("UpdateUser", $"[{actor}] cập nhật thông tin người dùng: {user.Username}", actor);

        await _context.SaveChangesAsync();
        return Ok(new { message = "Cập nhật thành công" });
    }

    // 3. Đổi mật khẩu
    [HttpPut("{id}/change-password")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto dto)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (!BC.Verify(dto.OldPassword, user.PasswordHash))
        {
            return BadRequest("Mật khẩu cũ không chính xác.");
        }

        user.PasswordHash = BC.HashPassword(dto.NewPassword);

        // 👉 GHI LOG: người dùng tự đổi mật khẩu của chính họ -> dùng user.Username là đúng
        // Nhưng nếu admin đổi giúp thì log actor
        var actor = GetCurrentUsername();
        // Nếu không xác định được actor hoặc actor chính là user đó -> ghi username của họ
        var logActor = string.IsNullOrEmpty(actor) || actor == "Unknown" ? user.Username : actor;
        AddActivityLog("ChangePassword", $"[{logActor}] thay đổi mật khẩu tài khoản: {user.Username}", logActor);

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đổi mật khẩu thành công!" });
    }

    // 4. Lấy danh sách User
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll()
    {
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
        if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
            return BadRequest("Tên đăng nhập đã tồn tại!");

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

        // 👉 GHI LOG TẠO USER: actor là admin đang thực hiện, không phải user mới tạo
        var actor = GetCurrentUsername();
        AddActivityLog("CreateUser", $"[{actor}] tạo user mới: {user.Username}", actor);

        // 👉 Chỉ gọi SaveChanges 1 LẦN DUY NHẤT ở cuối cùng
        await _context.SaveChangesAsync();

        return Ok(new { message = "Tạo user thành công" });
    }

    // 6. Khóa / Mở khóa User
    [HttpPut("{id}/lock")]
    public async Task<IActionResult> ToggleLock(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Đảo ngược trạng thái
        user.IsLocked = !user.IsLocked;

        // 👉 GHI LOG KHÓA / MỞ KHÓA: actor là admin, target là user bị khóa/mở
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