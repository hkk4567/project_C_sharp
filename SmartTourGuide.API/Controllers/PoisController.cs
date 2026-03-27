using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

/// <summary>
/// Controller quản lý toàn bộ nghiệp vụ liên quan đến Địa điểm (POI - Point of Interest).
///
/// Phân quyền sử dụng:
///   - Mobile App  → GET api/pois/mobile          (lấy danh sách Active, hỗ trợ đa ngôn ngữ)
///   - Admin       → GET api/pois                 (danh sách Active dùng cho Web Admin)
///   - Admin       → GET api/pois/pending         (danh sách chờ duyệt)
///   - Admin       → GET/PUT api/pois/admin/{id}  (xem chi tiết + duyệt/từ chối)
///   - Owner       → GET api/pois/owner/{id}      (danh sách POI của chủ sở hữu)
///   - Owner       → POST / PUT / DELETE          (tạo, sửa, xóa POI của mình)
/// </summary>
/// 
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

    // ═══════════════════════════════════════════════════════════════════════
    //  HELPER
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lấy username của người đang thực hiện thao tác.
    /// Thứ tự ưu tiên: JWT Token → Header "X-User-Name" → "Unknown".
    /// </summary>
    private string GetCurrentUsername()
    {
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        var headerName = HttpContext.Request.Headers["X-User-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerName)) return headerName;

        return "Unknown";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MOBILE APP
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [MOBILE] Lấy danh sách địa điểm đang hoạt động (Status = Active) cho ứng dụng di động.
    ///
    /// Endpoint này được tách riêng khỏi GET api/pois để tránh ảnh hưởng đến Web Admin.
    /// Mọi thay đổi dành riêng cho Mobile (ẩn danh hóa, filter ngôn ngữ, v.v.)
    /// chỉ cần thực hiện ở đây.
    ///
    /// Logic ngôn ngữ:
    ///   - Tiếng Việt (vi-VN): trả về tất cả POI Active, dùng tên/mô tả gốc làm fallback.
    ///   - Ngôn ngữ khác     : chỉ trả về POI đã có bản dịch cho ngôn ngữ đó.
    ///
    /// Audio trả về được lọc theo đúng ngôn ngữ yêu cầu.
    /// </summary>
    /// <param name="langCode">Mã ngôn ngữ theo chuẩn IETF BCP 47 (mặc định: "vi-VN").</param>
    // GET api/pois/mobile?langCode=en-US

    [HttpGet("mobile")]
    public async Task<ActionResult<IEnumerable<PoiDto>>> GetMobilePois([FromQuery] string langCode = "vi-VN")
    {
        // 1. Lấy Query các địa điểm đang Active
        var query = _context.Pois
            .Include(p => p.GeofenceSetting)
            .Include(p => p.MediaAssets)
            .Where(p => p.Status == PoiStatus.Active)
            .AsQueryable();

        // 2. TỐI ƯU: Vừa lọc, vừa lấy bản dịch trong 1 câu truy vấn SQL (Left Join)
        var mobileQuery = query.Select(p => new
        {
            Poi = p,
            Translation = _context.PoiTranslations
                .FirstOrDefault(t => t.PoiId == p.Id && t.LanguageCode == langCode)
        });

        // 3. LOGIC QUAN TRỌNG: 
        // Nếu là ngôn ngữ nước ngoài, bắt buộc phải có bản dịch mới lấy (Translation != null)
        if (langCode != "vi-VN")
        {
            mobileQuery = mobileQuery.Where(x => x.Translation != null);
        }

        var data = await mobileQuery.ToListAsync();

        // 4. Map sang DTO để trả về cho App
        var result = data.Select(x =>
        {
            var p = x.Poi;
            var trans = x.Translation;

            // Lọc audio theo ngôn ngữ
            var audioList = p.MediaAssets.Where(m => m.Type == MediaType.AudioFile);
            if (langCode != "vi-VN")
                audioList = audioList.Where(m => m.LanguageCode == langCode);
            else
                audioList = audioList.Where(m => m.LanguageCode == "vi-VN" || string.IsNullOrEmpty(m.LanguageCode));

            return new PoiDto
            {
                Id = p.Id,
                Name = trans?.TranslatedName ?? p.Name,
                Description = trans?.TranslatedDescription ?? p.Description ?? "",
                Address = trans?.TranslatedAddress ?? p.Address ?? "",
                Status = p.Status.ToString(),
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                TriggerRadius = p.GeofenceSetting?.TriggerRadiusInMeters ?? 50,
                CooldownInSeconds = p.GeofenceSetting?.CooldownInSeconds ?? 300,
                Priority = p.GeofenceSetting?.Priority ?? 1,
                AudioUrls = audioList.Select(m => m.UrlOrContent).ToList(),
                ImageUrls = p.MediaAssets.Where(m => m.Type == MediaType.Image).Select(m => m.UrlOrContent).ToList()
            };
        });

        return Ok(result);
    }

    // 1. API Poi (Có hỗ trợ Đa ngôn ngữ)
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
                CooldownInSeconds = p.GeofenceSetting?.CooldownInSeconds ?? 300,
                Priority = p.GeofenceSetting?.Priority ?? 1,

                AudioUrls = audioList.Select(m => m.UrlOrContent).ToList(),
                ImageUrls = p.MediaAssets.Where(m => m.Type == MediaType.Image).Select(m => m.UrlOrContent).ToList()
            };
        });

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult> CreatePoi([FromForm] CreatePoiDto dto, [FromForm] List<IFormFile> files)
    {
        // 1. Khởi tạo POI mới
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

        // 🔥 BẮT BUỘC PHẢI CÓ DÒNG NÀY ĐỂ MYSQL TẠO RA ID CHO ĐỊA ĐIỂM 🔥
        await _context.SaveChangesAsync();

        // 2. Xử lý Upload file 
        if (files != null && files.Count > 0)
        {
            foreach (var file in files)
            {
                var isAudio = file.ContentType.StartsWith("audio");
                var folder = isAudio ? "audio" : "images";
                var url = await _fileService.SaveFileAsync(file, folder);

                _context.MediaAssets.Add(new MediaAsset
                {
                    PoiId = newPoi.Id, // Lúc này newPoi.Id mới có giá trị hợp lệ (vd: 5, 6, 7)
                    Type = isAudio ? MediaType.AudioFile : MediaType.Image,
                    UrlOrContent = url,
                    LanguageCode = "vi-VN"
                });
            }
        }

        // 3. Xử lý Ghi Log
        // Ưu tiên: JWT/Header -> OwnerId từ DB (fallback khi owner tự tạo POI của mình)
        var currentUsername = GetCurrentUsername();
        if (currentUsername == "Unknown")
        {
            var owner = await _context.Users.FindAsync(dto.OwnerId);
            currentUsername = owner?.Username ?? $"Owner_{dto.OwnerId}";
        }

        var log = new ActivityLog
        {
            ActivityType = "CreatePOI",
            Description = $"Chủ gian hàng [{currentUsername}] đã tạo địa điểm mới: '{dto.Name}' (Chờ duyệt)",
            UserName = currentUsername,
            Timestamp = DateTime.Now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        };

        _context.ActivityLogs.Add(log);

        // 4. Lưu lại toàn bộ File Media và Log
        await _context.SaveChangesAsync();

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

        // --- CHECK TOUR ---
        bool isInAnyTour = await _context.TourDetails.AnyAsync(td => td.PoiId == id);

        if (isInAnyTour)
        {
            return BadRequest("Địa điểm này đang nằm trong một Tuyến Du Lịch (Tour). Vui lòng liên hệ Admin để gỡ địa điểm ra khỏi Tour trước khi xóa!");
        }

        // --- XÓA TRANSLATIONS ---
        var translations = await _context.PoiTranslations
            .Where(t => t.PoiId == id)
            .ToListAsync();

        if (translations.Any())
        {
            _context.PoiTranslations.RemoveRange(translations);
        }

        // 🔥 LẤY THÔNG TIN TRƯỚC KHI XÓA
        var poiName = poi.Name;

        // 👉 Lấy username từ bảng Users (theo OwnerId)
        var user = await _context.Users.FindAsync(poi.OwnerId);
        var username = user?.Username ?? "Unknown";

        // 👉 Lấy IP
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        _context.Pois.Remove(poi);

        await _context.SaveChangesAsync();

        // 🔥 GHI LOG ĐÚNG FORMAT DB CỦA BẠN
        var log = new ActivityLog
        {
            ActivityType = "DeletePOI",
            Description = $"User {username} đã xóa POI: {poiName}",
            UserName = username,
            Timestamp = DateTime.Now,
            IpAddress = ip ?? ""
        };

        _context.ActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã xóa địa điểm thành công!" });
    }

    //2.3. API cho Chủ gian hàng: sửa địa điểm 
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePoi(int id, [FromForm] CreatePoiDto dto, [FromForm] List<IFormFile> files)
    {
        var poi = await _context.Pois.FindAsync(id);
        if (poi == null) return NotFound("Không tìm thấy địa điểm.");

        // 🔥 LẤY THÔNG TIN CŨ
        var oldName = poi.Name;

        // 👉 Lấy username thật từ DB
        var user = await _context.Users.FindAsync(poi.OwnerId);
        var username = user?.Username ?? "Unknown";

        // 1. Cập nhật thông tin
        poi.Name = dto.Name;
        poi.Description = dto.Description;
        poi.Address = dto.Address ?? "N/A";
        poi.Latitude = dto.Latitude;
        poi.Longitude = dto.Longitude;

        // 2. Logic nghiệp vụ
        if (poi.Status == PoiStatus.Active || poi.Status == PoiStatus.Rejected)
        {
            poi.Status = PoiStatus.Pending;
        }

        // 3. Upload file
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

        // ✅ SAVE TRƯỚC
        await _context.SaveChangesAsync();

        // 🔥 GHI LOG SAU KHI THÀNH CÔNG
        var log = new ActivityLog
        {
            ActivityType = "UpdatePOI",
            Description = $"User {username} cập nhật POI: {oldName} → {poi.Name}",
            UserName = username,
            Timestamp = DateTime.Now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        };

        _context.ActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Cập nhật thành công!", newStatus = poi.Status.ToString() });
    }

    //2.4. API cho Chủ gian hàng: lấy id của 1 địa điểm
    [HttpGet("{id}")]
    public async Task<ActionResult<PoiDto>> GetPoi(int id)
    {
        var p = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .Include(p => p.MediaAssets) // QUAN TRỌNG: Bổ sung dòng này để lấy Ảnh/Audio cũ
            .FirstOrDefaultAsync(x => x.Id == id);

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
            OwnerId = p.OwnerId,

            // Map thông tin cấu hình Geofence
            TriggerRadius = p.GeofenceSetting?.TriggerRadiusInMeters ?? 50,
            Priority = p.GeofenceSetting?.Priority ?? 1,

            // BẮT BUỘC: Map Ảnh và Audio để trang Sửa (Edit) hiển thị được dữ liệu cũ
            ImageUrls = p.MediaAssets.Where(m => m.Type == MediaType.Image).Select(m => m.UrlOrContent).ToList(),
            AudioUrls = p.MediaAssets.Where(m => m.Type == MediaType.AudioFile).Select(m => m.UrlOrContent).ToList(),
            ExistingAudios = p.MediaAssets
                .Where(m => m.Type == MediaType.AudioFile && (m.LanguageCode == "vi-VN" || string.IsNullOrEmpty(m.LanguageCode)))
                .Select(m => new MediaAssetDto { Id = m.Id, Url = m.UrlOrContent })
                .ToList()
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

        // 🔥 LẤY USERNAME NGƯỜI THỰC HIỆN (admin), không dùng chuỗi cứng "Admin"
        var username = GetCurrentUsername();

        // 🔥 LOG
        _context.ActivityLogs.Add(new ActivityLog
        {
            ActivityType = "ApprovePOI",
            Description = $"{username} đã duyệt POI: {poi.Name} (ID: {id})",
            UserName = username,
            Timestamp = DateTime.Now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        });

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã duyệt địa điểm!" });
    }

    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<PoiDto>>> GetPendingPois()
    {
        var pois = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .Include(p => p.MediaAssets) // Bổ sung Include MediaAssets
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
            Longitude = p.Longitude,

            // Bổ sung lấy ảnh đại diện để hiển thị trên UI Admin
            ImageUrls = p.MediaAssets.Where(m => m.Type == MediaType.Image).Select(m => m.UrlOrContent).ToList()
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
                .Select(m => m.UrlOrContent).ToList(),

            ExistingAudios = p.MediaAssets
                .Where(m => m.Type == MediaType.AudioFile)
                .Select(m => new MediaAssetDto
                {
                    Id = m.Id,
                    Url = m.UrlOrContent,
                    LanguageCode = string.IsNullOrEmpty(m.LanguageCode) ? "vi-VN" : m.LanguageCode
                }).ToList()
        });
    }
    //3.3 Từ chối
    // 3. API cho Admin: Từ chối bài
    [HttpPut("{id}/reject")]
    public async Task<IActionResult> RejectPoi(int id)
    {
        var poi = await _context.Pois.FindAsync(id);
        if (poi == null) return NotFound("Không tìm thấy địa điểm.");

        // Chuyển trạng thái sang Rejected
        poi.Status = PoiStatus.Rejected;

        await _context.SaveChangesAsync();

        // 🔥 LẤY USERNAME NGƯỜI THỰC HIỆN (admin), không dùng chuỗi cứng "Admin"
        var username = GetCurrentUsername();

        // 🔥 GHI LOG
        _context.ActivityLogs.Add(new ActivityLog
        {
            ActivityType = "RejectPOI",
            Description = $"{username} đã từ chối POI: {poi.Name} (ID: {id})",
            UserName = username,
            Timestamp = DateTime.Now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        });

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã từ chối địa điểm!" });
    }
    // 4. API đếm số lượng địa điểm đang chờ duyệt (Dùng cho bảng thống kê ActivityLog)
    [HttpGet("pending-count")]
    public async Task<ActionResult<int>> GetPendingCount()
    {
        // Sử dụng đúng Enum PoiStatus.Pending của bạn
        var count = await _context.Pois.CountAsync(p => p.Status == PoiStatus.Pending);
        return Ok(count);
    }
}