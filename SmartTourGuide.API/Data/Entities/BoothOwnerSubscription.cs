using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTourGuide.API.Data.Entities
{
    /// <summary>
    /// Bảng lưu đăng ký subscription thực tế của từng BoothOwner.
    /// Mỗi owner có thể có nhiều subscription (lịch sử), nhưng chỉ 1 Active tại một thời điểm.
    /// </summary>
    public class BoothOwnerSubscription
    {
        [Key]
        public int Id { get; set; }

        // ─── Khóa ngoại ─────────────────────────────────────────────────
        /// <summary>Chủ gian hàng đăng ký</summary>
        public int OwnerId { get; set; }
        public virtual User Owner { get; set; } = null!;

        /// <summary>Gói được chọn (Basic / Pro / Enterprise)</summary>
        public int PlanId { get; set; }
        public virtual SubscriptionPlan Plan { get; set; } = null!;

        // ─── Chu kỳ & thời hạn ──────────────────────────────────────────
        /// <summary>Chu kỳ thanh toán: Monthly = 0, Yearly = 1</summary>
        public BillingCycle BillingCycle { get; set; }

        /// <summary>Ngày bắt đầu subscription có hiệu lực</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Ngày hết hạn (StartDate + 1 tháng hoặc + 1 năm)</summary>
        public DateTime EndDate { get; set; }

        // ─── Trạng thái ─────────────────────────────────────────────────
        /// <summary>
        /// PendingPayment: chờ xác nhận thanh toán
        /// Active:         đang hiệu lực, POI được hiển thị
        /// Expired:        hết hạn, POI tự ẩn
        /// Cancelled:      chủ hàng hoặc Admin hủy trước hạn
        /// </summary>
        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.PendingPayment;

        // ─── Thanh toán ─────────────────────────────────────────────────
        /// <summary>Số tiền thực tế thu (theo gói + chu kỳ tại thời điểm đăng ký)</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal AmountPaid { get; set; }

        /// <summary>Mã giao dịch từ cổng thanh toán (VNPay, MoMo…) hoặc Admin xác nhận thủ công</summary>
        [MaxLength(200)]
        public string? PaymentReference { get; set; }

        /// <summary>Phương thức: "VNPay", "MoMo", "Manual" (Admin xác nhận tay)</summary>
        [MaxLength(50)]
        public string PaymentMethod { get; set; } = "Manual";

        // ─── Metadata ───────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Lý do nếu Admin hủy hoặc từ chối</summary>
        [MaxLength(500)]
        public string? CancelReason { get; set; }

        /// <summary>Snapshot tên gói tại thời điểm đăng ký (đề phòng Admin đổi tên gói sau)</summary>
        [MaxLength(100)]
        public string PlanNameSnapshot { get; set; } = string.Empty;
    }

    public enum BillingCycle
    {
        Monthly = 0,
        Yearly = 1
    }

    public enum SubscriptionStatus
    {
        PendingPayment = 0, // Chờ thanh toán / Admin duyệt
        Active = 1, // Đang hiệu lực
        Expired = 2, // Hết hạn
        Cancelled = 3  // Đã hủy
    }
}
