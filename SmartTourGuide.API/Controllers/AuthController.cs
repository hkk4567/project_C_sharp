using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.DTOs;
using SmartTourGuide.Shared.Enums;
using BC = BCrypt.Net.BCrypt;

namespace SmartTourGuide.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;

    public AuthController(AppDbContext context)
    {
        _context = context;
    }

    // Hàm Helper dùng chung để lưu Log
    private void AddActivityLog(string activityType, string description, string username)
    {
        // Lấy IP, xử lý trường hợp X-Forwarded-For trả về chuỗi dài gồm nhiều IP
        var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(ip))
        {
            ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        }
        else
        {
            // Nếu qua proxy, X-Forwarded-For có thể có dạng "IP1, IP2", ta chỉ lấy IP đầu tiên
            ip = ip.Split(',')[0].Trim();
        }

        ip ??= "Unknown";

        // Cắt ngắn IP nếu quá dài để tránh lỗi Entity Framework khi lưu vào Database (ví dụ DB giới hạn 50 ký tự)
        if (ip.Length > 50) ip = ip.Substring(0, 50);

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
        if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
            return BadRequest("Tên đăng nhập đã tồn tại.");

        var user = new User
        {
            Username = dto.Username,
            FullName = dto.FullName,
            Email = dto.Email,
            Role = (UserRole)dto.Role,
            PasswordHash = BC.HashPassword(dto.Password)
        };

        _context.Users.Add(user);

        // THÊM LOG CHO ĐĂNG KÝ
        AddActivityLog("Register", $"Tạo tài khoản mới: {user.Username}", user.Username);

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đăng ký thành công!" });
    }

    // 2. Đăng nhập
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == dto.Username);
        Console.WriteLine("Đã vào login");

        if (user == null)
        {
            Console.WriteLine("Không tìm thấy user");
            return Unauthorized("Sai tài khoản hoặc mật khẩu");
        }

        if (user.IsLocked)
        {
            Console.WriteLine("Tài khoản đã bị khóa");

            // Ghi log cho trường hợp đăng nhập bằng tài khoản đã bị khóa
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

        // THÊM LOG CHO ĐĂNG NHẬP
        AddActivityLog("Login", $"User đăng nhập: {user.Username}", user.Username);

        var result = await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Đăng nhập thành công!",
            saved = result, // 👈 thêm dòng này để debug
            user = new { user.Id, user.Username, user.FullName, user.Role }
        });
    }
    [HttpGet("debug-log")]
    public async Task<IActionResult> DebugLog()
    {
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
    //
}