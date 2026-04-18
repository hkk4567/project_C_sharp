using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data.Entities; // Chứa các entity map với bảng DB
// Truy cập dữ liệu (SELECT, INSERT, UPDATE, DELETE)
// Định nghĩa các bảng trong DB bằng code
// Cấu hình quan hệ giữa các bảng
// Thiết lập ràng buộc (constraint), index, behavior khi xóa
namespace SmartTourGuide.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // DbSet đại diện cho các bảng chính trong hệ thống
        public DbSet<User> Users { get; set; }
        public DbSet<Poi> Pois { get; set; }
        public DbSet<GeofenceSetting> GeofenceSettings { get; set; }
        public DbSet<MediaAsset> MediaAssets { get; set; }
        public DbSet<UserLocationLog> UserLocationLogs { get; set; }
        public DbSet<Tour> Tours { get; set; }
        public DbSet<TourDetail> TourDetails { get; set; }
        public DbSet<PoiTranslation> PoiTranslations { get; set; }
        public DbSet<TourTranslation> TourTranslations { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }
        public DbSet<PoiListenLog> PoiListenLogs { get; set; }
        public DbSet<OwnerNotification> OwnerNotifications { get; set; }
        public DbSet<AdminNotification> AdminNotifications { get; set; }
        public DbSet<QrScanLog> QrScanLogs => Set<QrScanLog>();
        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<BoothOwnerSubscription> BoothOwnerSubscriptions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User: đặt tên bảng tường minh và ép Username duy nhất
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username).IsUnique(); // Tránh trùng tài khoản đăng nhập

            // Poi -> Owner (User): 1 owner có nhiều POI, không xóa dây chuyền POI khi xóa owner
            modelBuilder.Entity<Poi>()
                .HasOne(p => p.Owner)
                .WithMany(u => u.OwnedPois)
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.Restrict); // Bảo toàn dữ liệu POI

            // Poi <-> GeofenceSetting (1-1): geofence phụ thuộc POI
            modelBuilder.Entity<Poi>()
                .HasOne(p => p.GeofenceSetting)
                .WithOne(gp => gp.Poi)
                .HasForeignKey<GeofenceSetting>(gp => gp.PoiId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa POI thì xóa geofence

            // MediaAsset -> Poi (1-n): media phụ thuộc POI
            modelBuilder.Entity<MediaAsset>()
                .HasOne(m => m.Poi)
                .WithMany(p => p.MediaAssets)
                .HasForeignKey(m => m.PoiId)
                .OnDelete(DeleteBehavior.Cascade); // Dọn dữ liệu con khi xóa POI

            // UserLocationLog: lưu lịch sử vị trí, hỗ trợ cả user đăng nhập và khách
            modelBuilder.Entity<UserLocationLog>(entity =>
                {
                    // Khóa chính
                    entity.HasKey(e => e.Id);

                    // Index để tối ưu truy vấn theo user và thời gian
                    entity.HasIndex(e => e.UserId);
                    entity.HasIndex(e => e.Timestamp);

                    // Map navigation trực tiếp để dùng đúng FK UserId, tránh phát sinh cột thừa
                    entity.HasOne(e => e.User)
                        .WithMany()
                        .HasForeignKey(e => e.UserId)
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired(false); // Cho phép log của khách vãng lai (UserId null)
                });
            // TourDetail: bảng liên kết N-N giữa Tour và Poi

            // Tour xóa -> dòng liên kết xóa theo
            modelBuilder.Entity<TourDetail>()
                .HasOne(td => td.Tour)
                .WithMany(t => t.TourDetails)
                .HasForeignKey(td => td.TourId)
                .OnDelete(DeleteBehavior.Cascade);

            // Poi xóa -> chặn nếu còn trong tour, tránh mất cấu trúc tour
            modelBuilder.Entity<TourDetail>()
                .HasOne(td => td.Poi)
                .WithMany() // Không cần navigation ngược trong Poi
                .HasForeignKey(td => td.PoiId)
                .OnDelete(DeleteBehavior.Restrict);

            // TourTranslation: mỗi tour chỉ có 1 bản dịch trên mỗi ngôn ngữ
            modelBuilder.Entity<TourTranslation>(entity =>
            {
                entity.HasIndex(t => new { t.TourId, t.LanguageCode }).IsUnique();

                // Tour xóa -> bản dịch xóa theo
                entity.HasOne(t => t.Tour)
                    .WithMany(tour => tour.TourTranslations)
                    .HasForeignKey(t => t.TourId)
                    .OnDelete(DeleteBehavior.Cascade);
            });


            // PoiListenLog: tối ưu thống kê theo POI và mốc thời gian
            modelBuilder.Entity<PoiListenLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.PoiId);
                entity.HasIndex(e => e.Timestamp);
            });

            // OwnerNotification: thông báo cho chủ POI
            modelBuilder.Entity<OwnerNotification>(entity =>
            {
                entity.ToTable("OwnerNotifications");

                // Index phục vụ lọc theo trạng thái đọc và thời gian tạo
                entity.HasIndex(e => e.OwnerId);
                entity.HasIndex(e => new { e.OwnerId, e.IsRead, e.CreatedAt });

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(e => e.OwnerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Poi>()
                    .WithMany()
                    .HasForeignKey(e => e.PoiId)
                    .OnDelete(DeleteBehavior.SetNull); // Giữ lịch sử thông báo khi POI bị xóa
            });

            // AdminNotification: thông báo cho tài khoản quản trị
            modelBuilder.Entity<AdminNotification>(entity =>
            {
                entity.ToTable("AdminNotifications");

                // Index phục vụ màn hình danh sách thông báo admin
                entity.HasIndex(e => e.AdminId);
                entity.HasIndex(e => new { e.AdminId, e.IsRead, e.CreatedAt });

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(e => e.AdminId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Poi>()
                    .WithMany()
                    .HasForeignKey(e => e.PoiId)
                    .OnDelete(DeleteBehavior.SetNull); // Không mất thông báo cũ nếu POI đã bị xóa
            });
            modelBuilder.Entity<QrScanLog>(e =>
            {
                e.ToTable("QrScanLogs");
                e.HasKey(x => x.Id);

                e.Property(x => x.ScannedAt)
                    .HasColumnType("datetime(6)");

                e.HasIndex(x => x.PoiId);
                e.HasIndex(x => x.ScannedAt);

                e.HasOne(x => x.Poi)
                    .WithMany()
                    .HasForeignKey(x => x.PoiId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // SubscriptionPlan: bảng danh mục gói
            modelBuilder.Entity<SubscriptionPlan>().ToTable("SubscriptionPlans");

            // BoothOwnerSubscription: lịch sử đăng ký của từng owner
            modelBuilder.Entity<BoothOwnerSubscription>(entity =>
            {
                entity.ToTable("BoothOwnerSubscriptions");

                // Khóa ngoại Owner → User
                entity.HasOne(s => s.Owner)
                    .WithMany() // User không cần navigation ngược
                    .HasForeignKey(s => s.OwnerId)
                    .OnDelete(DeleteBehavior.Cascade); // Xóa user → xóa lịch sử sub

                // Khóa ngoại Plan → SubscriptionPlan
                entity.HasOne(s => s.Plan)
                    .WithMany(p => p.Subscriptions)
                    .HasForeignKey(s => s.PlanId)
                    .OnDelete(DeleteBehavior.Restrict); // Giữ lại lịch sử dù Admin ẩn gói

                // Index tối ưu truy vấn kiểm tra active subscription
                entity.HasIndex(s => new { s.OwnerId, s.Status })
                    .HasDatabaseName("IX_BoothOwnerSubscriptions_OwnerId_Status");

                // Index tối ưu job expire
                entity.HasIndex(s => s.EndDate)
                    .HasDatabaseName("IX_BoothOwnerSubscriptions_EndDate");
            });
        }
    }
}