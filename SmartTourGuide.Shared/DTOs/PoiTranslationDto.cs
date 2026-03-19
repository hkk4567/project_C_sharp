namespace SmartTourGuide.Shared.DTOs;

public class PoiTranslationDto
{
    public int PoiId { get; set; }
    public required string LanguageCode { get; set; } // "en-US"
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Address { get; set; }
    public List<MediaAssetDto> Audios { get; set; } = new();
}

public class MediaAssetDto
{
    public int Id { get; set; }
    public string? Url { get; set; }
    public string LanguageCode { get; set; } = "vi-VN";
}