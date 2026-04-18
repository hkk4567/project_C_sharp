using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTourGuide.API.Data.Entities
{
    /// <summary>
    /// Bảng danh mục các gói đăng ký mà Admin định nghĩa sẵn.
    /// BoothOwner chọn một trong các gói này để kích hoạt subscription.
    /// </summary>
    public class SubscriptionPlan
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Tên gói: Basic, Pro, Enterprise</summary>
        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        /// <summary>Mô tả ngắn hiển thị cho BoothOwner</summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>Giá theo tháng (VNĐ)</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceMonthly { get; set; }

        /// <summary>Giá theo năm (VNĐ) — thường thấp hơn 12x tháng để khuyến khích</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceYearly { get; set; }

        /// <summary>
        /// Số POI tối đa được hiển thị đồng thời với gói này.
        /// -1 = unlimited
        /// </summary>
        public int MaxActivePois { get; set; } = 1;

        /// <summary>Admin có thể ẩn/xóa gói mà không ảnh hưởng subscription đang chạy</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public virtual ICollection<BoothOwnerSubscription> Subscriptions { get; set; } = new List<BoothOwnerSubscription>();
    }
}
