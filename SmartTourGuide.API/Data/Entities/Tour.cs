using System.ComponentModel.DataAnnotations;

namespace SmartTourGuide.API.Data.Entities;

public class Tour
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public required string Name { get; set; } // Tên Tour (VD: Hà Nội City Tour)

    public required string Description { get; set; }

    // Ảnh đại diện cho Tour (lấy từ ảnh của POI đầu tiên hoặc upload riêng)
    public string? ThumbnailUrl { get; set; }

    // Thời gian dự kiến (VD: 120 phút)
    public int EstimatedDurationMinutes { get; set; }

    // Danh sách các điểm trong tour
    public ICollection<TourDetail> TourDetails { get; set; } = new List<TourDetail>();
}