using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data.Entities; // Nhớ using namespace chứa các class Entity ở câu trả lời trước

namespace SmartTourGuide.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // Khai báo các bảng
        public DbSet<User> Users { get; set; }
        public DbSet<Poi> Pois { get; set; }
        public DbSet<GeofenceSetting> GeofenceSettings { get; set; }
        public DbSet<MediaAsset> MediaAssets { get; set; }
        public DbSet<UserLocationLog> UserLocationLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- Cấu hình User ---
            // Đặt tên bảng là Users (vì User đôi khi trùng từ khóa hệ thống)
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username).IsUnique(); // Username không được trùng

            // --- Cấu hình quan hệ User (Chủ gian hàng) -> POI ---
            modelBuilder.Entity<Poi>()
                .HasOne(p => p.Owner)
                .WithMany(u => u.OwnedPois)
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.Restrict); // Xóa User thì KHÔNG xóa POI ngay (để an toàn dữ liệu)

            // --- Cấu hình Geofence Setting (1-1 với POI) ---
            modelBuilder.Entity<Poi>()
                .HasOne(p => p.GeofenceSetting)
                .WithOne(gp => gp.Poi)
                .HasForeignKey<GeofenceSetting>(gp => gp.PoiId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa POI thì xóa luôn Setting

            // --- Cấu hình Media Asset (1-nhiều với POI) ---
            modelBuilder.Entity<MediaAsset>()
                .HasOne(m => m.Poi)
                .WithMany(p => p.MediaAssets)
                .HasForeignKey(m => m.PoiId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa POI thì xóa luôn ảnh/audio

            // --- Cấu hình User Location Log ---
            modelBuilder.Entity<UserLocationLog>(entity =>
            {
                // Chỉ định khóa chính
                entity.HasKey(e => e.Id);

                // Tạo Index cho UserId để tìm lịch sử của 1 người cho nhanh
                entity.HasIndex(e => e.UserId);

                // Tạo Index cho Timestamp để lọc theo ngày tháng nhanh hơn
                entity.HasIndex(e => e.Timestamp);

                // Thiết lập khóa ngoại (nếu xóa User thì xóa luôn lịch sử đi lại cho sạch DB)
                entity.HasOne<User>()
                      .WithMany() // User không cần chứa list Log (vì quá nhiều)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}