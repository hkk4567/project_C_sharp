using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
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