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
        public DbSet<Tour> Tours { get; set; }
        public DbSet<TourDetail> TourDetails { get; set; }
        public DbSet<PoiTranslation> PoiTranslations { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }
        public DbSet<PoiListenLog> PoiListenLogs { get; set; }
        public DbSet<OwnerNotification> OwnerNotifications { get; set; }
        public DbSet<AdminNotification> AdminNotifications { get; set; }
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

            // --- CẤU HÌNH TOUR DETAIL (QUAN HỆ N-N) ---

            // 1. Tour xóa -> Chi tiết xóa theo (Cascade)
            modelBuilder.Entity<TourDetail>()
                .HasOne(td => td.Tour)
                .WithMany(t => t.TourDetails)
                .HasForeignKey(td => td.TourId)
                .OnDelete(DeleteBehavior.Cascade);

            // 2. Poi xóa -> Không cho xóa nếu đang nằm trong Tour (Restrict) 
            // Hoặc xóa luôn chi tiết tour (Cascade) -> Tùy nghiệp vụ. 
            // Ở đây mình chọn Restrict để an toàn dữ liệu.
            modelBuilder.Entity<TourDetail>()
                .HasOne(td => td.Poi)
                .WithMany() // Poi entity không cần chứa list TourDetail ngược lại
                .HasForeignKey(td => td.PoiId)
                .OnDelete(DeleteBehavior.Restrict);


            // --- Cấu hình Poi Listen Log ---
            modelBuilder.Entity<PoiListenLog>(entity => {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.PoiId);
                entity.HasIndex(e => e.Timestamp);
            });

            // --- Cấu hình Owner Notification ---
            modelBuilder.Entity<OwnerNotification>(entity =>
            {
                entity.ToTable("OwnerNotifications");

                entity.HasIndex(e => e.OwnerId);
                entity.HasIndex(e => new { e.OwnerId, e.IsRead, e.CreatedAt });

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(e => e.OwnerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Poi>()
                    .WithMany()
                    .HasForeignKey(e => e.PoiId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // --- Cấu hình Admin Notification ---
            modelBuilder.Entity<AdminNotification>(entity =>
            {
                entity.ToTable("AdminNotifications");

                entity.HasIndex(e => e.AdminId);
                entity.HasIndex(e => new { e.AdminId, e.IsRead, e.CreatedAt });

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(e => e.AdminId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Poi>()
                    .WithMany()
                    .HasForeignKey(e => e.PoiId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}