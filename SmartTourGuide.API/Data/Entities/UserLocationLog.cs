using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTourGuide.API.Data.Entities;

public class UserLocationLog
{
    [Key]
    public long Id { get; set; }

    // UserId có dấu ? để cho phép Null (không đăng nhập)
    public int? UserId { get; set; }

    public string? DeviceId { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public DateTime Timestamp { get; set; }

    public virtual User? User { get; set; }
}