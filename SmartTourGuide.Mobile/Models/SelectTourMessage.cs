using CommunityToolkit.Mvvm.Messaging.Messages;
using SmartTourGuide.Mobile.Services;

namespace SmartTourGuide.Mobile.Models;
// Chọn tour từ trang ToursPage về MainPage
// SelectTourMessage.cs
// Dùng truyền TourModel đã chọn.
// MainPage nhận và render lộ trình tour trên bản đồ.
// Mục đích file:
// - Định nghĩa message truyền TourModel đã chọn giữa các màn hình.
// - Dùng với WeakReferenceMessenger để ToursPage gửi và MainPage nhận.

// Message mang theo dữ liệu tour được người dùng chọn.
public class SelectTourMessage : ValueChangedMessage<TourModel>
{
    // Constructor nhận tour cần gửi đi.
    public SelectTourMessage(TourModel value) : base(value)
    {
    }
}