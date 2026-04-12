using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.DTOs;
using SmartTourGuide.Shared.Enums;
using BC = BCrypt.Net.BCrypt;

namespace SmartTourGuide.API.Controllers;
// endpoint là /api/auth/register và /api/auth/login để xử 
//lý đăng ký và đăng nhập tài khoản.

// File này xử lý xác thực tài khoản.
// - Đăng ký và đăng nhập người dùng
// - Ghi activity log cho các thao tác auth
// - Có endpoint debug để test việc lưu log
[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    // DbContext dùng để thao tác với Users và ActivityLogs.
    private readonly AppDbContext _context;

    public AuthController(AppDbContext context)
    {
        _context = context;
    }

    // Hàm helper dùng chung để ghi activity log cho các thao tác đăng ký/đăng nhập.
    private void AddActivityLog(string activityType, string description, string username)
    {
        // Lấy IP, ưu tiên header proxy nếu có.
        var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (string.IsNullOrEmpty(ip))
        {
            ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        }
        else
        {
            // Nếu qua proxy, chỉ lấy IP đầu tiên trong danh sách.
            ip = ip.Split(',')[0].Trim();
        }

        ip ??= "Unknown";

        // Cắt ngắn IP nếu cần để tránh lỗi độ dài khi lưu vào database.
        if (ip.Length > 50) ip = ip.Substring(0, 50);

        // Tạo record log và để SaveChanges ở hàm gọi xử lý.
        _context.ActivityLogs.Add(new ActivityLog
        {
            ActivityType = activityType,
            Description = description,
            UserName = username,
            Timestamp = DateTime.Now,
            IpAddress = ip
        });
    }

    // 1. Đăng ký tài khoản
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        // Không cho tạo trùng username.
        if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
            return BadRequest("Tên đăng nhập đã tồn tại.");

        // Tạo user mới và hash mật khẩu trước khi lưu.
        var user = new User
        {
            Username = dto.Username,
            FullName = dto.FullName,
            Email = dto.Email,
            Role = (UserRole)dto.Role,
            PasswordHash = BC.HashPassword(dto.Password)
        };

        _context.Users.Add(user);
        // audit là ghi log hoạt động, không phải ghi log lỗi. Nếu có lỗi sẽ trả về lỗi trước khi ghi log.
        // Ghi log đăng ký để phục vụ audit.
        AddActivityLog("Register", $"Tạo tài khoản mới: {user.Username}", user.Username);

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đăng ký thành công!" });
    }

    // 2. Đăng nhập
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        // Tìm user theo username để kiểm tra mật khẩu và trạng thái tài khoản.
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == dto.Username);
        Console.WriteLine("Đã vào login");

        if (user == null)
        {
            Console.WriteLine("Không tìm thấy user");
            return Unauthorized("Sai tài khoản hoặc mật khẩu");
        }

        // Chặn đăng nhập nếu tài khoản đang bị khóa.
        if (user.IsLocked)
        {
            Console.WriteLine("Tài khoản đã bị khóa");

            // Ghi log cho trường hợp đăng nhập bằng tài khoản đã bị khóa.
            AddActivityLog("LoginBlocked", $"Từ chối đăng nhập do tài khoản bị khóa: {user.Username}", user.Username);
            await _context.SaveChangesAsync();

            return Unauthorized("Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        if (!BC.Verify(dto.Password, user.PasswordHash))
        {
            Console.WriteLine("Sai mật khẩu");
            return Unauthorized("Sai tài khoản hoặc mật khẩu");
        }

        Console.WriteLine("Login thành công");

        // Ghi log đăng nhập thành công.
        AddActivityLog("Login", $"User đăng nhập: {user.Username}", user.Username);

        // Lưu cả user lẫn log trong cùng một lần commit.
        var result = await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Đăng nhập thành công!",
            saved = result, // thêm dòng này để debug
            user = new { user.Id, user.Username, user.FullName, user.Role }
        });
    }

    // Endpoint kiểm tra nhanh việc ghi ActivityLog trong môi trường dev/debug.
    [HttpGet("debug-log")]
    public async Task<IActionResult> DebugLog()
    {
        // Tạo một log mẫu để xác nhận pipeline lưu log hoạt động.
        _context.ActivityLogs.Add(new ActivityLog
        {
            ActivityType = "DEBUG",
            Description = "Test log",
            UserName = "test",
            Timestamp = DateTime.Now,
            IpAddress = "127.0.0.1"
        });

        var saved = await _context.SaveChangesAsync();

        return Ok(saved);
    }
}