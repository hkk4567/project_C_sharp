using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

// File này quản lý bản dịch cho Tour.
// - Lấy danh sách bản dịch theo tour hoặc theo ngôn ngữ
// - Tạo mới, cập nhật, xóa bản dịch
// - Ghi activity log khi có thay đổi bản dịch

/// <summary>
/// Quản lý bản dịch (i18n) cho Tour.
/// Tách riêng khỏi ToursController để dễ kiểm soát lỗi và mở rộng sau.
/// </summary>
[Route("api/tour-translations")]
[ApiController]
public class TourTranslationsController : ControllerBase
{
    // DbContext dùng để truy vấn Tour, TourTranslation và ActivityLog.
    private readonly AppDbContext _context;

    public TourTranslationsController(AppDbContext context)
    {
        _context = context;
    }

    // Lấy username hiện tại để ghi log thao tác bản dịch.
    private string GetCurrentUsername()
    {
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        var headerName = HttpContext.Request.Headers["X-User-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerName)) return headerName;

        return "Unknown";
    }

    // ─────────────────────────────────────────────────────────────
    // 1. GET tất cả bản dịch của 1 Tour
    //    GET: api/tour-translations/{tourId}
    // ─────────────────────────────────────────────────────────────
    [HttpGet("{tourId}")]
    public async Task<ActionResult<List<TourTranslationDto>>> GetAllByTour(int tourId)
    {
        // Kiểm tra Tour tồn tại trước để tránh trả dữ liệu cho Id không hợp lệ.
        var tourExists = await _context.Tours.AnyAsync(t => t.Id == tourId);
        if (!tourExists)
            return NotFound(new { message = $"Không tìm thấy Tour với Id = {tourId}." });

        // DTO (Data Transfer Object) chỉ trả về các trường cần cho frontend.
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
    //    GET: api/tour-translations/{tourId}/{
    // 
    // Code}
    //    VD:  GET: api/tour-translations/5/en-US
    // ─────────────────────────────────────────────────────────────
    [HttpGet("{tourId}/{langCode}")]
    public async Task<ActionResult<TourTranslationDto>> GetByLanguage(int tourId, string langCode)
    {
        // Tìm bản dịch đúng theo TourId và mã ngôn ngữ.
        var trans = await _context.TourTranslations
            .FirstOrDefaultAsync(t => t.TourId == tourId && t.LanguageCode == langCode);

        if (trans == null)
        {
            // Trả object rỗng để frontend biết chưa có bản dịch mà không bị lỗi parse.
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
        // Kiểm tra Tour tồn tại trước khi lưu bản dịch.
        var tourExists = await _context.Tours.AnyAsync(t => t.Id == dto.TourId);
        if (!tourExists)
            return NotFound(new { message = $"Không tìm thấy Tour với Id = {dto.TourId}." });

        // Upsert nghĩa là: nếu đã có thì cập nhật, chưa có thì tạo mới.
        var existing = await _context.TourTranslations
            .FirstOrDefaultAsync(t => t.TourId == dto.TourId && t.LanguageCode == dto.LanguageCode);
        var isNewTranslation = existing == null;
        var tour = await _context.Tours.FirstOrDefaultAsync(t => t.Id == dto.TourId);

        if (existing == null)
        {
            // Tạo mới bản dịch khi chưa tồn tại.
            var newTrans = new TourTranslation
            {
                TourId = dto.TourId,
                LanguageCode = dto.LanguageCode,
                TranslatedName = dto.TranslatedName.Trim(),
                TranslatedDescription = dto.TranslatedDescription.Trim(),
                Tour = null!
            };
            _context.TourTranslations.Add(newTrans);
            existing = newTrans;
        }
        else
        {
            // Cập nhật bản dịch đã có.
            existing.TranslatedName = dto.TranslatedName.Trim();
            existing.TranslatedDescription = dto.TranslatedDescription.Trim();
        }

        // Ghi log để theo dõi ai đã thêm hoặc sửa bản dịch nào.
        var username = GetCurrentUsername();
        _context.ActivityLogs.Add(new ActivityLog
        {
            ActivityType = "SaveTourTranslation",
            Description = $"{username} {(isNewTranslation ? "đã thêm" : "đã cập nhật")} ngôn ngữ {dto.LanguageCode} cho Tour '{tour?.Name ?? dto.TourId.ToString()}'",
            UserName = username,
            Timestamp = DateTime.Now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        });

        await _context.SaveChangesAsync();

        if (isNewTranslation)
        {
            // CreatedAtAction trả về trạng thái 201 và gắn luôn đường dẫn tới bản dịch vừa tạo.
            return CreatedAtAction(
                nameof(GetByLanguage),
                new { tourId = existing.TourId, langCode = existing.LanguageCode },
                new { message = "Đã tạo bản dịch mới thành công.", id = existing.Id }
            );
        }

        return Ok(new { message = "Đã cập nhật bản dịch thành công.", id = existing.Id });
    }

    // ─────────────────────────────────────────────────────────────
    // 4. DELETE bản dịch theo ngôn ngữ
    //    DELETE: api/tour-translations/{tourId}/{langCode}
    //    VD:     DELETE: api/tour-translations/5/en-US
    // ─────────────────────────────────────────────────────────────
    [HttpDelete("{tourId}/{langCode}")]
    public async Task<IActionResult> DeleteTranslation(int tourId, string langCode)
    {
        // Tìm đúng bản dịch theo tour và ngôn ngữ trước khi xóa.
        var trans = await _context.TourTranslations
            .FirstOrDefaultAsync(t => t.TourId == tourId && t.LanguageCode == langCode);

        if (trans == null)
            return NotFound(new { message = $"Không tìm thấy bản dịch '{langCode}' cho Tour Id = {tourId}." });

        // Xóa bản dịch khỏi database.
        _context.TourTranslations.Remove(trans);
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Đã xoá bản dịch '{langCode}' của Tour Id = {tourId}." });
    }
}