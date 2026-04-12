using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

// File này quản lý toàn bộ nghiệp vụ của Tour.
// - Mobile: lấy danh sách và chi tiết tour theo ngôn ngữ
// - Web/Admin: xem, tạo, xóa tour và danh sách điểm dừng
// - Xử lý ảnh bìa, bảng nối TourDetail và activity log

[Route("api/[controller]")]
[ApiController]
public class ToursController : ControllerBase
{
    // DbContext để thao tác với Tour, TourDetail, POI và log.
    private readonly AppDbContext _context;

    // Service lưu file ảnh bìa của tour vào wwwroot.
    private readonly FileStorageService _fileService;

    public ToursController(AppDbContext context, FileStorageService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    // 👉 HÀM HELPER LẤY USERNAME CỦA NGƯỜI ĐANG THỰC HIỆN
    private string GetCurrentUsername()
    {
        // Ưu tiên lấy từ Identity/Token nếu người dùng đã đăng nhập.
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        // Nếu chưa có token thì lấy từ header phụ để phục vụ test hoặc request nội bộ.
        var headerName = HttpContext.Request.Headers["X-User-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerName)) return headerName;

        return "Unknown";
    }

    // --- ENDPOINT DÀNH RIÊNG CHO MOBILE ---

    // 1. Lấy danh sách Tour đã lọc theo ngôn ngữ
    // GET: api/tours/mobile?langCode=en-US
    [HttpGet("mobile")]
    public async Task<ActionResult<List<TourDto>>> GetMobileTours([FromQuery] string langCode = "vi-VN")
    {
        // Lấy các tour có ít nhất một điểm dừng.
        var query = _context.Tours
            .Include(t => t.TourDetails)
            .ThenInclude(td => td.Poi)
            .Where(t => t.TourDetails.Any()) // Chỉ lấy tour có điểm dừng
            .AsQueryable();

        // AsQueryable() giúp tiếp tục ghép điều kiện lọc động trước khi query chạy xuống DB.

        // Nếu không phải tiếng Việt, chỉ lấy tour mà TẤT CẢ POI bên trong đã có bản dịch.
        if (langCode != "vi-VN")
        {
            query = query.Where(t => t.TourDetails.All(td =>
                _context.PoiTranslations.Any(trans =>
                    trans.PoiId == td.PoiId && trans.LanguageCode == langCode)));
        }

        var tours = await query.OrderByDescending(t => t.Id).ToListAsync();

        // Lấy bản dịch tên/mô tả của tour nếu ngôn ngữ yêu cầu khác tiếng Việt.
        var tourIds = tours.Select(t => t.Id).ToList();
        var tourTranslations = langCode != "vi-VN"
            ? await _context.TourTranslations
                .Where(tt => tourIds.Contains(tt.TourId) && tt.LanguageCode == langCode)
                .ToListAsync()
            : new List<TourTranslation>();

        return tours.Select(t =>
        {
            var tt = tourTranslations.FirstOrDefault(x => x.TourId == t.Id);
            return new TourDto
            {
                Id = t.Id,
                Name = tt?.TranslatedName ?? t.Name,
                Description = tt?.TranslatedDescription ?? t.Description,
                ThumbnailUrl = t.ThumbnailUrl ?? string.Empty,
                TotalPois = t.TourDetails.Count
            };
        }).ToList();
    }

    // 2. Lấy chi tiết 1 Tour (Dịch tên các điểm dừng bên trong)
    // GET: api/tours/mobile/5?langCode=en-US
    [HttpGet("mobile/{id}")]
    public async Task<ActionResult<TourDto>> GetMobileTourDetail(int id, [FromQuery] string langCode = "vi-VN")
    {
        // Lấy tour kèm danh sách điểm dừng để dựng chi tiết cho mobile.
        var tour = await _context.Tours
            .Include(t => t.TourDetails)
            .ThenInclude(td => td.Poi)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tour == null) return NotFound();

        // Lấy toàn bộ ID POI trong tour để tải bản dịch một lần, tránh query lặp lại nhiều lần.
        var poiIds = tour.TourDetails.Select(td => td.PoiId).ToList();
        var translations = await _context.PoiTranslations
            .Where(trans => poiIds.Contains(trans.PoiId) && trans.LanguageCode == langCode)
            .ToListAsync();

        // Lấy bản dịch tên/mô tả của tour nếu cần.
        var tourTrans = langCode != "vi-VN"
            ? await _context.TourTranslations
                .FirstOrDefaultAsync(tt => tt.TourId == id && tt.LanguageCode == langCode)
            : null;

        return new TourDto
        {
            Id = tour.Id,
            Name = tourTrans?.TranslatedName ?? tour.Name,
            Description = tourTrans?.TranslatedDescription ?? tour.Description,
            ThumbnailUrl = tour.ThumbnailUrl ?? string.Empty,
            TotalPois = tour.TourDetails.Count,
            Pois = tour.TourDetails.OrderBy(td => td.OrderIndex).Select(td =>
            {
                var trans = translations.FirstOrDefault(tr => tr.PoiId == td.PoiId);
                return new TourDetailDto
                {
                    PoiId = td.PoiId,
                    // Ưu tiên lấy tên đã dịch; nếu không có thì dùng tên gốc.
                    PoiName = trans?.TranslatedName ?? td.Poi.Name,
                    Address = trans?.TranslatedAddress ?? td.Poi.Address,
                    Latitude = td.Poi.Latitude,
                    Longitude = td.Poi.Longitude,
                    OrderIndex = td.OrderIndex
                };
            }).ToList()
        };
    }

    // --- ENDPOINT DÀNH RIÊNG CHO WEB ---
    // 1. Lấy danh sách TẤT CẢ Tour (Dùng cho Admin)
    // GET: api/tours
    [HttpGet]
    public async Task<ActionResult<List<TourDto>>> GetAllTours()
    {
        // Filtered Include: chỉ nạp các TourDetail có POI đang Active.
        var tours = await _context.Tours
            .Include(t => t.TourDetails.Where(td => td.Poi.Status == PoiStatus.Active))
                .ThenInclude(td => td.Poi)
            .OrderByDescending(t => t.Id)
            .ToListAsync();

        return tours.Select(t => new TourDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            ThumbnailUrl = t.ThumbnailUrl ?? string.Empty,

            // Số lượng điểm chỉ tính các POI còn Active.
            TotalPois = t.TourDetails.Count
        }).ToList();
    }

