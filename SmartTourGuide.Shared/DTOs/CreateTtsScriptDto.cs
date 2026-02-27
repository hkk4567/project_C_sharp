using System.ComponentModel.DataAnnotations;

namespace SmartTourGuide.Shared.DTOs;

public class CreateTtsScriptDto
{
    [Required]
    public int PoiId { get; set; }

    [Required(ErrorMessage = "Nội dung văn bản không được để trống")]
    public required string Content { get; set; } // Sẽ map vào UrlOrContent của Entity

    [Required]
    [RegularExpression(@"^[a-z]{2}-[A-Z]{2}$", ErrorMessage = "Định dạng ngôn ngữ phải là xx-XX (vd: vi-VN)")]
    public string LanguageCode { get; set; } = "vi-VN";

    [Required]
    public string VoiceGender { get; set; } = "Female";
}