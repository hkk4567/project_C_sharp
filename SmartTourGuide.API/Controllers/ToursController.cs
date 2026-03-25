using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ToursController : ControllerBase
{
    private readonly AppDbContext _context;

    private readonly FileStorageService _fileService;

    public ToursController(AppDbContext context, FileStorageService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    // 👉 HÀM HELPER LẤY USERNAME CỦA NGƯỜI ĐANG THỰC HIỆN
    private string GetCurrentUsername()
    {
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

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
        var query = _context.Tours
            .Include(t => t.TourDetails)
            .ThenInclude(td => td.Poi)
            .Where(t => t.TourDetails.Any()) // Chỉ lấy tour có điểm dừng
            .AsQueryable();

        // Nếu không phải Tiếng Việt, chỉ lấy Tour mà TẤT CẢ POI bên trong đã có bản dịch
        if (langCode != "vi-VN")
        {
            query = query.Where(t => t.TourDetails.All(td =>
                _context.PoiTranslations.Any(trans =>
                    trans.PoiId == td.PoiId && trans.LanguageCode == langCode)));
        }

        var tours = await query.OrderByDescending(t => t.Id).ToListAsync();

        return tours.Select(t => new TourDto
        {
            Id = t.Id,
            Name = t.Name, // Ở đây bạn có thể thêm logic dịch tên Tour nếu có bảng TourTranslation
            Description = t.Description,
            ThumbnailUrl = t.ThumbnailUrl ?? string.Empty,
            TotalPois = t.TourDetails.Count
        }).ToList();
    }

    // 2. Lấy chi tiết 1 Tour (Dịch tên các điểm dừng bên trong)
    // GET: api/tours/mobile/5?langCode=en-US
    [HttpGet("mobile/{id}")]
    public async Task<ActionResult<TourDto>> GetMobileTourDetail(int id, [FromQuery] string langCode = "vi-VN")
    {
        var tour = await _context.Tours
            .Include(t => t.TourDetails)
            .ThenInclude(td => td.Poi)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tour == null) return NotFound();

        // Lấy danh sách ID các POI trong tour để tải bản dịch 1 lần duy nhất (tối ưu performance)
        var poiIds = tour.TourDetails.Select(td => td.PoiId).ToList();
        var translations = await _context.PoiTranslations
            .Where(trans => poiIds.Contains(trans.PoiId) && trans.LanguageCode == langCode)
            .ToListAsync();

        return new TourDto
        {
            Id = tour.Id,
            Name = tour.Name,
            Description = tour.Description,
            ThumbnailUrl = tour.ThumbnailUrl ?? string.Empty,
            TotalPois = tour.TourDetails.Count,
            Pois = tour.TourDetails.OrderBy(td => td.OrderIndex).Select(td =>
            {
                var trans = translations.FirstOrDefault(tr => tr.PoiId == td.PoiId);
                return new TourDetailDto
                {
                    PoiId = td.PoiId,
                    // ƯU TIÊN LẤY TÊN ĐÃ DỊCH CHO MOBILE
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
        var tours = await _context.Tours
            // Sử dụng tính năng Filtered Include của EF Core để chỉ lấy các chi tiết có POI đã Active
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

            // Số lượng điểm bây giờ chỉ đếm những điểm Active
            TotalPois = t.TourDetails.Count
        }).ToList();
    }

    // 2. Chi tiết 1 Tour (Giữ nguyên logic cũ nhưng bỏ Owner)
    // GET: api/tours/5
    [HttpGet("{id}")]
    public async Task<ActionResult<TourDto>> GetTour(int id)
    {
        var tour = await _context.Tours
            // Lọc ngay từ lúc truy vấn Database để tối ưu hiệu năng
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

            // Map danh sách điểm (Lúc này list TourDetails chỉ còn chứa các điểm Active)
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
        // 1. Logic xử lý ảnh bìa
        string thumbnailUrl = "";

        if (thumbnailFile != null)
        {
            thumbnailUrl = await _fileService.SaveFileAsync(thumbnailFile, "tours");
        }
        else if (dto.PoiIds != null && dto.PoiIds.Count > 0)
        {
            var firstPoiId = dto.PoiIds[0];
            var firstPoiImage = await _context.MediaAssets
                .FirstOrDefaultAsync(m => m.PoiId == firstPoiId && m.Type == MediaType.Image);

            if (firstPoiImage != null)
            {
                thumbnailUrl = firstPoiImage.UrlOrContent;
            }
        }

        // 2. Tạo Tour
        var tour = new Tour
        {
            Name = dto.Name,
            Description = dto.Description,
            ThumbnailUrl = thumbnailUrl
        };

        _context.Tours.Add(tour);
        await _context.SaveChangesAsync();

        // 3. Lưu danh sách POI (Nối bảng TourDetail)
        if (dto.PoiIds != null && dto.PoiIds.Count > 0)
        {
            int order = 1;
            var distinctIds = dto.PoiIds.Distinct().ToList();

            foreach (var poiId in distinctIds)
            {
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

        // 4. 🔥 GHI LOG - dùng GetCurrentUsername() thay vì chuỗi cứng "Admin"
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
        var tour = await _context.Tours.FindAsync(id);
        if (tour == null) return NotFound();

        // 🔥 Lưu lại tên tour trước khi xóa để đưa vào câu log
        var tourName = tour.Name;

        _context.Tours.Remove(tour);
        await _context.SaveChangesAsync(); // Cascade delete sẽ tự xóa các TourDetail

        // 🔥 GHI LOG - dùng GetCurrentUsername() thay vì chuỗi cứng "Admin"
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