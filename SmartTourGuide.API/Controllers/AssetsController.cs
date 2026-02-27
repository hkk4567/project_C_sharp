using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AssetsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly FileStorageService _fileService;
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
        // Kiểm tra POI có tồn tại không
        var poi = await _context.Pois.FindAsync(poiId);
        if (poi == null) return NotFound("Địa điểm không tồn tại.");

        if (file == null || file.Length == 0) return BadRequest("File không hợp lệ.");

        // Xác định loại file (Image hay Audio)
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

        // Lưu file vật lý
        var url = await _fileService.SaveFileAsync(file, folder);

        // Lưu vào Database
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
        var poi = await _context.Pois.FindAsync(dto.PoiId);
        if (poi == null) return NotFound("Địa điểm không tồn tại.");

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
        var asset = await _context.MediaAssets.FindAsync(id);
        if (asset == null) return NotFound();

        // Bước 1: Nếu là file ảnh/audio thì phải xóa file vật lý trong wwwroot
        if (asset.Type != MediaType.TtsScript && !string.IsNullOrEmpty(asset.UrlOrContent))
        {
            // asset.UrlOrContent dạng: "/uploads/audio/abc.mp3"
            // Cần chuyển thành đường dẫn tuyệt đối trong ổ cứng
            var relativePath = asset.UrlOrContent.TrimStart('/');
            var fullPath = Path.Combine(_env.WebRootPath, relativePath);

            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        // Bước 2: Xóa trong Database
        _context.MediaAssets.Remove(asset);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã xóa tài nguyên thành công." });
    }
}