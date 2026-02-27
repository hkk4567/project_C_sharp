namespace SmartTourGuide.Shared.DTOs;

public class UserUpdateDto
{
    public required string FullName { get; set; }
    public required string Email { get; set; }
}

public class ChangePasswordDto
{
    public required string OldPassword { get; set; }
    public required string NewPassword { get; set; }
}