    // 2. Chi tiết 1 Tour (Giữ nguyên logic cũ nhưng bỏ Owner)
    // GET: api/tours/5
    [HttpGet("{id}")]
    public async Task<ActionResult<TourDto>> GetTour(int id)
    {
        // Lấy tour và chỉ giữ các điểm dừng đang Active để tránh trả dữ liệu không còn hợp lệ.
        var tour = await _context.Tours
            .Include(t => t.TourDetails.Where(td => td.Poi.Status == PoiStatus.Active))
                .ThenInclude(td => td.Poi)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tour == null) return NotFound();

        return new TourDto
        {
            Id = tour.Id,
            Name = tour.Name,
            Description = tour.Description,
            ThumbnailUrl = tour.ThumbnailUrl ?? string.Empty,
            TotalPois = tour.TourDetails.Count,

            // Map danh sách điểm dừng sang DTO để frontend dùng.
            Pois = tour.TourDetails
                .OrderBy(td => td.OrderIndex)
                .Select(td => new TourDetailDto
                {
                    PoiId = td.PoiId,
                    PoiName = td.Poi.Name,
                    Address = td.Poi.Address,
                    Latitude = td.Poi.Latitude,
                    Longitude = td.Poi.Longitude,
                    OrderIndex = td.OrderIndex
                }).ToList()
        };
    }

    // 3. Admin tạo Tour mới
    // POST: api/tours
    [HttpPost]
    public async Task<IActionResult> CreateTour([FromForm] CreateTourDto dto, IFormFile? thumbnailFile)
    {
        // Xử lý ảnh bìa cho tour.
        string thumbnailUrl = "";

        if (thumbnailFile != null)
        {
            // Nếu người dùng upload ảnh riêng thì lưu file đó làm thumbnail.
            thumbnailUrl = await _fileService.SaveFileAsync(thumbnailFile, "tours");
        }
        else if (dto.PoiIds != null && dto.PoiIds.Count > 0)
        {
            // Nếu không có ảnh bìa, lấy ảnh đầu tiên của POI đầu tiên làm ảnh đại diện.
            var firstPoiId = dto.PoiIds[0];
            var firstPoiImage = await _context.MediaAssets
                .FirstOrDefaultAsync(m => m.PoiId == firstPoiId && m.Type == MediaType.Image);

            if (firstPoiImage != null)
            {
                thumbnailUrl = firstPoiImage.UrlOrContent;
            }
        }

        // Tạo bản ghi Tour mới.
        var tour = new Tour
        {
            Name = dto.Name,
            Description = dto.Description,
            ThumbnailUrl = thumbnailUrl
        };

        _context.Tours.Add(tour);
        await _context.SaveChangesAsync();

        // Lưu các POI của tour vào bảng nối TourDetail.
        if (dto.PoiIds != null && dto.PoiIds.Count > 0)
        {
            int order = 1;
            var distinctIds = dto.PoiIds.Distinct().ToList();

            foreach (var poiId in distinctIds)
            {
                // Bỏ qua POI không tồn tại để tránh lỗi khi tạo tour.
                var poi = await _context.Pois.FindAsync(poiId);
                if (poi == null) continue;

                var detail = new TourDetail
                {
                    Tour = tour,
                    Poi = poi,
                    TourId = tour.Id,
                    PoiId = poiId,
                    OrderIndex = order++
                };
                _context.TourDetails.Add(detail);
            }
            await _context.SaveChangesAsync();
        }

        // Ghi activity log bằng đúng username hiện tại, không hard-code "Admin".
        var username = GetCurrentUsername();
        var log = new ActivityLog
        {
            ActivityType = "CreateTour",
            Description = $"Admin {username} đã tạo Tour mới: '{tour.Name}'",
            UserName = username,
            Timestamp = DateTime.Now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        };

        _context.ActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Tạo tour thành công", tourId = tour.Id });
    }

    // 4. Admin xóa Tour
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTour(int id)
    {
        // Tìm tour cần xóa.
        var tour = await _context.Tours.FindAsync(id);
        if (tour == null) return NotFound();

        // Lưu tên tour trước khi xóa để ghi log sau này.
        var tourName = tour.Name;

        _context.Tours.Remove(tour);
        // Cascade delete nghĩa là dữ liệu con (TourDetail) sẽ bị xóa theo khi Tour bị xóa.
        await _context.SaveChangesAsync();

        // Ghi log thao tác xóa tour.
        var username = GetCurrentUsername();
        var log = new ActivityLog
        {
            ActivityType = "DeleteTour",
            Description = $"Admin {username} đã xóa Tour: '{tourName}'",
            UserName = username,
            Timestamp = DateTime.Now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        };

        _context.ActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã xóa tour." });
    }
}