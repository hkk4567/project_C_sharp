using System.ComponentModel.DataAnnotations;
using SmartTourGuide.Shared.Enums;
namespace SmartTourGuide.API.Data.Entities
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Username { get; set; }

        [Required]
        public required string PasswordHash { get; set; } // Mật khẩu đã mã hóa

        [Required]
        [MaxLength(100)]
        public required string FullName { get; set; }
        [Required]
        [EmailAddress]
        public required string Email { get; set; }

        // Phân quyền: 0 = Tourist, 1 = BoothOwner, 2 = Admin
        public UserRole Role { get; set; }

        // Quan hệ: Một chủ gian hàng có thể có nhiều địa điểm
        public virtual ICollection<Poi> OwnedPois { get; set; } = new List<Poi>();
    }
}