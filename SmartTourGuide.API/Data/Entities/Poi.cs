using System.ComponentModel.DataAnnotations;
namespace SmartTourGuide.API.Data.Entities
{
    public class Poi
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public required string Name { get; set; } // Tên gian hàng/địa điểm

        public required string Description { get; set; } // Mô tả hiển thị

        // --- Định vị (Slide 1 & 5) ---
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public required string Address { get; set; }

        // --- Quản lý trạng thái ---
        // Pending: Chờ Admin duyệt
        // Active: Đang hiển thị cho User
        // Rejected: Bị từ chối
        public PoiStatus Status { get; set; } = PoiStatus.Pending;

        // --- Khóa ngoại ---
        public int OwnerId { get; set; } // ID của Chủ gian hàng
        public virtual User Owner { get; set; } = null!;

        // Quan hệ
        public virtual GeofenceSetting GeofenceSetting { get; set; } = null!;
        public virtual ICollection<MediaAsset> MediaAssets { get; set; } = new List<MediaAsset>();
    }

    public enum PoiStatus { Pending, Active, Rejected, Hidden }
}