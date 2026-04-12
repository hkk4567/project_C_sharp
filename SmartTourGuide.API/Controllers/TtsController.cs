using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;

namespace SmartTourGuide.API.Controllers;

// File này quản lý Text-to-Speech (TTS) cho POI.
// - Tạo audio preview để nghe thử
// - Tạo và lưu audio vĩnh viễn vào MediaAssets
// - Tách văn bản dài thành các đoạn nhỏ để gọi TTS ổn định hơn

[Route("api/[controller]")]
[ApiController]
public class TtsController : ControllerBase
{
    // DbContext để lưu audio TTS vào MediaAssets và cập nhật trạng thái POI.
    private readonly AppDbContext _context;
    // Service lưu file nếu cần tái sử dụng luồng lưu trữ của hệ thống.
    private readonly FileStorageService _fileService;
    // Cần để xác định đường dẫn wwwroot khi ghi file MP3 xuống ổ đĩa.
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
        // Không cho preview nếu nội dung rỗng.
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest("Nội dung văn bản không được để trống.");

        // Giới hạn độ dài để tránh request quá lớn và tăng tốc phản hồi.
        if (req.Text.Length > 500)
            return BadRequest("Văn bản tối đa 500 ký tự cho mỗi lần preview.");

        try
        {
            // Tạo audio ngay trong bộ nhớ, không lưu file xuống ổ đĩa.
            var audioBytes = await GenerateAudioAsync(req.Text, req.LanguageCode);

            // Trả về trực tiếp audio stream để người dùng nghe thử.
            return File(audioBytes, "audio/mpeg");
        }
        catch (Exception ex)
        {
            // Nếu dịch vụ TTS lỗi thì trả 503 để client biết đây là lỗi tạm thời.
            return StatusCode(503, $"Không thể tạo audio: {ex.Message}. Vui lòng kiểm tra kết nối mạng.");
        }
    }

    // ─── 2. SAVE: Tạo audio và lưu vĩnh viễn vào MediaAssets của POI ─────────
    // POST: api/tts/save
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] TtsSaveRequest req)
    {
        // Nội dung trống thì không thể tạo audio.
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest("Nội dung văn bản không được để trống.");

        // PoiId phải hợp lệ vì audio này sẽ gắn với một POI cụ thể.
        if (req.PoiId <= 0)
            return BadRequest("PoiId không hợp lệ.");

        // Kiểm tra POI tồn tại trước khi tạo file audio cho nó.
        var poi = await _context.Pois.FindAsync(req.PoiId);
        if (poi == null)
            return NotFound("Địa điểm không tồn tại.");

        try
        {
            // Tạo audio từ văn bản bằng dịch vụ TTS.
            var audioBytes = await GenerateAudioAsync(req.Text, req.LanguageCode);

            // Lưu file MP3 xuống wwwroot/uploads/audio/ để có URL dùng lại về sau.
            var fileName = $"{Guid.NewGuid()}.mp3";
            var webRootPath = string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(_env.ContentRootPath, "wwwroot")
                : _env.WebRootPath;

            var uploadPath = Path.Combine(webRootPath, "uploads", "audio");
            Directory.CreateDirectory(uploadPath);

            var filePath = Path.Combine(uploadPath, fileName);
            await System.IO.File.WriteAllBytesAsync(filePath, audioBytes);

            // URL tương đối để frontend hoặc mobile có thể phát lại audio.
            var fileUrl = $"/uploads/audio/{fileName}";

            // Tạo bản ghi MediaAssets để audio được gắn với POI.
            var asset = new MediaAsset
            {
                PoiId = req.PoiId,
                Type = MediaType.AudioFile,
                UrlOrContent = fileUrl,
                LanguageCode = req.LanguageCode
            };

            _context.MediaAssets.Add(asset);
            // Nếu POI đang Active mà có audio mới, đưa về Pending để admin duyệt lại.
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
        // Chuẩn hóa mã ngôn ngữ: "vi-VN" -> "vi", "en-US" -> "en".
        var lang = languageCode.Contains('-')
            ? languageCode.Split('-')[0].ToLower()
            : languageCode.ToLower();

        // gTTS là Google Translate Text-to-Speech, không cần API key nhưng có giới hạn độ dài.
        // Nếu text dài thì chia thành nhiều đoạn rồi ghép các file MP3 lại.
        var chunks = SplitTextIntoChunks(text, 180);
        var audioChunks = new List<byte[]>();

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(15);

        // Giả lập User-Agent của trình duyệt để hạn chế bị Google chặn request.
        httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");

        foreach (var chunk in chunks)
        {
            var encodedText = Uri.EscapeDataString(chunk);
            // Endpoint TTS của Google Translate.
            var url = $"https://translate.google.com/translate_tts?ie=UTF-8&q={encodedText}&tl={lang}&client=tw-ob&ttsspeed=0.9";

            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"gTTS trả về lỗi {(int)response.StatusCode}. Thử lại sau.");

            var bytes = await response.Content.ReadAsByteArrayAsync();
            audioChunks.Add(bytes);
        }

        // Nếu chỉ có một đoạn thì trả luôn đoạn đó.
        if (audioChunks.Count == 1)
            return audioChunks[0];

        // Nếu có nhiều đoạn thì ghép byte arrays lại thành một file.
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
        // Chia nhỏ văn bản để tránh vượt giới hạn của dịch vụ TTS.
        var chunks = new List<string>();
        if (text.Length <= maxLength)
        {
            chunks.Add(text);
            return chunks;
        }

        // Tách theo dấu câu để đoạn audio nghe tự nhiên hơn.
        var sentences = text.Split(new[] { '.', '!', '?', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var current = "";

        foreach (var sentence in sentences)
        {
            var s = sentence.Trim();
            if (string.IsNullOrEmpty(s)) continue;

            // Nếu ghép thêm mà vượt giới hạn thì đẩy đoạn hiện tại vào danh sách.
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

        // Nếu vẫn không tạo được chunk nào thì cắt thẳng theo độ dài tối đa.
        return chunks.Count > 0 ? chunks : new List<string> { text[..Math.Min(text.Length, maxLength)] };
    }
}

// ─── DTOs ────────────────────────────────────────────────────────────────────
// Request cho preview TTS: chỉ cần text và ngôn ngữ.
public class TtsPreviewRequest
{
    public required string Text { get; set; }
    public string LanguageCode { get; set; } = "vi-VN";
}

// Request cho lưu TTS: có thêm PoiId để gắn audio vào POI.
public class TtsSaveRequest
{
    public required string Text { get; set; }
    public string LanguageCode { get; set; } = "vi-VN";
    public int PoiId { get; set; }
}
