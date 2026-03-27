namespace SmartTourGuide.Shared.DTOs;

public class CreatePoiDto
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }

    // Đổi từ string sang int ở đây
    public int OwnerId { get; set; }
}