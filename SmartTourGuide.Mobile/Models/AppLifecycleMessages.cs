using CommunityToolkit.Mvvm.Messaging.Messages;

namespace SmartTourGuide.Mobile.Models;

// AppLifecycleMessages.cs
// Dùng để báo khi app vào nền hoặc quay lại foreground.
// MainPage nhận message này để tạm dừng hoặc tiếp tục audio.

// Mục đích file:
// - Định nghĩa các message vòng đời ứng dụng để các màn hình giao tiếp qua Messenger.
// - Tách rời logic App và MainPage, tránh gọi trực tiếp phụ thuộc lẫn nhau.

// Message gửi khi app vào background (khóa màn hình, có cuộc gọi, chuyển app...).
// MainPage dùng message này để tạm dừng audio/tiến trình cần thiết.
public class AppSleepMessage : ValueChangedMessage<bool>
{
    // Value=true chỉ mang ý nghĩa tín hiệu sự kiện đã xảy ra.
    public AppSleepMessage() : base(true) { }
}

// Message gửi khi app quay lại foreground.
// MainPage dùng message này để tiếp tục audio/khôi phục trạng thái phù hợp.
public class AppResumeMessage : ValueChangedMessage<bool>
{
    // Value=true chỉ mang ý nghĩa tín hiệu sự kiện đã xảy ra.
    public AppResumeMessage() : base(true) { }
}
