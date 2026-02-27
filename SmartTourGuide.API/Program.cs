using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data;
using SmartTourGuide.API.Services;

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

var app = builder.Build();
app.UseStaticFiles();
// --- 2. Cấu hình Pipeline (Middleware) ---

// Bật Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();

app.UseAuthorization();

// Map các Controller vào đường dẫn URL (QUAN TRỌNG)
app.MapControllers();

// Chạy ứng dụng (QUAN TRỌNG NHẤT - Thiếu cái này app sẽ tắt ngay)
app.Run();