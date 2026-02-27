namespace SmartTourGuide.Shared.DTOs;

public class RegisterDto
{
    public required string Username { get; set; }
    public required string Password { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public int Role { get; set; } = 0; // Mặc định là Tourist
}

public class LoginDto
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}