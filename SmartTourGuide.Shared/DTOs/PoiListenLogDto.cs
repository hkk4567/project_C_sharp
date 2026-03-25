namespace SmartTourGuide.Shared.DTOs;

public class PoiListenLogDto
{
    public int PoiId { get; set; }
        public string DeviceId { get; set; } = string.Empty; 

    public int ListenDurationSec { get; set; }
    public DateTime Timestamp { get; set; }
}

public class TopPoiDto
{
    public int PoiId { get; set; }
    public string PoiName { get; set; } = "";
    public int ListenCount { get; set; }
}

public class AvgListenTimeDto
{
    public int PoiId { get; set; }
    public string PoiName { get; set; } = "";
    public double AvgDurationSec { get; set; }
}