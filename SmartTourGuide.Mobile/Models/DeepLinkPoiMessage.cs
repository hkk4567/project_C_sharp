using CommunityToolkit.Mvvm.Messaging.Messages;

namespace SmartTourGuide.Mobile.Models;

// DeepLinkPoiMessage.cs
// Chứa dữ liệu PoiId và AutoPlay.
// App gửi message, MainPage nhận để điều hướng tới POI và có thể tự phát audio.
// Chọn tour từ trang ToursPage về MainPage

/// <summary>
/// Mục đích file:
/// 1) Định nghĩa message dùng để truyền dữ liệu deep link POI.
/// 2) Tách luồng gửi/nhận giữa App và MainPage qua Messenger.
///
/// Message này được gửi từ App.HandleDeepLink() đến MainPage.
/// MainPage nhận PoiId + AutoPlay để điều hướng và phát audio tương ứng.
/// </summary>
public class DeepLinkPoiMessage : ValueChangedMessage<DeepLinkPoiPayload>
{
    // Constructor tiện dụng: tạo payload từ 2 tham số cơ bản.
    public DeepLinkPoiMessage(int poiId, bool autoPlay)
       : base(new DeepLinkPoiPayload { PoiId = poiId, AutoPlay = autoPlay })
    {
    }

    // Constructor nhận payload có sẵn (dùng khi đã dựng object trước đó).
    public DeepLinkPoiMessage(DeepLinkPoiPayload payload) : base(payload) { }

    // Constructor mặc định cho một số ngữ cảnh binding/khởi tạo rỗng.
    public DeepLinkPoiMessage() : base(new DeepLinkPoiPayload()) { }

    // Truy cập nhanh PoiId mà không cần đi qua Value.PoiId.
    public int PoiId
    {
        get => Value.PoiId;
        set => Value.PoiId = value;
    }

    // Cờ tự động phát audio sau khi điều hướng đến POI.
    public bool AutoPlay
    {
        get => Value.AutoPlay;
        set => Value.AutoPlay = value;
    }
}

// Dữ liệu thực được mang trong ValueChangedMessage.
public class DeepLinkPoiPayload
{
    // ID POI đích cần mở.
    public int PoiId { get; set; }

    // true: tự động phát audio, false: chỉ mở chi tiết POI.
    public bool AutoPlay { get; set; }
}
