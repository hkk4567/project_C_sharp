using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace SmartTourGuide.Mobile.Platforms.Android;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize
                         | ConfigChanges.Orientation
                         | ConfigChanges.UiMode
                         | ConfigChanges.ScreenLayout
                         | ConfigChanges.SmallestScreenSize
                         | ConfigChanges.Density,
    LaunchMode = LaunchMode.SingleTask)]

// 1. App Links: HTTPS (Dev Tunnel / Ngrok)
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "https",
    DataHost = "2tlcgj8k-7058.asse.devtunnels.ms",
    DataPathPrefix = "/poi/",
    AutoVerify = true)]

// 2. Custom Scheme (Fallback)
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "smarttourguide",
    DataHost = "poi")]

// 3. THÊM MỚI: Dành cho Test Localhost/Emulator (Cái bạn vừa copy từ XML sang)
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "http",
    DataHost = "10.0.2.2",
    DataPort = "5277",
    AutoVerify = true)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Intent?.Data is { } uri)
            HandleUri(uri.ToString()!);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.Data is { } uri)
            HandleUri(uri.ToString()!);
    }

    private static void HandleUri(string? uriString)
    {
        if (string.IsNullOrEmpty(uriString)) return;

        try
        {
            var uri = new Uri(uriString);
            // Kiểm tra xem có đúng là đường dẫn /poi/ hay không
            if (uri.AbsolutePath.Contains("/poi/"))
            {
                var segments = uri.AbsolutePath.Split('/');
                var lastSegment = segments.LastOrDefault();

                if (int.TryParse(lastSegment, out int poiId))
                {
                    // Delay một chút để chắc chắn MainPage đã khởi tạo xong và đăng ký Messenger
                    Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            // Gửi tin nhắn tới MainPage (đã code ở file MainPage.DeepLink.cs)
                            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(
                                new DeepLinkPoiMessage(poiId, true));
                            System.Diagnostics.Debug.WriteLine($"[DeepLink] Đã gửi tin nhắn cho POI: {poiId}");
                        });
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] Deep link error: {ex.Message}");
        }
    }
}