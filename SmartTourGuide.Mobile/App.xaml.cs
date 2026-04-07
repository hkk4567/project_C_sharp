using CommunityToolkit.Mvvm.Messaging;
using SmartTourGuide.Mobile.Models;

namespace SmartTourGuide.Mobile;

public partial class App : Application
{
    // THÊM 2 BIẾN NÀY ĐỂ LƯU TẠM THÔNG TIN TỪ QR (Tránh lỗi Cold Start)
    public static int? PendingDeepLinkPoiId { get; set; }
    public static bool PendingDeepLinkAutoPlay { get; set; }

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        WeakReferenceMessenger.Default.Send(new AppSleepMessage());
    }

    protected override void OnResume()
    {
        base.OnResume();
        WeakReferenceMessenger.Default.Send(new AppResumeMessage());
    }

    // ─── Android Deep Link Entry Point ────────────────────────────────────
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

            // SỬA LẠI: Mặc định quét QR là TỰ ĐỘNG PHÁT NHẠC (true)
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            bool autoPlay = true;
            if (query["autoplay"] != null)
            {
                autoPlay = string.Equals(query["autoplay"], "true", StringComparison.OrdinalIgnoreCase);
            }

            // 1. LƯU LẠI CHO MAINPAGE XỬ LÝ (Nếu app vừa bị tắt hẳn)
            PendingDeepLinkPoiId = poiId;
            PendingDeepLinkAutoPlay = autoPlay;

            // 2. GỬI TIN NHẮN (Nếu app chỉ đang thu nhỏ, vẫn còn chạy ngầm)
            WeakReferenceMessenger.Default.Send(new DeepLinkPoiMessage
            {
                PoiId = poiId,
                AutoPlay = autoPlay
            });

            var currentWindow = Application.Current?.Windows?.Count > 0 ? Application.Current.Windows[0] : null;
            if (currentWindow?.Page is Shell shell)
            {
                shell.Dispatcher.Dispatch(async () =>
                {
                    await shell.GoToAsync("//MainPage", animate: false);
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DeepLink] Error: {ex.Message}");
        }
    }
}