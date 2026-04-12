using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.API.Controllers;

// File này quản lý toàn bộ nghiệp vụ của POI (địa điểm).
// - Mobile: lấy danh sách POI đang hoạt động
// - Owner: tạo, sửa, xóa POI của mình
// - Admin: duyệt, từ chối, xem chi tiết và cập nhật geofence

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
    // DbContext thao tác với POI, log, notification và dữ liệu liên quan.
    private readonly AppDbContext _context;
    // Dùng để lưu file ảnh/audio khi tạo hoặc cập nhật POI.
    private readonly FileStorageService _fileService;
    // Cần thiết khi xóa file vật lý trong wwwroot.
    private readonly IWebHostEnvironment _env;

    public PoisController(AppDbContext context, FileStorageService fileService, IWebHostEnvironment env)
    {
        _context = context;
        _fileService = fileService;
        _env = env;
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
        // Ưu tiên lấy từ Claims/Identity nếu request đã đăng nhập.
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        // Fallback cho các request test hoặc khi client chỉ gửi header.
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
        // Chỉ lấy POI đang Active để app mobile hiển thị dữ liệu đã duyệt.
        var query = _context.Pois
            .Include(p => p.GeofenceSetting)
            .Include(p => p.MediaAssets)
            .Where(p => p.Status == PoiStatus.Active)
            .AsQueryable();

        // Lấy bản dịch theo ngôn ngữ ngay trong truy vấn để giảm round-trip DB.
        var mobileQuery = query.Select(p => new
        {
            Poi = p,
            Translation = _context.PoiTranslations
                .FirstOrDefault(t => t.PoiId == p.Id && t.LanguageCode == langCode)
        });

        // Nếu là ngôn ngữ khác tiếng Việt thì bắt buộc phải có bản dịch.
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

            // Chỉ trả audio đúng ngôn ngữ để app phát đúng nội dung.
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
        // Endpoint cho web/admin: lấy các POI đã được duyệt và đang hoạt động.
        var pois = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .Include(p => p.MediaAssets)
            .Where(p => p.Status == PoiStatus.Active)
            .ToListAsync();

        // Nếu là ngôn ngữ khác tiếng Việt thì lấy trước danh sách bản dịch tương ứng.
        var translations = new List<PoiTranslation>();
        if (langCode != "vi-VN")
        {
            translations = await _context.PoiTranslations
                .Where(t => t.LanguageCode == langCode)
                .ToListAsync();
        }

        var result = pois.Select(p =>
        {
            // Tìm bản dịch của POI hiện tại trong tập dữ liệu đã tải.
            var trans = translations.FirstOrDefault(t => t.PoiId == p.Id);

            // Lọc audio theo ngôn ngữ yêu cầu.
            var audioList = p.MediaAssets.Where(m => m.Type == MediaType.AudioFile);
            if (langCode != "vi-VN")
            {
                // Ngôn ngữ khác thì chỉ lấy audio tương ứng.
                audioList = audioList.Where(m => m.LanguageCode == langCode);
            }
            else
            {
                // Tiếng Việt thì lấy audio gốc hoặc audio chưa khai báo ngôn ngữ.
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
        // Khởi tạo POI mới ở trạng thái Pending để chờ admin duyệt.
        var newPoi = new Poi
        {
            Name = dto.Name,
            Description = dto.Description ?? string.Empty,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            OwnerId = dto.OwnerId,
            Status = PoiStatus.Pending,
            Address = dto.Address ?? "N/A",
            GeofenceSetting = new GeofenceSetting { TriggerRadiusInMeters = 50 }
        };

        _context.Pois.Add(newPoi);

        // Lưu sớm để DB sinh Id cho POI trước khi thêm MediaAssets.
        await _context.SaveChangesAsync();

        // Xử lý upload ảnh/audio gắn với POI vừa tạo.
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

        // Xác định username người tạo để ghi log đúng nguồn.
        var currentUsername = GetCurrentUsername();
        if (currentUsername == "Unknown")
        {
            // Nếu request không có username, dùng OwnerId từ form làm fallback.
            var owner = await _context.Users.FindAsync(dto.OwnerId);
            currentUsername = owner?.Username ?? $"Owner_{dto.OwnerId}";
        }

        // Ghi activity log cho thao tác tạo POI.
        var log = new ActivityLog
        {
            ActivityType = "CreatePOI",
            Description = $"Chủ gian hàng [{currentUsername}] đã tạo địa điểm mới: '{dto.Name}' (Chờ duyệt)",
            UserName = currentUsername,
            Timestamp = DateTime.Now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        };

        _context.ActivityLogs.Add(log);

        // Tạo notification cho toàn bộ admin để họ biết có POI mới cần duyệt.
        var adminIds = await _context.Users
            .Where(u => u.Role == SmartTourGuide.Shared.Enums.UserRole.Admin)
            .Select(u => u.Id)
            .ToListAsync();

        foreach (var adminId in adminIds)
        {
            _context.AdminNotifications.Add(new AdminNotification
            {
                AdminId = adminId,
                PoiId = newPoi.Id,
                OwnerUsername = currentUsername,
                Title = "Yêu cầu duyệt POI",
                Message = $"{currentUsername} yêu cầu duyệt địa điểm '{dto.Name}'.",
                CreatedAt = DateTime.Now,
                IsRead = false
            });
        }

        // Commit toàn bộ POI, media, log và notifications trong một lần lưu.
        await _context.SaveChangesAsync();

        return Ok(new { message = "Tạo thành công, vui lòng chờ Admin duyệt!", id = newPoi.Id });
    }


    // 2.1. API cho Chủ gian hàng: Lấy danh sách POI của một chủ sở hữu cụ thể
    [HttpGet("owner/{ownerId}")]
    public async Task<ActionResult<IEnumerable<PoiDto>>> GetPoisByOwner(int ownerId)
    {
        // Lấy danh sách POI thuộc riêng một owner để màn hình quản lý hiển thị.
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
                .Select(m => new MediaAssetDto
                {
                    Id = m.Id,
                    Url = m.UrlOrContent,
                    LanguageCode = string.IsNullOrEmpty(m.LanguageCode) ? "vi-VN" : m.LanguageCode
                })
                .ToList()
        });

        return Ok(result);
    }
    //2.2. API cho Chủ gian hàng: xóa địa điểm 

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePoi(int id)
    {
        // Include MediaAssets để xóa luôn file vật lý tương ứng trên ổ đĩa.
        var poi = await _context.Pois
            .Include(p => p.MediaAssets)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (poi == null) return NotFound("Không tìm thấy địa điểm.");

        // Không cho xóa nếu POI đang được dùng trong Tour.
        bool isInAnyTour = await _context.TourDetails.AnyAsync(td => td.PoiId == id);

        if (isInAnyTour)
        {
            return BadRequest("Địa điểm này đang nằm trong một Tuyến Du Lịch (Tour). Vui lòng liên hệ Admin để gỡ địa điểm ra khỏi Tour trước khi xóa!");
        }

        // Xóa toàn bộ file ảnh/audio trên wwwroot trước khi xóa record POI.
        var webRootPath = string.IsNullOrEmpty(_env.WebRootPath)
            ? Path.Combine(_env.ContentRootPath, "wwwroot")
            : _env.WebRootPath;

        foreach (var asset in poi.MediaAssets)
        {
            // TtsScript lưu text thuần, không có file vật lý → bỏ qua
            if (asset.Type == MediaType.TtsScript) continue;
            if (string.IsNullOrEmpty(asset.UrlOrContent)) continue;

            var relativePath = asset.UrlOrContent.Replace("\\", "/").TrimStart('/');
            var fullPath = Path.Combine(webRootPath, relativePath);

            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }

        // Xóa toàn bộ bản dịch của POI để tránh dữ liệu mồ côi.
        var translations = await _context.PoiTranslations
            .Where(t => t.PoiId == id)
            .ToListAsync();

        if (translations.Any())
        {
            _context.PoiTranslations.RemoveRange(translations);
        }

        // Lưu lại thông tin cần thiết trước khi xóa để ghi log.
        var poiName = poi.Name;

        // Lấy username chủ sở hữu từ bảng Users.
        var user = await _context.Users.FindAsync(poi.OwnerId);
        var username = user?.Username ?? "Unknown";

        // Lấy IP của request để phục vụ audit.
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        _context.Pois.Remove(poi);

        await _context.SaveChangesAsync();

        // Ghi log thao tác xóa sau khi đã xóa thành công.
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
        // Tìm POI cần cập nhật.
        var poi = await _context.Pois.FindAsync(id);
        if (poi == null) return NotFound("Không tìm thấy địa điểm.");

        // Lưu tên cũ để ghi log theo kiểu trước/sau.
        var oldName = poi.Name;

        // Lấy username thật từ DB để ghi log.
        var user = await _context.Users.FindAsync(poi.OwnerId);
        var username = user?.Username ?? "Unknown";

        // Cập nhật thông tin cơ bản của POI.
        poi.Name = dto.Name;
        poi.Description = dto.Description ?? string.Empty;
        poi.Address = dto.Address ?? "N/A";
        poi.Latitude = dto.Latitude;
        poi.Longitude = dto.Longitude;

        // Khi có chỉnh sửa thì đưa POI về Pending để admin duyệt lại.
        if (poi.Status != PoiStatus.Pending)
        {
            poi.Status = PoiStatus.Pending;
        }

        // Nếu có file mới thì lưu thêm ảnh/audio cho POI.
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

        // Lưu thay đổi POI và media trước khi ghi log.
        await _context.SaveChangesAsync();

        // Ghi activity log sau khi cập nhật thành công.
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
        // Lấy POI kèm hình, audio và cấu hình geofence để phục vụ màn hình edit.
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

            // Map cấu hình geofence sang DTO.
            TriggerRadius = p.GeofenceSetting?.TriggerRadiusInMeters ?? 50,
            Priority = p.GeofenceSetting?.Priority ?? 1,

            // Trả lại media cũ để form sửa có thể hiển thị đầy đủ dữ liệu.
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
        // Admin duyệt POI bằng cách chuyển sang trạng thái Active.
        var poi = await _context.Pois.FindAsync(id);
        if (poi == null) return NotFound();

        // LẤY USERNAME NGƯỜI THỰC HIỆN (admin), không dùng chuỗi cứng "Admin"
        var username = GetCurrentUsername();

        poi.Status = PoiStatus.Active;

        // Tạo thông báo cho chủ gian hàng khi POI được duyệt
        _context.OwnerNotifications.Add(new OwnerNotification
        {
            OwnerId = poi.OwnerId,
            PoiId = poi.Id,
            AdminUsername = username,
            Title = "POI đã được duyệt",
            Message = $"{username} đã duyệt địa điểm '{poi.Name}'.",
            CreatedAt = DateTime.Now,
            IsRead = false
        });

        await _context.SaveChangesAsync();

        // Ghi activity log cho hành động duyệt POI.
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
        // Chỉ lấy những POI đang chờ duyệt để admin xử lý.
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

            // Lấy ảnh đại diện để giao diện admin xem nhanh.
            ImageUrls = p.MediaAssets.Where(m => m.Type == MediaType.Image).Select(m => m.UrlOrContent).ToList()
        });

        return Ok(result);
    }

    // 3.1. API cho Admin: cập nhật cấu hình Geofence
    [HttpPut("{id}/geofence")]
    // [Authorize(Roles = "Admin")] // Bỏ comment dòng này khi bạn đã có JWT Token thực, test thì tạm ẩn
    public async Task<IActionResult> UpdateGeofence(int id, [FromBody] UpdateGeofenceDto dto)
    {
        // Lấy POI cùng GeofenceSetting hiện tại.
        var poi = await _context.Pois
            .Include(p => p.GeofenceSetting)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (poi == null) return NotFound("Địa điểm không tồn tại");

        // Nếu POI chưa có setting thì tạo mới để cập nhật.
        if (poi.GeofenceSetting == null)
        {
            poi.GeofenceSetting = new GeofenceSetting { PoiId = id };
        }

        // Cập nhật thông số geofence từ DTO.
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
        // Admin cần xem đủ media và cấu hình để duyệt chính xác.
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
        // Chuyển POI sang trạng thái Rejected khi admin từ chối.
        var poi = await _context.Pois.FindAsync(id);
        if (poi == null) return NotFound("Không tìm thấy địa điểm.");

        // Lấy username người thực hiện (admin)
        var username = GetCurrentUsername();

        // Chuyển trạng thái sang Rejected
        poi.Status = PoiStatus.Rejected;

        // Gửi thông báo cho owner biết lý do bị từ chối.
        _context.OwnerNotifications.Add(new OwnerNotification
        {
            OwnerId = poi.OwnerId,
            PoiId = poi.Id,
            AdminUsername = username,
            Title = "POI bị từ chối",
            Message = $"{username} đã từ chối địa điểm '{poi.Name}' do thông tin địa chỉ không khớp.",
            CreatedAt = DateTime.Now,
            IsRead = false
        });

        await _context.SaveChangesAsync();

        // Ghi activity log cho thao tác từ chối POI.
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
        // Đếm số POI đang chờ duyệt để phục vụ dashboard/thống kê.
        var count = await _context.Pois.CountAsync(p => p.Status == PoiStatus.Pending);
        return Ok(count);
    }
}