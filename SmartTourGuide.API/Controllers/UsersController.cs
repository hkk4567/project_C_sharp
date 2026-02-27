using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.DTOs;

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

    // 1. Lấy thông tin chi tiết User
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProfile(int id)
    {
        var user = await _context.Users
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.FullName,
                u.Email,
                u.Role
            })
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound("Người dùng không tồn tại.");
        return Ok(user);
    }

    // 2. Cập nhật thông tin cá nhân (FullName, Email)
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProfile(int id, [FromBody] UserUpdateDto dto)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.FullName = dto.FullName;
        user.Email = dto.Email;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Cập nhật thông tin thành công!" });
    }

    // 3. Đổi mật khẩu
    [HttpPut("{id}/change-password")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto dto)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Lưu ý: Trong thực tế bạn phải dùng thư viện BCrypt hoặc Identity để Verify mật khẩu
        // Ở đây mình ví dụ logic kiểm tra cơ bản
        if (user.PasswordHash != dto.OldPassword)
        {
            return BadRequest("Mật khẩu cũ không chính xác.");
        }

        user.PasswordHash = dto.NewPassword; // Nhớ Hash mật khẩu trước khi lưu!
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đổi mật khẩu thành công!" });
    }
}