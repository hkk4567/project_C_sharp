namespace SmartTourGuide.Shared.DTOs;

public class AdminDashboardSummaryDto
{
    public int TotalPois { get; set; }
    public int PendingPois { get; set; }
    public int TotalUsers { get; set; }
    public int LockedUsers { get; set; }
    public int TotalTours { get; set; }
    public int TotalListenEventsWeek { get; set; }
    public List<AdminDailyCountDto> WeeklyListenSeries { get; set; } = new();
    public List<ActivityLogDto> RecentActivities { get; set; } = new();
}

public class AdminDailyCountDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}