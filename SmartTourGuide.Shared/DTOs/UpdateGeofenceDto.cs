using System.ComponentModel.DataAnnotations;

namespace SmartTourGuide.Shared.DTOs;

public class UpdateGeofenceDto
{
    // Bán kính kích hoạt (mét)
    [Range(10, 500, ErrorMessage = "Bán kính từ 10m đến 500m")]
    public double TriggerRadiusInMeters { get; set; }

    // Thời gian chờ để kích hoạt lại (giây) - Tránh spam
    [Range(30, 3600)]
    public int CooldownInSeconds { get; set; }

    // Mức độ ưu tiên (nếu 2 vùng chồng lấn nhau, cái nào cao hơn thì phát)
    public int Priority { get; set; }
}