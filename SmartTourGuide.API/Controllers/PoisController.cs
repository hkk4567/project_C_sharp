using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PoisController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly FileStorageService _fileService;

    public PoisController(AppDbContext context, FileStorageService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    // 1. API cho App Mobile
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PoiDto>>> GetPois()
    {
        var pois = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .Include(p => p.MediaAssets)
            .Where(p => p.Status == PoiStatus.Active)
            .ToListAsync();

        var result = pois.Select(p => new PoiDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description ?? "", // Đảm bảo gán cho thuộc tính required
            Address = p.Address ?? "",         // Đảm bảo gán cho thuộc tính required
            Status = p.Status.ToString(),
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            TriggerRadius = p.GeofenceSetting?.TriggerRadiusInMeters ?? 50,
            Priority = p.GeofenceSetting?.Priority ?? 1,
            AudioUrls = p.MediaAssets
                .Where(m => m.Type == MediaType.AudioFile)
                .Select(m => m.UrlOrContent).ToList(),
            ImageUrls = p.MediaAssets
                .Where(m => m.Type == MediaType.Image)
                .Select(m => m.UrlOrContent).ToList()
        });

        return Ok(result);
    }

    // 2. API cho Chủ gian hàng: Tạo địa điểm mới
    [HttpPost]
    public async Task<ActionResult> CreatePoi([FromForm] CreatePoiDto dto, [FromForm] List<IFormFile> files)
    {
        var newPoi = new Poi
        {
            Name = dto.Name,
            Description = dto.Description,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            OwnerId = dto.OwnerId,
            Status = PoiStatus.Pending,
            Address = dto.Address ?? "N/A",
            GeofenceSetting = new GeofenceSetting { TriggerRadiusInMeters = 50 }
        };

        _context.Pois.Add(newPoi);
        await _context.SaveChangesAsync();

        if (files != null && files.Count > 0)
        {
            foreach (var file in files)
            {
                var isAudio = file.ContentType.StartsWith("audio");
                var folder = isAudio ? "audio" : "images";
                var url = await _fileService.SaveFileAsync(file, folder);

                _context.MediaAssets.Add(new MediaAsset
                {
                    PoiId = newPoi.Id,
                    Type = isAudio ? MediaType.AudioFile : MediaType.Image,
                    UrlOrContent = url,
                    LanguageCode = "vi-VN"
                });
            }
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Tạo thành công, vui lòng chờ Admin duyệt!", id = newPoi.Id });
    }

    // 2.1. API cho Chủ gian hàng: Lấy danh sách POI của một chủ sở hữu cụ thể
    [HttpGet("owner/{ownerId}")]
    public async Task<ActionResult<IEnumerable<PoiDto>>> GetPoisByOwner(int ownerId)
    {
        var pois = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .Include(p => p.MediaAssets)
            .Where(p => p.OwnerId == ownerId) // Lọc theo Owner
            .OrderByDescending(p => p.Id)   // Mới nhất lên đầu
            .ToListAsync();

        var result = pois.Select(p => new PoiDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            Address = p.Address,
            Status = p.Status.ToString(), // Chuyển Enum sang String
            TriggerRadius = p.GeofenceSetting?.TriggerRadiusInMeters ?? 50,

            // Lấy ảnh đầu tiên làm ảnh đại diện
            ImageUrls = p.MediaAssets
                        .Where(m => m.Type == MediaType.Image)
                        .Select(m => m.UrlOrContent)
                        .ToList()
        });

        return Ok(result);
    }

    //2.2. API cho Chủ gian hàng: xóa địa điểm 
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePoi(int id)
    {
        var poi = await _context.Pois.FindAsync(id);
        if (poi == null) return NotFound();

        // (Tùy chọn) Kiểm tra xem người gọi API có phải là chủ sở hữu không 
        // ở bước này tạm thời bỏ qua để đơn giản hóa

        _context.Pois.Remove(poi);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Đã xóa thành công" });
    }
    //2.3. API cho Chủ gian hàng: sửa địa điểm 
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePoi(int id, [FromForm] CreatePoiDto dto, [FromForm] List<IFormFile> files)
    {
        var poi = await _context.Pois.FindAsync(id);
        if (poi == null) return NotFound("Không tìm thấy địa điểm.");

        // 1. Cập nhật thông tin cơ bản
        poi.Name = dto.Name;
        poi.Description = dto.Description;
        poi.Address = dto.Address ?? "N/A";
        poi.Latitude = dto.Latitude;
        poi.Longitude = dto.Longitude;

        // 2. Logic nghiệp vụ: Nếu đang Active (Đã duyệt) mà sửa -> Chuyển về Pending (Chờ duyệt)
        if (poi.Status == PoiStatus.Active)
        {
            poi.Status = PoiStatus.Pending;
        }
        // Nếu đang Rejected (Bị từ chối) -> Cũng chuyển về Pending để Admin xem lại
        else if (poi.Status == PoiStatus.Rejected)
        {
            poi.Status = PoiStatus.Pending;
        }

        // 3. Xử lý file mới (nếu có upload thêm)
        // Lưu ý: Logic này chỉ thêm file mới, không xóa file cũ.
        // Muốn xóa file cũ thì phải dùng API Delete Asset riêng ở AssetsController
        if (files != null && files.Count > 0)
        {
            foreach (var file in files)
            {
                var isAudio = file.ContentType.StartsWith("audio");
                var folder = isAudio ? "audio" : "images";
                var url = await _fileService.SaveFileAsync(file, folder);

                _context.MediaAssets.Add(new MediaAsset
                {
                    PoiId = poi.Id,
                    Type = isAudio ? MediaType.AudioFile : MediaType.Image,
                    UrlOrContent = url,
                    LanguageCode = "vi-VN"
                });
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Cập nhật thành công!", newStatus = poi.Status.ToString() });
    }
    //2.4. API cho Chủ gian hàng: lấy id của 1 địa điểm
    [HttpGet("{id}")]
    public async Task<ActionResult<PoiDto>> GetPoi(int id)
    {
        var p = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .FirstOrDefaultAsync(x => x.Id == id); // Có thể include thêm MediaAssets nếu muốn hiển thị ảnh cũ

        if (p == null) return NotFound();

        return new PoiDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Address = p.Address,
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            Status = p.Status.ToString(),
            // Map các field khác nếu cần
        };
    }


    // 3. API cho Admin: Duyệt bài
    [HttpPut("{id}/approve")]
    public async Task<IActionResult> ApprovePoi(int id)
    {
        var poi = await _context.Pois.FindAsync(id);
        if (poi == null) return NotFound();

        poi.Status = PoiStatus.Active;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã duyệt địa điểm!" });
    }

    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<PoiDto>>> GetPendingPois()
    {
        var pois = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .Where(p => p.Status == PoiStatus.Pending) // Chỉ lấy bài chờ duyệt
            .ToListAsync();

        var result = pois.Select(p => new PoiDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description ?? "",
            Address = p.Address ?? "",
            Status = p.Status.ToString(),
            Latitude = p.Latitude,
            Longitude = p.Longitude
        });

        return Ok(result);
    }
    // 3.1. API cho Admin: cập nhật cấu hình Geofence
    [HttpPut("{id}/geofence")]
    // [Authorize(Roles = "Admin")] // Bỏ comment dòng này khi bạn đã có JWT Token thực, test thì tạm ẩn
    public async Task<IActionResult> UpdateGeofence(int id, [FromBody] UpdateGeofenceDto dto)
    {
        // 1. Lấy POI kèm theo bảng Setting
        var poi = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (poi == null) return NotFound("Địa điểm không tồn tại");

        // 2. Nếu chưa có setting (dữ liệu cũ), tạo mới
        if (poi.GeofenceSetting == null)
        {
            poi.GeofenceSetting = new GeofenceSetting { PoiId = id };
        }

        // 3. Cập nhật dữ liệu
        poi.GeofenceSetting.TriggerRadiusInMeters = dto.TriggerRadiusInMeters;
        poi.GeofenceSetting.CooldownInSeconds = dto.CooldownInSeconds;
        poi.GeofenceSetting.Priority = dto.Priority;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã cập nhật cấu hình Geofence!" });
    }
}