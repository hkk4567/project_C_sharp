namespace SmartTourGuide.Mobile;

public partial class WelcomePage : ContentPage
{
    public WelcomePage()
    {
        InitializeComponent();
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        if (sender is View view)
        {
            await view.ScaleToAsync(0.97, 80, Easing.CubicOut);
            await view.ScaleToAsync(1.0, 80, Easing.CubicIn);
        }

        await Shell.Current.GoToAsync("//MainPage");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 🟢 NẾU APP ĐƯỢC MỞ TỪ QUÉT QR -> BỎ QUA TRANG NÀY VÀ VÀO THẲNG MAINPAGE
        if (App.PendingDeepLinkPoiId.HasValue)
        {
            // Tùy theo cấu trúc AppShell của bạn, thường dùng route "//MainPage"
            await Shell.Current.GoToAsync("//MainPage");
            return;
        }

    }
}
