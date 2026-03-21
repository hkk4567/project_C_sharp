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
}