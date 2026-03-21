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

    // 2. Cập nhật User
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateUpdateUserDto dto)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.FullName = dto.FullName;
        user.Email = dto.Email;
        user.Role = dto.Role;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Cập nhật thành công" });
    }

    // 3. Đổi mật khẩu
    [HttpPut("{id}/change-password")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto dto)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Lưu ý: Trong thực tế bạn phải dùng thư viện BCrypt hoặc Identity để Verify mật khẩu
        // Ở đây mình ví dụ logic kiểm tra cơ bản
        if (!BC.Verify(dto.OldPassword, user.PasswordHash))
        {
            return BadRequest("Mật khẩu cũ không chính xác.");
        }

        user.PasswordHash = BC.HashPassword(dto.NewPassword); // Nhớ Hash mật khẩu trước khi lưu!
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
    // 5. Tạo User mới
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
            PasswordHash = BC.HashPassword(dto.Password), // Mã hóa pass
            IsLocked = false
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Tạo user thành công" });
    }

    // 6. Khóa / Mở khóa User
    [HttpPut("{id}/lock")]
    public async Task<IActionResult> ToggleLock(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Đảo ngược trạng thái (Đang khóa -> Mở, Đang mở -> Khóa)
        user.IsLocked = !user.IsLocked;

        await _context.SaveChangesAsync();
        return Ok(new { message = user.IsLocked ? "Đã khóa tài khoản" : "Đã mở khóa tài khoản" });
    }

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