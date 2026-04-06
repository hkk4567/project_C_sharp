using CommunityToolkit.Mvvm.Messaging;
using SmartTourGuide.Mobile.Models;

namespace SmartTourGuide.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    // Khi app vào background (khoá màn hình, cuộc gọi đến, chuyển sang app khác...)
    // → tạm dừng audio để không phát vào tai người đang nghe điện thoại
    protected override void OnSleep()
    {
        base.OnSleep();
        WeakReferenceMessenger.Default.Send(new AppSleepMessage());
    }

    // Khi app quay lại foreground → tiếp tục phát từ chỗ đã dừng
    protected override void OnResume()
    {
        base.OnResume();
        WeakReferenceMessenger.Default.Send(new AppResumeMessage());
    }

    // ─── Android Deep Link Entry Point ────────────────────────────────────
    // Được gọi từ MainActivity.OnNewIntent (xem MainActivity.cs bên dưới)
    public void HandleDeepLink(Uri uri)
    {
        try
        {
            var segments = uri.AbsolutePath
                              .Trim('/')
                              .Split('/', StringSplitOptions.RemoveEmptyEntries);

            var poiIndex = Array.IndexOf(segments, "poi");
            if (poiIndex < 0 || poiIndex + 1 >= segments.Length) return;

            if (!int.TryParse(segments[poiIndex + 1], out var poiId)) return;

            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var autoPlay = string.Equals(query["autoplay"], "true", StringComparison.OrdinalIgnoreCase);

            WeakReferenceMessenger.Default.Send(new DeepLinkPoiMessage
            {
                PoiId = poiId,
                AutoPlay = autoPlay
            });

            if (Windows.Count > 0 && Windows[0].Page is AppShell shell)
            {
                shell.GoToAsync("//MainPage");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DeepLink] Error: {ex.Message}");
        }
    }

}