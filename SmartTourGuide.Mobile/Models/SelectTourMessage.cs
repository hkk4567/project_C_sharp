using CommunityToolkit.Mvvm.Messaging.Messages;
using SmartTourGuide.Mobile.Services;

namespace SmartTourGuide.Mobile.Models;

// Lớp này dùng để định nghĩa loại dữ liệu bạn muốn truyền đi
public class SelectTourMessage : ValueChangedMessage<TourModel>
{
    public SelectTourMessage(TourModel value) : base(value)
    {
    }
}