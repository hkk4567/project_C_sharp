using SmartTourGuide.Shared.Enums;

namespace SmartTourGuide.Shared.DTOs;

public class UserDto
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public UserRole Role { get; set; }
    public bool IsLocked { get; set; }
}

