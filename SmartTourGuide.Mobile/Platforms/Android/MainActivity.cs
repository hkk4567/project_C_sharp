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
            if (IPlatformApplication.Current?.Services
                    ?.GetService<App>() is { } app)
            {
                app.HandleDeepLink(new Uri(uriString));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] Deep link error: {ex.Message}");
        }
    }
}