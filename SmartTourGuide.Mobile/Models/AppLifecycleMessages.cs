using CommunityToolkit.Mvvm.Messaging.Messages;

namespace SmartTourGuide.Mobile.Models;

// Gửi khi app vào background (màn hình khóa, cuộc gọi đến, chuyển app...)
public class AppSleepMessage : ValueChangedMessage<bool>
{
    public AppSleepMessage() : base(true) { }
}

// Gửi khi app quay lại foreground
public class AppResumeMessage : ValueChangedMessage<bool>
{
    public AppResumeMessage() : base(true) { }
}
