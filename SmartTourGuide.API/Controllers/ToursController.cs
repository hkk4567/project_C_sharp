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

    // 1. Lấy danh sách TẤT CẢ Tour (Dùng cho cả Admin và Mobile App)
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