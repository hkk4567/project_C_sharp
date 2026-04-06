using CommunityToolkit.Mvvm.Messaging.Messages;

namespace SmartTourGuide.Mobile.Models;

/// <summary>
/// Tin nhắn được gửi từ App.HandleDeepLink() đến MainPage.
/// MainPage đăng ký nhận tin nhắn và xử lý điều hướng + phát audio.
/// </summary>
public class DeepLinkPoiMessage : ValueChangedMessage<DeepLinkPoiPayload>
{
    public DeepLinkPoiMessage(DeepLinkPoiPayload payload) : base(payload) { }

    public DeepLinkPoiMessage() : base(new DeepLinkPoiPayload()) { }

    public int PoiId
    {
        get => Value.PoiId;
        set => Value.PoiId = value;
    }

    public bool AutoPlay
    {
        get => Value.AutoPlay;
        set => Value.AutoPlay = value;
    }
}

public class DeepLinkPoiPayload
{
    public int PoiId { get; set; }
    public bool AutoPlay { get; set; }
}
