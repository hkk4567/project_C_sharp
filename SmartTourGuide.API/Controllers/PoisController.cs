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
    // 1. API cho App Mobile (Có hỗ trợ Đa ngôn ngữ)
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PoiDto>>> GetPois([FromQuery] string langCode = "vi-VN")
    {
        // Lấy danh sách địa điểm Active
        var pois = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .Include(p => p.MediaAssets)
            .Where(p => p.Status == PoiStatus.Active)
            .ToListAsync();

        // Lấy tất cả bản dịch theo ngôn ngữ yêu cầu (nếu khác tiếng Việt)
        var translations = new List<PoiTranslation>();
        if (langCode != "vi-VN")
        {
            translations = await _context.PoiTranslations
                .Where(t => t.LanguageCode == langCode)
                .ToListAsync();
        }

        var result = pois.Select(p =>
        {
            // Tìm xem địa điểm này có bản dịch không
            var trans = translations.FirstOrDefault(t => t.PoiId == p.Id);

            // Lọc file Audio theo ngôn ngữ
            var audioList = p.MediaAssets.Where(m => m.Type == MediaType.AudioFile);
            if (langCode != "vi-VN")
            {
                // Nếu ngôn ngữ khác, lấy audio của ngôn ngữ đó
                audioList = audioList.Where(m => m.LanguageCode == langCode);
            }
            else
            {
                // Nếu tiếng Việt, lấy audio gốc (vi-VN hoặc chưa set)
                audioList = audioList.Where(m => m.LanguageCode == "vi-VN" || string.IsNullOrEmpty(m.LanguageCode));
            }

            return new PoiDto
            {
                Id = p.Id,
                // NẾU CÓ BẢN DỊCH THÌ LẤY BẢN DỊCH, KHÔNG THÌ LẤY BẢN GỐC (Fallback)
                Name = trans?.TranslatedName ?? p.Name,
                Description = trans?.TranslatedDescription ?? p.Description ?? "",
                Address = trans?.TranslatedAddress ?? p.Address ?? "",

                Status = p.Status.ToString(),
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                TriggerRadius = p.GeofenceSetting?.TriggerRadiusInMeters ?? 50,
                Priority = p.GeofenceSetting?.Priority ?? 1,

                AudioUrls = audioList.Select(m => m.UrlOrContent).ToList(),
                ImageUrls = p.MediaAssets.Where(m => m.Type == MediaType.Image).Select(m => m.UrlOrContent).ToList()
            };
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
            .Where(p => p.OwnerId == ownerId)
            .OrderByDescending(p => p.Id)
            .ToListAsync();

        var result = pois.Select(p => new PoiDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            Address = p.Address,
            Status = p.Status.ToString(),
            TriggerRadius = p.GeofenceSetting?.TriggerRadiusInMeters ?? 50,

            ImageUrls = p.MediaAssets.Where(m => m.Type == MediaType.Image).Select(m => m.UrlOrContent).ToList(),

            // Code cũ: AudioUrls = ...
            AudioUrls = p.MediaAssets.Where(m => m.Type == MediaType.AudioFile).Select(m => m.UrlOrContent).ToList(),

            // MỚI: Lấy danh sách Audio Tiếng Việt (hoặc mặc định) KÈM THEO ID
            ExistingAudios = p.MediaAssets
                .Where(m => m.Type == MediaType.AudioFile && (m.LanguageCode == "vi-VN" || string.IsNullOrEmpty(m.LanguageCode)))
                .Select(m => new MediaAssetDto { Id = m.Id, Url = m.UrlOrContent })
                .ToList()
        });

        return Ok(result);
    }

    //2.2. API cho Chủ gian hàng: xóa địa điểm 
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePoi(int id)
    {
        var poi = await _context.Pois.FindAsync(id);
        if (poi == null) return NotFound("Không tìm thấy địa điểm.");

        // --- MỚI: KIỂM TRA RÀNG BUỘC VỚI BẢNG TOUR ---
        // Kiểm tra xem ID của địa điểm này có đang nằm trong bất kỳ chi tiết Tour nào không
        bool isInAnyTour = await _context.TourDetails.AnyAsync(td => td.PoiId == id);

        if (isInAnyTour)
        {
            // Trả về lỗi 400 (BadRequest) kèm câu thông báo cho chủ gian hàng
            return BadRequest("Địa điểm này đang nằm trong một Tuyến Du Lịch (Tour). Vui lòng liên hệ Admin để gỡ địa điểm ra khỏi Tour trước khi xóa!");
        }

        // --- MỚI: XÓA CÁC BẢN DỊCH (Nếu có) ---
        // Lấy tất cả bản dịch của địa điểm này và xóa
        var translations = await _context.PoiTranslations.Where(t => t.PoiId == id).ToListAsync();
        if (translations.Any())
        {
            _context.PoiTranslations.RemoveRange(translations);
        }

        // Xóa địa điểm gốc (EF Core sẽ tự động Cascade xóa MediaAssets và GeofenceSetting đi kèm)
        _context.Pois.Remove(poi);

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã xóa địa điểm thành công!" });
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

    // 3.2. API cho Admin: Xem chi tiết POI (Bao gồm Audio/Ảnh đầy đủ để duyệt)
    [HttpGet("admin/{id}")]
    public async Task<ActionResult<PoiDto>> GetPoiForAdmin(int id)
    {
        var p = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .Include(p => p.MediaAssets) // Quan trọng: Admin cần xem ảnh/nghe audio để duyệt
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p == null) return NotFound();

        return Ok(new PoiDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description ?? "",
            Address = p.Address ?? "",
            Status = p.Status.ToString(),
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            OwnerId = p.OwnerId,

            // Map thông tin cấu hình Geofence
            TriggerRadius = p.GeofenceSetting?.TriggerRadiusInMeters ?? 50,
            Priority = p.GeofenceSetting?.Priority ?? 1,

            // Tách riêng Audio và Ảnh để hiển thị lên giao diện Admin
            AudioUrls = p.MediaAssets
                .Where(m => m.Type == MediaType.AudioFile)
                .Select(m => m.UrlOrContent).ToList(),

            ImageUrls = p.MediaAssets
                .Where(m => m.Type == MediaType.Image)
                .Select(m => m.UrlOrContent).ToList()
        });
    }
}