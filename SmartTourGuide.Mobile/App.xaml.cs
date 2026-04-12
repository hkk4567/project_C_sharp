using CommunityToolkit.Mvvm.Messaging;
using SmartTourGuide.Mobile.Models;

namespace SmartTourGuide.Mobile;

// File nay quan ly vong doi song cua ung dung va
// xu ly deep link/QR.
public partial class App : Application
{
    // Luu tam thong tin QR/deep link khi app chua khoi tao xong hoac bi tat han.
    public static int? PendingDeepLinkPoiId { get; set; }
    public static bool PendingDeepLinkAutoPlay { get; set; }

    public App()
    {
        InitializeComponent();
    }

    // Tao cua so dau tien cua ung dung.
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    // Gui thong bao khi ung dung tam dung.
    protected override void OnSleep()
    {
        base.OnSleep();
        WeakReferenceMessenger.Default.Send(new AppSleepMessage());
    }

    // Gui thong bao khi ung dung quay lai hoat dong.
    protected override void OnResume()
    {
        base.OnResume();
        WeakReferenceMessenger.Default.Send(new AppResumeMessage());
    }

    // Xu ly deep link tu QR tren Android.
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

            // Mac dinh la tu dong phat am thanh khi quet QR.
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            bool autoPlay = true;
            if (query["autoplay"] != null)
            {
                autoPlay = string.Equals(query["autoplay"], "true", StringComparison.OrdinalIgnoreCase);
            }

            // Luu lai de MainPage xu ly neu app vua mo lai
            //  tu trang thai tat han.
            PendingDeepLinkPoiId = poiId;
            PendingDeepLinkAutoPlay = autoPlay;

            // Gui tin nhan neu app dang chay ngam va co the nhan ngay.
            WeakReferenceMessenger.Default.Send(new DeepLinkPoiMessage
            {
                PoiId = poiId,
                AutoPlay = autoPlay
            });

            // Dieu huong ve man hinh chinh de xu ly POI.
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