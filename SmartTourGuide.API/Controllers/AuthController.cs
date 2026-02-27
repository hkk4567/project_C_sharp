using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.DTOs;
using SmartTourGuide.Shared.Enums;
using BC = BCrypt.Net.BCrypt; // Alias cho ngắn gọn

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

    // 1. Đăng ký tài khoản
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        // Kiểm tra xem Username đã tồn tại chưa
        if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
            return BadRequest("Tên đăng nhập đã tồn tại.");

        var user = new User
        {
            Username = dto.Username,
            FullName = dto.FullName,
            Email = dto.Email,
            Role = (UserRole)dto.Role,
            // Mã hóa mật khẩu trước khi lưu
            PasswordHash = BC.HashPassword(dto.Password)
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đăng ký thành công!" });
    }

    // 2. Đăng nhập
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == dto.Username);

        // Kiểm tra user tồn tại và khớp mật khẩu (Verify)
        if (user == null || !BC.Verify(dto.Password, user.PasswordHash))
        {
            return Unauthorized("Tên đăng nhập hoặc mật khẩu không đúng.");
        }

        // Tạm thời trả về thông tin user (Sau này sẽ thay bằng JWT Token)
        return Ok(new
        {
            message = "Đăng nhập thành công!",
            user = new { user.Id, user.Username, user.FullName, user.Role }
        });
    }
}