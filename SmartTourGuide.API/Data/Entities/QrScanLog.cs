using System.ComponentModel.DataAnnotations;
using SmartTourGuide.API.Services;

namespace SmartTourGuide.API.Data.Entities;

public class QrScanLog
{
    public int Id { get; set; }

    public int PoiId { get; set; }
    public DateTime ScannedAt { get; set; } = VietnamTime.Now;

    public string? DeviceId { get; set; }
    public string? UserAgent { get; set; }

    public Poi? Poi { get; set; }
}