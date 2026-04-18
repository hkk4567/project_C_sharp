using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Data.Entities;
using SmartTourGuide.API.Services;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.HttpOverrides;
var builder = WebApplication.CreateBuilder(args);

// --- 1. Đăng ký các dịch vụ (Services) ---

// Đăng ký Controllers (QUAN TRỌNG)
builder.Services.AddControllers();

// Đăng ký Swagger để test API
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Lấy connection string từ file json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Đăng ký DbContext dùng MySQL (Pomelo)
// Lưu ý: Đảm bảo bạn đã cài package: Pomelo.EntityFrameworkCore.MySql
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
);

// Đăng ký FileService
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<SubscriptionService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

// Cấu hình để ASP.NET Core tin tưởng thông tin từ Proxy/Tunnel
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;
    // Bỏ qua giới hạn IP để nhận được header từ Microsoft Dev Tunnels
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
var app = builder.Build();

// Seed gói mặc định cho subscription
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var existingPlans = db.SubscriptionPlans.ToList();

    // Luôn đảm bảo có gói Miễn phí mặc định và luôn Active (fix cứng)
    var freePlan = existingPlans.FirstOrDefault(p => p.Name == "Miễn phí" || p.Name == "Mien phi");
    if (freePlan is null)
    {
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Name = "Miễn phí",
            Description = "Gói mặc định miễn phí",
            PriceMonthly = 0,
            PriceYearly = 0,
            MaxActivePois = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
    }
    else
    {
        freePlan.Name = "Miễn phí";
        freePlan.Description = "Gói mặc định miễn phí";
        freePlan.PriceMonthly = 0;
        freePlan.PriceYearly = 0;
        freePlan.MaxActivePois = 1;
        freePlan.IsActive = true;
    }

    // Seed sẵn 1 gói Pro mẫu nếu chưa có
    var proPlan = existingPlans.FirstOrDefault(p => p.Name == "Pro");
    if (proPlan is null)
    {
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Name = "Pro",
            Description = "Gói nâng cao mở rộng số POI và khả năng hiển thị",
            PriceMonthly = 199000,
            PriceYearly = 1999000,
            MaxActivePois = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
    }
    else
    {
        proPlan.MaxActivePois = 1;
    }

    db.SaveChanges();
}

//  Kích hoạt Middleware đọc Header từ Tunnel (Phải để ngay sau khi Build)
app.UseForwardedHeaders();
// ─── Static Files ────────────────────────────────────────────────────────────
// Cấu hình StaticFiles để phục vụ thư mục .well-known
// (tên bắt đầu bằng dấu chấm bị ẩn theo mặc định)
var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
    Path.Combine(builder.Environment.WebRootPath ??
                 Path.Combine(builder.Environment.ContentRootPath, "wwwroot")));

// Cần map content type đặc biệt cho apple-app-site-association (không có extension)
var contentTypeProvider = new FileExtensionContentTypeProvider();
// Đảm bảo các file json vẫn là json
contentTypeProvider.Mappings[".json"] = "application/json";

// [QUAN TRỌNG] Thêm dòng này để định nghĩa file APK
contentTypeProvider.Mappings[".apk"] = "application/vnd.android.package-archive";

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = fileProvider,
    RequestPath = "",
    ServeUnknownFileTypes = true,
    // [SỬA TẠI ĐÂY] Đừng để mặc định là json nữa, hãy để null hoặc octet-stream
    DefaultContentType = "application/octet-stream",
    ContentTypeProvider = contentTypeProvider
});

// --- 2. Cấu hình Pipeline (Middleware) ---

// Bật Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Map các Controller vào đường dẫn URL (QUAN TRỌNG)
app.MapControllers();

// Chạy ứng dụng (QUAN TRỌNG NHẤT - Thiếu cái này app sẽ tắt ngay)
app.Run();