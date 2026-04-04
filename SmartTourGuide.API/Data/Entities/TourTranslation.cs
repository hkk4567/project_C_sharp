using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTourGuide.API.Data.Entities;

public class TourTranslation
{
    [Key]
    public int Id { get; set; }

    // Khoá ngoại trỏ về Tour gốc
    public int TourId { get; set; }

    [ForeignKey("TourId")]
    public required Tour Tour { get; set; }

    // Mã ngôn ngữ: "en-US", "ja-JP", "ko-KR", "zh-CN"...
    [Required]
    [MaxLength(10)]
    public required string LanguageCode { get; set; }

    // Tên Tour đã dịch (VD: "Hanoi City Tour")
    [Required]
    [MaxLength(200)]
    public required string TranslatedName { get; set; }

    // Mô tả Tour đã dịch
    [Required]
    public required string TranslatedDescription { get; set; }
}