using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting; // <--- DÒNG MỚI (Thay cho using Mapsui)
using Plugin.Maui.Audio;
namespace SmartTourGuide.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        CacheService.ConfigureMapTileCache();
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp() // <--- DÙNG CÁI NÀY THAY CHO .UseMapsui()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });
        builder.Services.AddSingleton(AudioManager.Current);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}