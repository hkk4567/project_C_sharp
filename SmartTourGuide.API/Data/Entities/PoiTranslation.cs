using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTourGuide.API.Data.Entities;

public class PoiTranslation
{
    [Key]
    public int Id { get; set; }

    public int PoiId { get; set; }
    [ForeignKey("PoiId")]
    public required Poi Poi { get; set; }

    // Mã ngôn ngữ: "en-US", "ja-JP", "ko-KR"...
    [Required]
    [MaxLength(10)]
    public required string LanguageCode { get; set; }

    [Required]
    [MaxLength(200)]
    public required string TranslatedName { get; set; } // Tên đã dịch

    public required string TranslatedDescription { get; set; } // Mô tả đã dịch

    public required string TranslatedAddress { get; set; }

}