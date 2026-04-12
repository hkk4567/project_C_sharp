using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

// File này quản lý tài nguyên đa phương tiện của POI.
// - Upload ảnh hoặc audio gắn với một địa điểm
// - Lưu kịch bản TTS (Text-to-Speech) vào database
// - Xóa tài nguyên cả trên database lẫn file vật lý trong wwwroot
[Route("api/[controller]")]
[ApiController]
public class AssetsController : ControllerBase
{
    // DbContext để làm việc với POI và MediaAssets.
    private readonly AppDbContext _context;
    // Service lưu file vật lý vào wwwroot.
    private readonly FileStorageService _fileService;
    // Môi trường hosting để xác định đường dẫn gốc khi cần xóa file.
    private readonly IWebHostEnvironment _env; // Dùng để lấy đường dẫn gốc khi xóa file

    public AssetsController(AppDbContext context, FileStorageService fileService, IWebHostEnvironment env)
    {
        _context = context;
        _fileService = fileService;
        _env = env;
    }

    // 1. API Upload file (Ảnh hoặc Audio) bổ sung cho POI
    // POST: api/assets/upload/5 (5 là PoiId)
    [HttpPost("upload/{poiId}")]
    public async Task<IActionResult> UploadAsset(int poiId, IFormFile file, [FromQuery] string language = "vi-VN")
    {
        // Kiểm tra POI có tồn tại trước khi lưu tài nguyên.
        var poi = await _context.Pois.FindAsync(poiId);
        if (poi == null) return NotFound("Địa điểm không tồn tại.");

        // Bỏ qua request nếu file rỗng hoặc không hợp lệ.
        if (file == null || file.Length == 0) return BadRequest("File không hợp lệ.");

        // Xác định media type và thư mục lưu tương ứng.
        MediaType type;
        string folder;

        if (file.ContentType.StartsWith("image"))
        {
            type = MediaType.Image;
            folder = "images";
        }
        else if (file.ContentType.StartsWith("audio"))
        {
            type = MediaType.AudioFile;
            folder = "audio";
        }
        else
        {
            return BadRequest("Chỉ hỗ trợ file ảnh hoặc âm thanh.");
        }

        // Lưu file vật lý vào hệ thống file và nhận URL trả về.
        var url = await _fileService.SaveFileAsync(file, folder);

        // Lưu metadata của file vào database để POI có thể dùng lại.
        var asset = new MediaAsset
        {
            PoiId = poiId,
            Type = type,
            UrlOrContent = url,
            LanguageCode = language
        };

        _context.MediaAssets.Add(asset);
        await _context.SaveChangesAsync();

        return Ok(new { id = asset.Id, url = asset.UrlOrContent, msg = "Upload thành công!" });
    }

    // 2. API Thêm kịch bản Text-to-Speech (Slide 3)
    // POST: api/assets/script
    [HttpPost("script")]
    public async Task<IActionResult> AddTtsScript([FromBody] CreateTtsScriptDto dto)
    {
        // Chỉ cho phép tạo kịch bản khi POI đã tồn tại.
        var poi = await _context.Pois.FindAsync(dto.PoiId);
        if (poi == null) return NotFound("Địa điểm không tồn tại.");

        // Lưu nội dung script trực tiếp vào MediaAssets với kiểu TTS.
        var asset = new MediaAsset
        {
            PoiId = dto.PoiId,
            Type = MediaType.TtsScript,
            UrlOrContent = dto.Content, // Lưu nội dung văn bản vào cột này
            LanguageCode = dto.LanguageCode,
            VoiceGender = dto.VoiceGender
        };

        _context.MediaAssets.Add(asset);
        await _context.SaveChangesAsync();

        return Ok(new { id = asset.Id, content = asset.UrlOrContent, msg = "Lưu kịch bản nói thành công!" });
    }

    // 3. API Xóa tài nguyên
    // DELETE: api/assets/10
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsset(int id)
    {
        // Tìm asset cần xóa trong database.
        var asset = await _context.MediaAssets.FindAsync(id);
        if (asset == null) return NotFound();

        // Nếu là file ảnh/audio thì xóa luôn file vật lý trong wwwroot.
        if (asset.Type != MediaType.TtsScript && !string.IsNullOrEmpty(asset.UrlOrContent))
        {
            // Chuyển URL lưu trong DB sang đường dẫn tuyệt đối trên ổ đĩa.
            var relativePath = asset.UrlOrContent.TrimStart('/');
            var webRootPath = string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(_env.ContentRootPath, "wwwroot")
                : _env.WebRootPath;
            var fullPath = Path.Combine(webRootPath, relativePath);

            // Xóa file nếu còn tồn tại để tránh dữ liệu rác trên ổ đĩa.
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        // Sau khi xử lý file vật lý, xóa record khỏi database.
        _context.MediaAssets.Remove(asset);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã xóa tài nguyên thành công." });
    }
}