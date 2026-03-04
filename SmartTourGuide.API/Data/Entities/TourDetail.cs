using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTourGuide.API.Data.Entities;

public class TourDetail
{
    [Key]
    public int Id { get; set; }

    // Khóa ngoại trỏ về Tour
    public int TourId { get; set; }
    public required Tour Tour { get; set; }

    // Khóa ngoại trỏ về POI
    public int PoiId { get; set; }
    public required Poi Poi { get; set; }

    // QUAN TRỌNG: Thứ tự điểm đi (1, 2, 3...)
    public int OrderIndex { get; set; }

    // (Tùy chọn) Ghi chú riêng cho điểm này trong tour này
    // Ví dụ: "Tại điểm này nghỉ ăn trưa 30p"
    public string? Note { get; set; }
}