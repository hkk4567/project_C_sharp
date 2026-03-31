using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;

namespace SmartTourGuide.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TtsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly FileStorageService _fileService;
    private readonly IWebHostEnvironment _env;

    public TtsController(AppDbContext context, FileStorageService fileService, IWebHostEnvironment env)
    {
        _context = context;
        _fileService = fileService;
        _env = env;
    }

    // ─── 1. PREVIEW: Tạo audio tạm thời, trả về stream để nghe thử ───────────
    // POST: api/tts/preview
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] TtsPreviewRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest("Nội dung văn bản không được để trống.");

        if (req.Text.Length > 500)
            return BadRequest("Văn bản tối đa 500 ký tự cho mỗi lần preview.");

        try
        {
            var audioBytes = await GenerateAudioAsync(req.Text, req.LanguageCode);

            // Trả về trực tiếp audio stream để browser nghe thử — KHÔNG lưu file
            return File(audioBytes, "audio/mpeg");
        }
        catch (Exception ex)
        {
            return StatusCode(503, $"Không thể tạo audio: {ex.Message}. Vui lòng kiểm tra kết nối mạng.");
        }
    }

    // ─── 2. SAVE: Tạo audio và lưu vĩnh viễn vào MediaAssets của POI ─────────
    // POST: api/tts/save
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] TtsSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest("Nội dung văn bản không được để trống.");

        if (req.PoiId <= 0)
            return BadRequest("PoiId không hợp lệ.");

        var poi = await _context.Pois.FindAsync(req.PoiId);
        if (poi == null)
            return NotFound("Địa điểm không tồn tại.");

        try
        {
            var audioBytes = await GenerateAudioAsync(req.Text, req.LanguageCode);

            // Lưu file vật lý vào wwwroot/uploads/audio/
            var fileName = $"{Guid.NewGuid()}.mp3";
            var webRootPath = string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(_env.ContentRootPath, "wwwroot")
                : _env.WebRootPath;

            var uploadPath = Path.Combine(webRootPath, "uploads", "audio");
            Directory.CreateDirectory(uploadPath);

            var filePath = Path.Combine(uploadPath, fileName);
            await System.IO.File.WriteAllBytesAsync(filePath, audioBytes);

            var fileUrl = $"/uploads/audio/{fileName}";

            // Lưu vào bảng MediaAssets
            var asset = new MediaAsset
            {
                PoiId = req.PoiId,
                Type = MediaType.AudioFile,
                UrlOrContent = fileUrl,
                LanguageCode = req.LanguageCode
            };

            _context.MediaAssets.Add(asset);
            if (poi.Status == PoiStatus.Active)
            {
                poi.Status = PoiStatus.Pending;
            }
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Đã tạo và lưu audio thành công!",
                assetId = asset.Id,
                url = fileUrl,
                languageCode = req.LanguageCode
            });
        }
        catch (Exception ex)
        {
            return StatusCode(503, $"Không thể tạo audio: {ex.Message}");
        }
    }

    // ─── 3. HELPER: Gọi gTTS (Google Translate TTS - miễn phí) ──────────────
    private static async Task<byte[]> GenerateAudioAsync(string text, string languageCode)
    {
        // Chuẩn hoá language code: "vi-VN" -> "vi", "en-US" -> "en", "ja-JP" -> "ja"
        var lang = languageCode.Contains('-')
            ? languageCode.Split('-')[0].ToLower()
            : languageCode.ToLower();

        // gTTS sử dụng Google Translate TTS API (không cần key, giới hạn ~200 ký tự/request)
        // Nếu text dài thì chia thành nhiều đoạn rồi ghép lại
        var chunks = SplitTextIntoChunks(text, 180);
        var audioChunks = new List<byte[]>();

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(15);

        // User-Agent giả lập trình duyệt để tránh bị chặn
        httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");

        foreach (var chunk in chunks)
        {
            var encodedText = Uri.EscapeDataString(chunk);
            // Google Translate TTS endpoint (miễn phí, không cần API key)
            var url = $"https://translate.google.com/translate_tts?ie=UTF-8&q={encodedText}&tl={lang}&client=tw-ob&ttsspeed=0.9";

            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"gTTS trả về lỗi {(int)response.StatusCode}. Thử lại sau.");

            var bytes = await response.Content.ReadAsByteArrayAsync();
            audioChunks.Add(bytes);
        }

        // Ghép tất cả chunks thành 1 file MP3
        if (audioChunks.Count == 1)
            return audioChunks[0];

        // Ghép đơn giản: nối byte arrays (valid vì mỗi chunk là MP3 độc lập)
        var totalSize = audioChunks.Sum(c => c.Length);
        var result = new byte[totalSize];
        int offset = 0;
        foreach (var chunk in audioChunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }

    // Tách văn bản thành từng đoạn nhỏ, ưu tiên cắt tại dấu câu
    private static List<string> SplitTextIntoChunks(string text, int maxLength)
    {
        var chunks = new List<string>();
        if (text.Length <= maxLength)
        {
            chunks.Add(text);
            return chunks;
        }

        var sentences = text.Split(new[] { '.', '!', '?', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var current = "";

        foreach (var sentence in sentences)
        {
            var s = sentence.Trim();
            if (string.IsNullOrEmpty(s)) continue;

            if ((current + " " + s).Length > maxLength)
            {
                if (!string.IsNullOrEmpty(current))
                    chunks.Add(current.Trim());
                current = s;
            }
            else
            {
                current = string.IsNullOrEmpty(current) ? s : current + ". " + s;
            }
        }

        if (!string.IsNullOrEmpty(current))
            chunks.Add(current.Trim());

        return chunks.Count > 0 ? chunks : new List<string> { text[..Math.Min(text.Length, maxLength)] };
    }
}

// ─── DTOs ────────────────────────────────────────────────────────────────────
public class TtsPreviewRequest
{
    public required string Text { get; set; }
    public string LanguageCode { get; set; } = "vi-VN";
}

public class TtsSaveRequest
{
    public required string Text { get; set; }
    public string LanguageCode { get; set; } = "vi-VN";
    public int PoiId { get; set; }
}
