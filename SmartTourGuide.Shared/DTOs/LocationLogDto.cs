namespace SmartTourGuide.Shared.DTOs;

public class LocationLogDto
{
    public int UserId { get; set; } // Gửi 0 nếu không đăng nhập
    public string? DeviceId { get; set; } // ID ảo từ thiết bị
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }
}