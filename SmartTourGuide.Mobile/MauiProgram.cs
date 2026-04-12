using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Plugin.Maui.Audio;
namespace SmartTourGuide.Mobile;

// Mục đích file:
// - Khởi tạo MAUI app và đăng ký các dịch vụ dùng toàn cục.
// - Cấu hình cache bản đồ, SkiaSharp, font và logging.
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // 1) Cấu hình thư mục cache tile map trước khi dựng app.
        CacheService.ConfigureMapTileCache();

        // 2) Tạo builder gốc của MAUI.
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            // 3) Bật pipeline SkiaSharp cho các control đồ họa (thay cho UseMapsui cũ).
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                // 4) Đăng ký font dùng chung trong toàn ứng dụng.
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // 5) Đăng ký audio manager singleton để dùng xuyên suốt app.
        builder.Services.AddSingleton(AudioManager.Current);

#if DEBUG
        // 6) Bật logging debug khi chạy môi trường phát triển.
        builder.Logging.AddDebug();
#endif

        // 7) Build và trả về ứng dụng MAUI hoàn chỉnh.
        return builder.Build();
    }
}