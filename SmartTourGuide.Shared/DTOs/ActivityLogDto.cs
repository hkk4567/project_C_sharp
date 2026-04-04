namespace SmartTourGuide.Shared.DTOs;

public class ActivityLogDto
{
    public int Id { get; set; }
    public string ActivityType { get; set; } = "";
    public string Description { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string IpAddress { get; set; } = "";
    public string? ChangeDetails { get; set; }
}