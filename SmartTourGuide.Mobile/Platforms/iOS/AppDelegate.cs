using Foundation;
using UIKit;

namespace SmartTourGuide.Mobile.Platforms.iOS;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool ContinueUserActivity(
		UIApplication application,
		NSUserActivity userActivity,
		UIApplicationRestorationHandler completionHandler)
	{
		// 1. Kiểm tra xem link có phải là link web (Universal Link) không
		if (userActivity.ActivityType == NSUserActivityType.BrowsingWeb && userActivity.WebPageUrl != null && userActivity.WebPageUrl.AbsoluteString != null)
		{
			// 2. Chuyển đổi từ NSUrl (iOS) sang Uri (.NET)
			var uri = new Uri(userActivity.WebPageUrl.AbsoluteString);

			// 3. Gọi hàm xử lý ở App.xaml.cs
			if (IPlatformApplication.Current?.Application is App app)
			{
				app.HandleDeepLink(uri);
				return true;
			}
		}

		return false;
	}
}