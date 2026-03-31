using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TranslationsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly FileStorageService _fileService;

    public TranslationsController(AppDbContext context, FileStorageService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    // 1. GET: Lấy nội dung dịch (bao gồm list audio)
    [HttpGet("{poiId}/{langCode}")]
    public async Task<ActionResult<PoiTranslationDto>> GetTranslation(int poiId, string langCode)
    {
        // A. Lấy Text
        var trans = await _context.PoiTranslations
            .FirstOrDefaultAsync(x => x.PoiId == poiId && x.LanguageCode == langCode);

        // B. Lấy Audio thuộc ngôn ngữ này
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
        // --- PHẦN 1: LƯU TEXT ---
        var trans = await _context.PoiTranslations
            .FirstOrDefaultAsync(x => x.PoiId == dto.PoiId && x.LanguageCode == dto.LanguageCode);

        if (trans == null)
        {
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
            trans.TranslatedName = dto.Name;
            trans.TranslatedDescription = dto.Description;
            trans.TranslatedAddress = dto.Address;
        }

        // --- PHẦN 2: LƯU AUDIO (Nếu có upload thêm) ---
        if (audioFiles != null && audioFiles.Count > 0)
        {
            foreach (var file in audioFiles)
            {
                var url = await _fileService.SaveFileAsync(file, "audio");

                // Lưu vào MediaAssets với LanguageCode tương ứng
                _context.MediaAssets.Add(new MediaAsset
                {
                    PoiId = dto.PoiId,
                    Type = MediaType.AudioFile,
                    UrlOrContent = url,
                    LanguageCode = dto.LanguageCode // Quan trọng: Đánh dấu file này thuộc ngôn ngữ nào
                });
            }
        }
        // --- PHẦN 3: RESET STATUS POI VỀ PENDING ---
        var poi = await _context.Pois.FindAsync(dto.PoiId);
        if (poi != null && poi.Status == PoiStatus.Active)
        {
            poi.Status = PoiStatus.Pending;
        }
        await _context.SaveChangesAsync();
        return Ok(new { message = "Lưu thành công!" });
    }
}