using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;

namespace SmartTourGuide.API.Services
{
    /// <summary>
    /// Service xử lý logic nghiệp vụ subscription.
    /// Được inject vào SubscriptionsController và PoisController.
    /// </summary>
    public class SubscriptionService
    {
        private readonly AppDbContext _context;

        public SubscriptionService(AppDbContext context)
        {
            _context = context;
        }

        // ─── Kiểm tra ────────────────────────────────────────────────────

        /// <summary>
        /// Kiểm tra BoothOwner có subscription đang Active và chưa hết hạn không.
        /// Dùng để quyết định POI của owner có được hiển thị trên app hay không.
        /// </summary>
        public async Task<bool> HasActiveSubscriptionAsync(int ownerId)
        {
            var now = DateTime.UtcNow;
            return await _context.BoothOwnerSubscriptions
                .AnyAsync(s => s.OwnerId == ownerId
                            && s.Status == SubscriptionStatus.Active
                            && s.EndDate > now);
        }

        /// <summary>
        /// Lấy subscription đang Active của owner (null nếu không có).
        /// </summary>
        public async Task<BoothOwnerSubscription?> GetActiveSubscriptionAsync(int ownerId)
        {
            var now = DateTime.UtcNow;
            return await _context.BoothOwnerSubscriptions
                .Include(s => s.Plan)
                .Where(s => s.OwnerId == ownerId
                         && s.Status == SubscriptionStatus.Active
                         && s.EndDate > now)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();
        }

        // ─── Tính phí ────────────────────────────────────────────────────

        /// <summary>
        /// Tính số tiền cần thanh toán dựa trên gói và chu kỳ.
        /// </summary>
        public static decimal CalculateAmount(SubscriptionPlan plan, BillingCycle cycle)
            => cycle == BillingCycle.Yearly ? plan.PriceYearly : plan.PriceMonthly;

        /// <summary>
        /// Tính EndDate từ StartDate theo chu kỳ thanh toán.
        /// </summary>
        public static DateTime CalculateEndDate(DateTime start, BillingCycle cycle)
            => cycle == BillingCycle.Yearly
                ? start.AddYears(1)
                : start.AddMonths(1);

        // ─── Auto-expire job ─────────────────────────────────────────────

        /// <summary>
        /// Đánh dấu Expired các subscription đã quá EndDate.
        /// Gọi từ background job (IHostedService hoặc Hangfire).
        /// </summary>
        public async Task<int> ExpireOverdueSubscriptionsAsync()
        {
            var now = DateTime.UtcNow;
            var overdue = await _context.BoothOwnerSubscriptions
                .Where(s => s.Status == SubscriptionStatus.Active && s.EndDate <= now)
                .ToListAsync();

            foreach (var sub in overdue)
                sub.Status = SubscriptionStatus.Expired;

            await _context.SaveChangesAsync();
            return overdue.Count; // Số bản ghi đã cập nhật
        }
    }
}