using System.ComponentModel.DataAnnotations;
namespace SmartTourGuide.API.Data.Entities
{
    public class MediaAsset
    {
        [Key]
        public int Id { get; set; }

        public int PoiId { get; set; }
        public virtual Poi Poi { get; set; } = null!;

        // Loại: Image, AudioFile, hoặc TtsScript (kịch bản đọc)
        public MediaType Type { get; set; }

        // Đường dẫn file (nếu là ảnh/audio) hoặc nội dung text (nếu là Script)
        public required string UrlOrContent { get; set; }

        // Ngôn ngữ: "vi-VN", "en-US"
        public required string LanguageCode { get; set; } = "vi-VN";

        // Slide 3: Giọng đọc (nếu dùng TTS)
        public string? VoiceGender { get; set; } // Male/Female
    }

    public enum MediaType { Image, AudioFile, TtsScript }
}
