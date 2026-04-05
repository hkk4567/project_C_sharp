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
}
