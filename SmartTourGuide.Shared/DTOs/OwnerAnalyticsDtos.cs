namespace SmartTourGuide.Shared.DTOs;

public class OwnerAnalyticsSummaryDto
{
    public int OwnerId { get; set; }
    public int TotalPois { get; set; }
    public int DistinctPoisHeard { get; set; }
    public int TotalListensWeek { get; set; }
    public int TotalListensMonth { get; set; }
    public double AvgDurationSecMonth { get; set; }
}

public class OwnerDailyListenDto
{
    public DateTime Date { get; set; }
    public int ListenCount { get; set; }
}

public class OwnerHeatmapPointDto
{
    public int PoiId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int HitCount { get; set; }
}
