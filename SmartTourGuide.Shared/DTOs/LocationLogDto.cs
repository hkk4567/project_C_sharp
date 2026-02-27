namespace SmartTourGuide.Shared.DTOs;

public class LocationLogDto
{
    public int UserId { get; set; } // Tạm thời gửi ID trần (sau này có Login sẽ lấy từ Token)
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }
}