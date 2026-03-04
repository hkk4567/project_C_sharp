using SmartTourGuide.Shared.Enums;

namespace SmartTourGuide.Shared.DTOs;

public class CreateUpdateUserDto
{
    public required string Username { get; set; } // Chỉ dùng khi tạo mới
    public required string Password { get; set; } // Chỉ dùng khi tạo mới
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public UserRole Role { get; set; }
}