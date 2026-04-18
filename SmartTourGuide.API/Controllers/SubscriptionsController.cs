using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using SmartTourGuide.Shared.DTOs;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SmartTourGuide.API.Controllers;

/// <summary>
/// Quản lý toàn bộ nghiệp vụ Subscription của BoothOwner.
///
/// Phân quyền:
///   Admin   → CRUD SubscriptionPlan, xem tất cả subscription, duyệt/từ chối
///   Owner   → Xem gói, đăng ký mới, xem subscription của mình
///   (Không cần Tourist truy cập)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SubscriptionsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly SubscriptionService _subscriptionService;
    private const string FreePlanName = "Miễn phí";
    private const string ProPlanName = "Pro";
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromHours(24);
    private static readonly SubscriptionStatus[] PaymentRefUniqueStatuses =
    {
        SubscriptionStatus.PendingPayment,
        SubscriptionStatus.Active
    };

    public SubscriptionsController(AppDbContext context, SubscriptionService subscriptionService)
    {
        _context = context;
        _subscriptionService = subscriptionService;
    }

    // Helper lấy username hiện tại
    private string GetCurrentUsername()
    {
        var name = User.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;
        var headerName = HttpContext.Request.Headers["X-User-Name"].FirstOrDefault();
        return string.IsNullOrEmpty(headerName) ? "Unknown" : headerName;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SUBSCRIPTION PLANS — Admin quản lý, Owner/public xem
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [Public/Owner/Admin] Lấy danh sách tất cả gói đăng ký đang Active.
    /// Owner dùng endpoint này để chọn gói khi đăng ký.
    /// </summary>
    // GET api/subscriptions/plans
    [HttpGet("plans")]
    public async Task<ActionResult<IEnumerable<SubscriptionPlanDto>>> GetPlans()
    {
        var plans = await _context.SubscriptionPlans
            .Where(p => p.IsActive && p.Name == ProPlanName)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(1)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                PriceMonthly = p.PriceMonthly,
                PriceYearly = p.PriceYearly,
                MaxActivePois = 1,
                IsActive = p.IsActive
            })
            .ToListAsync();

        return Ok(plans);
    }

    /// <summary>
    /// [Admin] Lấy toàn bộ gói kể cả đã ẩn.
    /// </summary>
    // GET api/subscriptions/plans/all
    [HttpGet("plans/all")]
    public async Task<ActionResult<IEnumerable<SubscriptionPlanDto>>> GetAllPlans()
    {
        var plans = await _context.SubscriptionPlans
            .Where(p => p.Name == ProPlanName)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(1)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                PriceMonthly = p.PriceMonthly,
                PriceYearly = p.PriceYearly,
                MaxActivePois = 1,
                IsActive = p.IsActive
            })
            .ToListAsync();

        return Ok(plans);
    }

    /// <summary>
    /// [Admin] Tạo gói đăng ký mới.
    /// </summary>
    // POST api/subscriptions/plans
    [HttpPost("plans")]
    public async Task<ActionResult<SubscriptionPlanDto>> CreatePlan([FromBody] UpsertSubscriptionPlanDto dto)
    {
        var normalizedName = NormalizePlanName(dto.Name);
        if (normalizedName != "pro")
            return BadRequest(new { message = "Admin chỉ được tạo gói Pro. Gói Miễn phí là gói mặc định hệ thống." });

        if (dto.PriceMonthly < 0 || dto.PriceYearly < 0)
            return BadRequest(new { message = "Giá gói không được âm." });

        if (dto.PriceMonthly <= 0 || dto.PriceYearly <= 0)
        {
            return BadRequest(new { message = "Gói Pro yêu cầu giá theo tháng và theo năm lớn hơn 0." });
        }

        var existingPro = await _context.SubscriptionPlans
            .Where(p => p.Name == ProPlanName)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync();

        if (existingPro is not null)
        {
            existingPro.Description = dto.Description;
            existingPro.PriceMonthly = dto.PriceMonthly;
            existingPro.PriceYearly = dto.PriceYearly;
            existingPro.MaxActivePois = 1;
            existingPro.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();

            return Ok(new SubscriptionPlanDto
            {
                Id = existingPro.Id,
                Name = existingPro.Name,
                Description = existingPro.Description,
                PriceMonthly = existingPro.PriceMonthly,
                PriceYearly = existingPro.PriceYearly,
                MaxActivePois = 1,
                IsActive = existingPro.IsActive
            });
        }

        var plan = new SubscriptionPlan
        {
            Name = ProPlanName,
            Description = dto.Description,
            PriceMonthly = dto.PriceMonthly,
            PriceYearly = dto.PriceYearly,
            MaxActivePois = 1,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _context.SubscriptionPlans.Add(plan);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPlans), new { id = plan.Id }, new SubscriptionPlanDto
        {
            Id = plan.Id,
            Name = plan.Name,
            Description = plan.Description,
            PriceMonthly = plan.PriceMonthly,
            PriceYearly = plan.PriceYearly,
            MaxActivePois = 1,
            IsActive = plan.IsActive
        });
    }

    /// <summary>
    /// [Admin] Cập nhật gói đăng ký.
    /// </summary>
    // PUT api/subscriptions/plans/{id}
    [HttpPut("plans/{id:int}")]
    public async Task<IActionResult> UpdatePlan(int id, [FromBody] UpsertSubscriptionPlanDto dto)
    {
        var plan = await _context.SubscriptionPlans.FindAsync(id);
        if (plan is null) return NotFound(new { message = "Gói đăng ký không tồn tại." });

        if (IsFreePlan(plan))
            return BadRequest(new { message = "Gói Miễn phí là mặc định hệ thống, không thể chỉnh sửa." });

        var normalizedName = NormalizePlanName(dto.Name);
        if (normalizedName != "pro")
            return BadRequest(new { message = "Chỉ có thể cập nhật gói Pro." });

        if (dto.PriceMonthly < 0 || dto.PriceYearly < 0)
            return BadRequest(new { message = "Giá gói không được âm." });

        if (dto.PriceMonthly <= 0 || dto.PriceYearly <= 0)
        {
            return BadRequest(new { message = "Gói Pro yêu cầu giá theo tháng và theo năm lớn hơn 0." });
        }

        plan.Name = ProPlanName;
        plan.Description = dto.Description;
        plan.PriceMonthly = dto.PriceMonthly;
        plan.PriceYearly = dto.PriceYearly;
        plan.MaxActivePois = 1;
        plan.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// [Admin] Ẩn gói (soft-delete — không xóa hẳn để bảo toàn lịch sử).
    /// </summary>
    // DELETE api/subscriptions/plans/{id}
    [HttpDelete("plans/{id:int}")]
    public async Task<IActionResult> DeactivatePlan(int id)
    {
        var plan = await _context.SubscriptionPlans.FindAsync(id);
        if (plan is null) return NotFound(new { message = "Gói đăng ký không tồn tại." });

        if (IsFreePlan(plan))
            return BadRequest(new { message = "Gói Miễn phí là mặc định hệ thống, không thể ẩn." });

        plan.IsActive = false;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SUBSCRIPTIONS — Owner đăng ký, Admin duyệt
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [Admin] Xem toàn bộ subscription của hệ thống (có thể lọc theo status).
    /// </summary>
    // GET api/subscriptions?status=Active
    [HttpGet]
    public async Task<ActionResult<IEnumerable<BoothOwnerSubscriptionDto>>> GetAll(
        [FromQuery] string? status = null)
    {
        await SyncSubscriptionStatesAsync();

        var query = _context.BoothOwnerSubscriptions
            .Include(s => s.Owner)
            .Include(s => s.Plan)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<SubscriptionStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(s => s.Status == parsedStatus);
        }

        var now = DateTime.UtcNow;
        var subscriptions = await query
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var result = subscriptions
            .Select(s => MapToDto(s, now))
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// [Admin] Xem subscription của một owner cụ thể.
    /// </summary>
    // GET api/subscriptions/owner/{ownerId}
    [HttpGet("owner/{ownerId:int}")]
    public async Task<ActionResult<IEnumerable<BoothOwnerSubscriptionDto>>> GetByOwner(int ownerId)
    {
        await SyncSubscriptionStatesAsync(ownerId);

        var now = DateTime.UtcNow;
        var subscriptions = await _context.BoothOwnerSubscriptions
            .Include(s => s.Owner)
            .Include(s => s.Plan)
            .Where(s => s.OwnerId == ownerId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var result = subscriptions
            .Select(s => MapToDto(s, now))
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// [Owner] Xem subscription summary của chính mình (dùng trên dashboard owner).
    /// </summary>
    // GET api/subscriptions/my-summary/{ownerId}
    [HttpGet("my-summary/{ownerId:int}")]
    public async Task<ActionResult<OwnerSubscriptionSummaryDto>> GetMySummary(int ownerId)
    {
        await SyncSubscriptionStatesAsync(ownerId);

        var now = DateTime.UtcNow;

        // Lấy subscription active
        var active = await _context.BoothOwnerSubscriptions
            .Include(s => s.Plan)
            .Where(s => s.OwnerId == ownerId
                     && s.Status == SubscriptionStatus.Active
                     && s.EndDate > now)
            .OrderByDescending(s => s.EndDate)
            .FirstOrDefaultAsync();

        // Đếm POI đang Active của owner
        var activePoisCount = await _context.Pois
            .CountAsync(p => p.OwnerId == ownerId && p.Status == PoiStatus.Active);

        if (active is null)
        {
            // Kiểm tra có subscription đang PendingPayment không
            var pending = await _context.BoothOwnerSubscriptions
                .AnyAsync(s => s.OwnerId == ownerId && s.Status == SubscriptionStatus.PendingPayment);

            if (pending)
            {
                return Ok(new OwnerSubscriptionSummaryDto
                {
                    HasActiveSubscription = false,
                    Status = "PendingPayment",
                    CurrentActivePois = activePoisCount
                });
            }

            var latestNonPending = await _context.BoothOwnerSubscriptions
                .Include(s => s.Plan)
                .Where(s => s.OwnerId == ownerId && s.Status != SubscriptionStatus.PendingPayment)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();

            if (latestNonPending is not null)
            {
                var latestIsFree = IsFreePlan(latestNonPending.Plan);
                return Ok(new OwnerSubscriptionSummaryDto
                {
                    HasActiveSubscription = false,
                    PlanName = latestNonPending.Plan?.Name,
                    EndDate = latestIsFree ? null : latestNonPending.EndDate,
                    DaysRemaining = latestIsFree ? -1 : 0,
                    MaxActivePois = latestNonPending.Plan?.MaxActivePois ?? 0,
                    CurrentActivePois = activePoisCount,
                    Status = latestNonPending.Status.ToString()
                });
            }

            return Ok(new OwnerSubscriptionSummaryDto
            {
                HasActiveSubscription = false,
                Status = "None",
                CurrentActivePois = activePoisCount
            });
        }

        return Ok(new OwnerSubscriptionSummaryDto
        {
            HasActiveSubscription = true,
            PlanName = active.Plan.Name,
            EndDate = IsFreePlan(active.Plan) ? null : active.EndDate,
            DaysRemaining = IsFreePlan(active.Plan) ? -1 : Math.Max(0, (int)(active.EndDate - now).TotalDays),
            MaxActivePois = active.Plan.MaxActivePois,
            CurrentActivePois = activePoisCount,
            Status = "Active"
        });
    }

    /// <summary>
    /// [Owner] Đăng ký gói subscription mới.
    ///
    /// Workflow:
    ///   1. Owner chọn gói + chu kỳ thanh toán
    ///   2. Server tạo subscription với Status = PendingPayment
    ///   3. Admin xem danh sách pending → xác nhận → Status = Active
    ///   (Nếu tích hợp cổng thanh toán tự động thì bước 3 tự động qua webhook)
    /// </summary>
    // POST api/subscriptions/subscribe/{ownerId}
    [HttpPost("subscribe/{ownerId:int}")]
    public async Task<ActionResult<BoothOwnerSubscriptionDto>> Subscribe(
        int ownerId,
        [FromBody] CreateSubscriptionDto dto)
    {
        await SyncSubscriptionStatesAsync(ownerId);

        // Kiểm tra owner tồn tại và đúng role
        var owner = await _context.Users.FindAsync(ownerId);
        if (owner is null) return NotFound(new { message = "Không tìm thấy tài khoản." });

        // Kiểm tra gói hợp lệ
        var plan = await _context.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Id == dto.PlanId && p.IsActive);
        if (plan is null)
            return BadRequest(new { message = "Gói đăng ký không tồn tại hoặc đã bị ngừng." });

        var isFreePlan = IsFreePlan(plan);

        // Không cho đăng ký nếu đang có subscription Active
        var activeSubscription = await _context.BoothOwnerSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OwnerId == ownerId
                                   && s.Status == SubscriptionStatus.Active
                                   && s.EndDate > DateTime.UtcNow);

        if (activeSubscription is not null)
        {
            var isActivePlanFree = IsFreePlan(activeSubscription.Plan);
            var isUpgradingFromFreeToPro = isActivePlanFree && !isFreePlan;
            if (!isUpgradingFromFreeToPro)
            {
                return Conflict(new
                {
                    message = "Bạn đang có gói đăng ký còn hiệu lực. Vui lòng chờ hết hạn hoặc liên hệ Admin để gia hạn sớm."
                });
            }
        }

        // Cũng không cho đăng ký nếu còn đơn PendingPayment chưa được xử lý
        var hasPending = await _context.BoothOwnerSubscriptions
            .AnyAsync(s => s.OwnerId == ownerId && s.Status == SubscriptionStatus.PendingPayment);
        if (hasPending)
            return Conflict(new { message = "Bạn đã có yêu cầu đăng ký đang chờ xác nhận. Vui lòng đợi Admin xử lý." });

        var billingCycle = isFreePlan ? BillingCycle.Monthly : (BillingCycle)dto.BillingCycle;
        var startDate = DateTime.UtcNow;
        var endDate = isFreePlan
            ? SubscriptionService.GetNonExpiringEndDateUtc()
            : SubscriptionService.CalculateEndDate(startDate, billingCycle);
        var amount = isFreePlan ? 0 : SubscriptionService.CalculateAmount(plan, billingCycle);

        var normalizedPaymentReference = isFreePlan
            ? null
            : await GenerateUniquePaymentReferenceAsync();

        var subscription = new BoothOwnerSubscription
        {
            OwnerId = ownerId,
            PlanId = plan.Id,
            BillingCycle = billingCycle,
            StartDate = startDate,
            EndDate = endDate,
            AmountPaid = amount,
            PaymentReference = normalizedPaymentReference,
            PaymentMethod = isFreePlan ? "Free-Plan" : dto.PaymentMethod,
            Status = SubscriptionStatus.PendingPayment,
            PlanNameSnapshot = plan.Name,
            CreatedAt = DateTime.UtcNow
        };

        _context.BoothOwnerSubscriptions.Add(subscription);
        await _context.SaveChangesAsync();

        // Tạo notification cho toàn bộ Admin
        var adminIds = await _context.Users
            .Where(u => u.Role == SmartTourGuide.Shared.Enums.UserRole.Admin)
            .Select(u => u.Id)
            .ToListAsync();

        foreach (var adminId in adminIds)
        {
            _context.AdminNotifications.Add(new AdminNotification
            {
                AdminId = adminId,
                OwnerUsername = owner.FullName,
                Title = "Yêu cầu đăng ký gói mới",
                Message = $"BoothOwner '{owner.FullName}' vừa đăng ký gói {plan.Name} ({billingCycle}). Vui lòng xác nhận thanh toán.",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        var now = DateTime.UtcNow;
        // Reload để lấy navigation properties
        await _context.Entry(subscription).Reference(s => s.Owner).LoadAsync();
        await _context.Entry(subscription).Reference(s => s.Plan).LoadAsync();

        return CreatedAtAction(nameof(GetMySummary), new { ownerId }, MapToDto(subscription, now));
    }

    /// <summary>
    /// [Admin] Xem danh sách subscription đang chờ duyệt (PendingPayment).
    /// </summary>
    // GET api/subscriptions/pending
    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<BoothOwnerSubscriptionDto>>> GetPending()
    {
        await SyncSubscriptionStatesAsync();

        var now = DateTime.UtcNow;
        var subscriptions = await _context.BoothOwnerSubscriptions
            .Include(s => s.Owner)
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.PendingPayment)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        var result = subscriptions
            .Select(s => MapToDto(s, now))
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// [Admin] Duyệt (approve), từ chối (reject), hoặc hủy (cancel) một subscription.
    ///
    /// - approve: Status → Active, ghi nhận StartDate chính xác
    /// - reject:  Status → Cancelled, gửi notification cho owner
    /// - cancel:  Status → Cancelled (hủy giữa chừng), gửi notification cho owner
    /// </summary>
    // PUT api/subscriptions/{id}/review
    [HttpPut("{id:int}/review")]
    public async Task<IActionResult> ReviewSubscription(int id, [FromBody] ReviewSubscriptionDto dto)
    {
        var sub = await _context.BoothOwnerSubscriptions
            .Include(s => s.Owner)
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sub is null) return NotFound(new { message = "Subscription không tồn tại." });
        if (string.IsNullOrWhiteSpace(dto.Action))
            return BadRequest(new { message = "Action là bắt buộc. Sử dụng: approve | reject | cancel" });

        var adminUsername = GetCurrentUsername();

        switch (dto.Action.Trim().ToLowerInvariant())
        {
            case "approve":
                if (sub.Status != SubscriptionStatus.PendingPayment)
                    return BadRequest(new { message = "Chỉ có thể duyệt subscription đang ở trạng thái PendingPayment." });

                sub.Status = SubscriptionStatus.Active;
                // Tính lại StartDate/EndDate chính xác từ thời điểm Admin duyệt
                sub.StartDate = DateTime.UtcNow;
                var approvedFreePlan = IsFreePlan(sub.Plan);
                sub.EndDate = approvedFreePlan
                    ? SubscriptionService.GetNonExpiringEndDateUtc()
                    : SubscriptionService.CalculateEndDate(sub.StartDate, sub.BillingCycle);

                // Chỉ giữ 1 gói Active tại cùng thời điểm.
                var previousActive = await _context.BoothOwnerSubscriptions
                    .Where(s => s.OwnerId == sub.OwnerId
                             && s.Id != sub.Id
                             && s.Status == SubscriptionStatus.Active)
                    .ToListAsync();

                foreach (var item in previousActive)
                {
                    item.Status = SubscriptionStatus.Cancelled;
                    item.CancelReason = "Được thay thế bởi gói mới đã duyệt.";
                }

                // Notification cho Owner
                _context.OwnerNotifications.Add(new OwnerNotification
                {
                    OwnerId = sub.OwnerId,
                    Title = "Đăng ký gói thành công!",
                    Message = approvedFreePlan
                        ? $"Gói {sub.Plan.Name} của bạn đã được kích hoạt và không hết hạn. POI của bạn sẽ được hiển thị trên ứng dụng."
                        : $"Gói {sub.Plan.Name} của bạn đã được kích hoạt. Hiệu lực đến {sub.EndDate:dd/MM/yyyy}. POI của bạn sẽ được hiển thị trên ứng dụng.",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                break;

            case "reject":
                if (sub.Status != SubscriptionStatus.PendingPayment)
                    return BadRequest(new { message = "Chỉ có thể từ chối subscription đang ở trạng thái PendingPayment." });

                sub.Status = SubscriptionStatus.Cancelled;
                sub.CancelReason = dto.Reason ?? "Thanh toán không hợp lệ";

                _context.OwnerNotifications.Add(new OwnerNotification
                {
                    OwnerId = sub.OwnerId,
                    Title = "Yêu cầu đăng ký bị từ chối",
                    Message = $"Yêu cầu đăng ký gói {sub.Plan.Name} đã bị từ chối. Lý do: {sub.CancelReason}. Vui lòng liên hệ Admin để biết thêm chi tiết.",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                break;

            case "cancel":
                if (sub.Status != SubscriptionStatus.Active)
                    return BadRequest(new { message = "Chỉ có thể hủy subscription đang Active." });

                sub.Status = SubscriptionStatus.Cancelled;
                sub.CancelReason = dto.Reason ?? $"Admin {adminUsername} hủy thủ công";

                _context.OwnerNotifications.Add(new OwnerNotification
                {
                    OwnerId = sub.OwnerId,
                    Title = "Gói đăng ký đã bị hủy",
                    Message = $"Gói {sub.Plan.Name} của bạn đã bị hủy bởi Admin. Lý do: {sub.CancelReason}. POI của bạn sẽ bị ẩn khỏi ứng dụng.",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                break;

            default:
                return BadRequest(new { message = "Action không hợp lệ. Sử dụng: approve | reject | cancel" });
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// [Owner] Hủy yêu cầu đang chờ xác nhận của chính mình.
    /// </summary>
    // PUT api/subscriptions/{id}/owner-cancel?ownerId=123
    [HttpPut("{id:int}/owner-cancel")]
    public async Task<IActionResult> OwnerCancelPending(int id, [FromQuery] int ownerId)
    {
        await SyncSubscriptionStatesAsync(ownerId);

        if (ownerId <= 0)
            return BadRequest(new { message = "ownerId không hợp lệ." });

        var sub = await _context.BoothOwnerSubscriptions
            .Include(s => s.Owner)
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sub is null)
            return NotFound(new { message = "Subscription không tồn tại." });

        if (sub.OwnerId != ownerId)
            return BadRequest(new { message = "Bạn không có quyền hủy yêu cầu này." });

        if (sub.Status != SubscriptionStatus.PendingPayment)
            return BadRequest(new { message = "Chỉ có thể hủy yêu cầu đang chờ xác nhận." });

        sub.Status = SubscriptionStatus.Cancelled;
        sub.CancelReason = "Owner đã hủy yêu cầu trước khi Admin duyệt.";

        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// [Admin] Trigger thủ công job expire subscription hết hạn.
    /// Trong production nên dùng background job (Hangfire / IHostedService).
    /// </summary>
    // POST api/subscriptions/expire-check
    [HttpPost("expire-check")]
    public async Task<IActionResult> ExpireCheck()
    {
        var count = await _subscriptionService.ExpireOverdueSubscriptionsAsync();
        return Ok(new { message = $"Đã đánh dấu Expired {count} subscription hết hạn." });
    }

    private static bool IsFreePlan(SubscriptionPlan? plan)
    {
        if (plan is null) return false;

        var normalizedName = NormalizePlanName(plan.Name);
        return normalizedName == "mienphi" || (plan.PriceMonthly <= 0 && plan.PriceYearly <= 0);
    }

    private static string NormalizePlanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var decomposed = raw.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace(" ", string.Empty);
    }

    private async Task SyncSubscriptionStatesAsync(int? ownerId = null)
    {
        await _subscriptionService.ExpireOverdueSubscriptionsAsync();
        await AutoCancelExpiredPendingAsync(ownerId);
    }

    private async Task<bool> HasDuplicatePaymentReferenceAsync(string paymentReference)
    {
        return await _context.BoothOwnerSubscriptions
            .AnyAsync(s => s.PaymentReference == paymentReference && PaymentRefUniqueStatuses.Contains(s.Status));
    }

    private async Task<string> GenerateUniquePaymentReferenceAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = GenerateRandomPaymentReference();
            if (!await HasDuplicatePaymentReferenceAsync(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Không thể tạo mã giao dịch duy nhất. Vui lòng thử lại.");
    }

    private static string GenerateRandomPaymentReference()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var randomSuffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
        return $"STG-{timestamp}-{randomSuffix}";
    }

    private async Task<int> AutoCancelExpiredPendingAsync(int? ownerId = null)
    {
        var cutoff = DateTime.UtcNow.Subtract(PendingTimeout);

        var query = _context.BoothOwnerSubscriptions
            .Where(s => s.Status == SubscriptionStatus.PendingPayment && s.CreatedAt <= cutoff);

        if (ownerId.HasValue)
        {
            query = query.Where(s => s.OwnerId == ownerId.Value);
        }

        var expiredPending = await query.ToListAsync();
        if (expiredPending.Count == 0)
        {
            return 0;
        }

        foreach (var item in expiredPending)
        {
            item.Status = SubscriptionStatus.Cancelled;
            item.CancelReason = "Yêu cầu tự hết hạn sau 24 giờ chưa được duyệt.";
        }

        await _context.SaveChangesAsync();
        return expiredPending.Count;
    }

    // ─── Mapper helper ───────────────────────────────────────────────────

    private static BoothOwnerSubscriptionDto MapToDto(BoothOwnerSubscription s, DateTime now)
        => new()
        {
            Id = s.Id,
            OwnerId = s.OwnerId,
            OwnerName = s.Owner?.FullName ?? string.Empty,
            PlanId = s.PlanId,
            PlanName = s.Plan?.Name ?? s.PlanNameSnapshot,
            BillingCycle = s.BillingCycle.ToString(),
            AmountPaid = s.AmountPaid,
            StartDate = s.StartDate,
            EndDate = s.EndDate,
            Status = s.Status.ToString(),
            PaymentMethod = s.PaymentMethod,
            PaymentReference = s.PaymentReference,
            CancelReason = s.CancelReason,
            CreatedAt = s.CreatedAt,
            DaysRemaining = s.Status != SubscriptionStatus.Active
                ? 0
                : (IsFreePlan(s.Plan) ? -1 : Math.Max(0, (int)(s.EndDate - now).TotalDays))
        };
}
