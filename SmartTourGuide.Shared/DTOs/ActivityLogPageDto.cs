namespace SmartTourGuide.Shared.DTOs;

public class ActivityLogPageDto
{
    public List<ActivityLogDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TodayLogins { get; set; }
    public int TotalChanges { get; set; }
}