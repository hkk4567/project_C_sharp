using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting; // <--- DÒNG MỚI (Thay cho using Mapsui)

namespace SmartTourGuide.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp() // <--- DÙNG CÁI NÀY THAY CHO .UseMapsui()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
		builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}