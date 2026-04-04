using System.ComponentModel.DataAnnotations;

namespace SmartTourGuide.Shared.DTOs;

// DTO dùng để TRẢ VỀ thông tin bản dịch của 1 Tour
public class TourTranslationDto
{
    public int Id { get; set; }
    public int TourId { get; set; }
    public required string LanguageCode { get; set; }   // "en-US", "ja-JP"...
    public required string TranslatedName { get; set; }
    public required string TranslatedDescription { get; set; }
}

// DTO dùng để NHẬN DỮ LIỆU khi tạo mới / cập nhật bản dịch
public class SaveTourTranslationDto
{
    [Required]
    public int TourId { get; set; }

    [Required]
    [MaxLength(10)]
    public required string LanguageCode { get; set; }

    [Required]
    [MaxLength(200)]
    public required string TranslatedName { get; set; }

    [Required]
    public required string TranslatedDescription { get; set; }
}