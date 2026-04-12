using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

// File này quản lý bản dịch cho POI.
// - Lấy text dịch và danh sách audio theo ngôn ngữ
// - Lưu/cập nhật bản dịch kèm file audio
// - Đưa POI về Pending khi nội dung dịch thay đổi

[Route("api/[controller]")]
[ApiController]
public class TranslationsController : ControllerBase
{
    // DbContext để làm việc với POI, bản dịch, media và activity log.
    private readonly AppDbContext _context;
    // Service lưu file audio khi upload bản dịch mới.
    private readonly FileStorageService _fileService;

    public TranslationsController(AppDbContext context, FileStorageService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    // Lấy username hiện tại để ghi lại ai đã thêm/cập nhật bản dịch.
    private string GetCurrentUsername()
    {
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        var headerName = HttpContext.Request.Headers["X-User-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerName)) return headerName;

        return "Unknown";
    }

    // 1. GET: Lấy nội dung dịch (bao gồm list audio)
    [HttpGet("{poiId}/{langCode}")]
    public async Task<ActionResult<PoiTranslationDto>> GetTranslation(int poiId, string langCode)
    {
        // Lấy phần text dịch của POI theo đúng ngôn ngữ.
        var trans = await _context.PoiTranslations
            .FirstOrDefaultAsync(x => x.PoiId == poiId && x.LanguageCode == langCode);

        // Lấy danh sách audio thuộc đúng ngôn ngữ.
        var audios = await _context.MediaAssets
            .Where(m => m.PoiId == poiId
                     && m.Type == MediaType.AudioFile
                     && (m.LanguageCode == langCode
                         || (langCode == "vi-VN" && string.IsNullOrEmpty(m.LanguageCode))))
            .Select(m => new MediaAssetDto
            {
                Id = m.Id,
                Url = m.UrlOrContent,
                LanguageCode = string.IsNullOrEmpty(m.LanguageCode) ? "vi-VN" : m.LanguageCode
            })
            .ToListAsync();

        if (trans == null)
        {
            // Nếu chưa có text dịch thì vẫn trả audio để frontend không bị thiếu dữ liệu.
            return Ok(new PoiTranslationDto
            {
                PoiId = poiId,
                LanguageCode = langCode,
                Name = string.Empty,
                Description = string.Empty,
                Address = string.Empty,
                Audios = audios // Vẫn trả về audio nếu có (trường hợp dịch text chưa lưu nhưng audio đã up)
            });
        }

        // Nếu có text dịch thì trả luôn text + audio trong một DTO.
        return Ok(new PoiTranslationDto
        {
            PoiId = trans.PoiId,
            LanguageCode = trans.LanguageCode,
            Name = trans.TranslatedName,
            Description = trans.TranslatedDescription,
            Address = trans.TranslatedAddress,
            Audios = audios // Gán list audio vào
        });
    }

    // 2. POST: Lưu dịch + Upload NHIỀU file audio
    [HttpPost]
    public async Task<IActionResult> SaveTranslation([FromForm] PoiTranslationDto dto, [FromForm] List<IFormFile> audioFiles)
    {
        // Dùng cờ này để biết đây là bản dịch mới hay cập nhật bản dịch cũ.
        var isNewTranslation = false;

        // Phần 1: lưu nội dung text dịch.
        var trans = await _context.PoiTranslations
            .FirstOrDefaultAsync(x => x.PoiId == dto.PoiId && x.LanguageCode == dto.LanguageCode);

        if (trans == null)
        {
            isNewTranslation = true;
            // Chưa có bản dịch thì tạo mới.
            trans = new PoiTranslation
            {
                PoiId = dto.PoiId,
                LanguageCode = dto.LanguageCode,
                TranslatedName = dto.Name,
                TranslatedDescription = dto.Description,
                TranslatedAddress = dto.Address,
                Poi = null!
            };
            _context.PoiTranslations.Add(trans);
        }
        else
        {
            // Đã có rồi thì cập nhật lại nội dung.
            trans.TranslatedName = dto.Name;
            trans.TranslatedDescription = dto.Description;
            trans.TranslatedAddress = dto.Address;
        }

        // Phần 2: lưu thêm file audio nếu frontend upload kèm.
        if (audioFiles != null && audioFiles.Count > 0)
        {
            foreach (var file in audioFiles)
            {
                // Lưu file vào thư mục audio và nhận đường dẫn trả về.
                var url = await _fileService.SaveFileAsync(file, "audio");

                // Lưu metadata của file vào MediaAssets và gắn ngôn ngữ tương ứng.
                _context.MediaAssets.Add(new MediaAsset
                {
                    PoiId = dto.PoiId,
                    Type = MediaType.AudioFile,
                    UrlOrContent = url,
                    LanguageCode = dto.LanguageCode // Quan trọng: Đánh dấu file này thuộc ngôn ngữ nào
                });
            }
        }

        // Phần 3: khi nội dung thay đổi, đưa POI về Pending để admin duyệt lại.
        var poi = await _context.Pois.FindAsync(dto.PoiId);
        if (poi != null && poi.Status == PoiStatus.Active)
        {
            poi.Status = PoiStatus.Pending;
        }

        // Ghi activity log để biết ai đã thay đổi bản dịch nào.
        var username = GetCurrentUsername();
        _context.ActivityLogs.Add(new ActivityLog
        {
            ActivityType = "SavePoiTranslation",
            Description = $"{username} {(isNewTranslation ? "đã thêm" : "đã cập nhật")} ngôn ngữ {dto.LanguageCode} cho POI '{poi?.Name ?? dto.PoiId.ToString()}'",
            UserName = username,
            Timestamp = DateTime.Now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        });

        await _context.SaveChangesAsync();
        return Ok(new { message = "Lưu thành công!" });
    }
}