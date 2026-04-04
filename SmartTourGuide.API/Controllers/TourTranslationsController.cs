using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

/// <summary>
/// Quản lý bản dịch (i18n) cho Tour.
/// Tách riêng khỏi ToursController để dễ kiểm soát lỗi và mở rộng sau.
/// </summary>
[Route("api/tour-translations")]
[ApiController]
public class TourTranslationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public TourTranslationsController(AppDbContext context)
    {
        _context = context;
    }

    // ─────────────────────────────────────────────────────────────
    // 1. GET tất cả bản dịch của 1 Tour
    //    GET: api/tour-translations/{tourId}
    // ─────────────────────────────────────────────────────────────
    [HttpGet("{tourId}")]
    public async Task<ActionResult<List<TourTranslationDto>>> GetAllByTour(int tourId)
    {
        // Kiểm tra Tour tồn tại trước
        var tourExists = await _context.Tours.AnyAsync(t => t.Id == tourId);
        if (!tourExists)
            return NotFound(new { message = $"Không tìm thấy Tour với Id = {tourId}." });

        var translations = await _context.TourTranslations
            .Where(t => t.TourId == tourId)
            .OrderBy(t => t.LanguageCode)
            .Select(t => new TourTranslationDto
            {
                Id = t.Id,
                TourId = t.TourId,
                LanguageCode = t.LanguageCode,
                TranslatedName = t.TranslatedName,
                TranslatedDescription = t.TranslatedDescription
            })
            .ToListAsync();

        return Ok(translations);
    }

    // ─────────────────────────────────────────────────────────────
    // 2. GET bản dịch của 1 Tour theo ngôn ngữ cụ thể
    //    GET: api/tour-translations/{tourId}/{langCode}
    //    VD:  GET: api/tour-translations/5/en-US
    // ─────────────────────────────────────────────────────────────
    [HttpGet("{tourId}/{langCode}")]
    public async Task<ActionResult<TourTranslationDto>> GetByLanguage(int tourId, string langCode)
    {
        var trans = await _context.TourTranslations
            .FirstOrDefaultAsync(t => t.TourId == tourId && t.LanguageCode == langCode);

        if (trans == null)
        {
            // Trả về object rỗng (giống pattern của TranslationsController POI)
            // để phía Frontend biết chưa có bản dịch, không bị crash
            return Ok(new TourTranslationDto
            {
                Id = 0,
                TourId = tourId,
                LanguageCode = langCode,
                TranslatedName = string.Empty,
                TranslatedDescription = string.Empty
            });
        }

        return Ok(new TourTranslationDto
        {
            Id = trans.Id,
            TourId = trans.TourId,
            LanguageCode = trans.LanguageCode,
            TranslatedName = trans.TranslatedName,
            TranslatedDescription = trans.TranslatedDescription
        });
    }

    // ─────────────────────────────────────────────────────────────
    // 3. POST / PUT — Tạo mới hoặc cập nhật bản dịch (Upsert)
    //    POST: api/tour-translations
    //    Body: SaveTourTranslationDto (JSON)
    // ─────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> SaveTranslation([FromBody] SaveTourTranslationDto dto)
    {
        // Validate Tour tồn tại
        var tourExists = await _context.Tours.AnyAsync(t => t.Id == dto.TourId);
        if (!tourExists)
            return NotFound(new { message = $"Không tìm thấy Tour với Id = {dto.TourId}." });

        // Tìm bản dịch đã có (nếu có -> Update, chưa có -> Insert)
        var existing = await _context.TourTranslations
            .FirstOrDefaultAsync(t => t.TourId == dto.TourId && t.LanguageCode == dto.LanguageCode);

        if (existing == null)
        {
            // --- INSERT ---
            var newTrans = new TourTranslation
            {
                TourId = dto.TourId,
                LanguageCode = dto.LanguageCode,
                TranslatedName = dto.TranslatedName.Trim(),
                TranslatedDescription = dto.TranslatedDescription.Trim(),
                Tour = null!
            };
            _context.TourTranslations.Add(newTrans);
            await _context.SaveChangesAsync();

            return CreatedAtAction(
                nameof(GetByLanguage),
                new { tourId = newTrans.TourId, langCode = newTrans.LanguageCode },
                new { message = "Đã tạo bản dịch mới thành công.", id = newTrans.Id }
            );
        }
        else
        {
            // --- UPDATE ---
            existing.TranslatedName = dto.TranslatedName.Trim();
            existing.TranslatedDescription = dto.TranslatedDescription.Trim();
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã cập nhật bản dịch thành công.", id = existing.Id });
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 4. DELETE bản dịch theo ngôn ngữ
    //    DELETE: api/tour-translations/{tourId}/{langCode}
    //    VD:     DELETE: api/tour-translations/5/en-US
    // ─────────────────────────────────────────────────────────────
    [HttpDelete("{tourId}/{langCode}")]
    public async Task<IActionResult> DeleteTranslation(int tourId, string langCode)
    {
        var trans = await _context.TourTranslations
            .FirstOrDefaultAsync(t => t.TourId == tourId && t.LanguageCode == langCode);

        if (trans == null)
            return NotFound(new { message = $"Không tìm thấy bản dịch '{langCode}' cho Tour Id = {tourId}." });

        _context.TourTranslations.Remove(trans);
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Đã xoá bản dịch '{langCode}' của Tour Id = {tourId}." });
    }
}