namespace SmartTourGuide.Shared.DTOs;

public class OwnerQrScanStatsDto
{
    public int PoiId { get; set; }
    public string PoiName { get; set; } = string.Empty;
    public int TotalQrScans { get; set; }
}