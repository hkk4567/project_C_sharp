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
    DataHost = "6b5j1b5h-7058.asse.devtunnels.ms",
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
            if (!TryExtractDeepLinkPayload(uri, out var poiId, out var autoPlay))
                return;

            // Delay một chút để chắc chắn MainPage đã khởi tạo xong và đăng ký Messenger
            Task.Run(async () =>
            {
                await Task.Delay(2000);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Gửi tin nhắn tới MainPage (đã code ở file MainPage.DeepLink.cs)
                    CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(
                        new DeepLinkPoiMessage(poiId, autoPlay));
                    System.Diagnostics.Debug.WriteLine($"[DeepLink] Đã gửi tin nhắn cho POI: {poiId}");
                });
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] Deep link error: {ex.Message}");
        }
    }

    private static bool TryExtractDeepLinkPayload(Uri uri, out int poiId, out bool autoPlay)
    {
        poiId = 0;
        autoPlay = true;

        if (TryExtractPoiId(uri, out poiId))
        {
            autoPlay = TryReadAutoPlayQuery(uri);
            return true;
        }

        return false;
    }

    private static bool TryExtractPoiId(Uri uri, out int poiId)
    {
        poiId = 0;

        var segments = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        // HTTPS app link: /poi/{id}
        var poiIndex = Array.IndexOf(segments, "poi");
        if (poiIndex >= 0 && poiIndex + 1 < segments.Length && int.TryParse(segments[poiIndex + 1], out poiId))
            return true;

        // Custom scheme: smarttourguide://poi/{id}
        if (uri.Scheme.Equals("smarttourguide", StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("poi", StringComparison.OrdinalIgnoreCase)
            && segments.Length >= 1
            && int.TryParse(segments[0], out poiId))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadAutoPlayQuery(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
            return true;

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in query)
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 0)
                continue;

            if (!kv[0].Equals("autoplay", StringComparison.OrdinalIgnoreCase))
                continue;

            if (kv.Length == 1)
                return true;

            var rawValue = Uri.UnescapeDataString(kv[1]);
            return rawValue.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}