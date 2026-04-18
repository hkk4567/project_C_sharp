namespace SmartTourGuide.Shared.DTOs
{
    // ═══════════════════════════════════════════════════════════════
    //  SUBSCRIPTION PLAN DTOs
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Trả về cho client xem danh sách gói</summary>
    public class SubscriptionPlanDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal PriceMonthly { get; set; }
        public decimal PriceYearly { get; set; }
        public int MaxActivePois { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Admin tạo / cập nhật gói đăng ký</summary>
    public class UpsertSubscriptionPlanDto
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        public decimal PriceMonthly { get; set; }
        public decimal PriceYearly { get; set; }
        public int MaxActivePois { get; set; } = 1;
        public bool IsActive { get; set; } = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  BOOTH OWNER SUBSCRIPTION DTOs
    // ═══════════════════════════════════════════════════════════════

    /// <summary>BoothOwner gửi lên khi muốn đăng ký gói</summary>
    public class CreateSubscriptionDto
    {
        /// <summary>ID gói đăng ký (lấy từ GET /api/subscriptions/plans)</summary>
        public int PlanId { get; set; }

        /// <summary>0 = Monthly, 1 = Yearly</summary>
        public int BillingCycle { get; set; }

        /// <summary>Mã giao dịch từ cổng thanh toán (nếu đã thanh toán phía client)</summary>
        public string? PaymentReference { get; set; }

        /// <summary>"VNPay" | "MoMo" | "Manual"</summary>
        public string PaymentMethod { get; set; } = "Manual";
    }

    /// <summary>Trả về chi tiết một subscription</summary>
    public class BoothOwnerSubscriptionDto
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public int PlanId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public string BillingCycle { get; set; } = string.Empty; // "Monthly" | "Yearly"
        public decimal AmountPaid { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = string.Empty; // "Active" | "Expired" ...
        public string PaymentMethod { get; set; } = string.Empty;
        public string? PaymentReference { get; set; }
        public string? CancelReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public int DaysRemaining { get; set; } // Số ngày còn lại (tiện hiển thị)
    }

    /// <summary>Admin xác nhận / từ chối / hủy subscription</summary>
    public class ReviewSubscriptionDto
    {
        /// <summary>"approve" | "reject" | "cancel"</summary>
        public required string Action { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>Summary hiển thị trên dashboard của BoothOwner</summary>
    public class OwnerSubscriptionSummaryDto
    {
        public bool HasActiveSubscription { get; set; }
        public string? PlanName { get; set; }
        public DateTime? EndDate { get; set; }
        public int DaysRemaining { get; set; }
        public int MaxActivePois { get; set; }
        public int CurrentActivePois { get; set; }
        public string Status { get; set; } = "None";
    }
}
