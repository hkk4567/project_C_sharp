using CommunityToolkit.Mvvm.Messaging;
using SmartTourGuide.Mobile.Models;

namespace SmartTourGuide.Mobile;

public partial class MainPage
{
    // Đăng ký nhận tin nhắn Deep Link
    // Gọi hàm này trong OnAppearing() của MainPage.xaml.cs
    private void RegisterDeepLinkHandler()
    {
        WeakReferenceMessenger.Default
            .Register<DeepLinkPoiMessage>(this, async (_, msg) =>
            {
                await HandleDeepLinkPoiAsync(msg.PoiId, msg.AutoPlay);
            });
    }

    // Hủy đăng ký để tránh rò rỉ bộ nhớ
    // Gọi hàm này trong OnDisappearing() của MainPage.xaml.cs
    private void UnregisterDeepLinkHandler()
    {
        WeakReferenceMessenger.Default.Unregister<DeepLinkPoiMessage>(this);
    }

    // Xử lý logic Deep Link
    private async Task HandleDeepLinkPoiAsync(int poiId, bool autoPlay)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                // 1. Tìm POI (Sửa _pois thành _allPoisCache)
                var poi = _allPoisCache?.FirstOrDefault(p => p.Id == poiId)
                          ?? await FetchPoiFromApiAsync(poiId);

                if (poi == null)
                {
                    await DisplayAlertAsync(AppRes.AlertNotice, "Không tìm thấy điểm tham quan này.", AppRes.OkButton);
                    return;
                }

                // 2. Di chuyển bản đồ Mapsui đến POI (Sửa _mapView thành mapView)
                if (mapView?.Map?.Navigator != null)
                {
                    var smc = Mapsui.Projections.SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
                    mapView.Map.Navigator.CenterOnAndZoomTo(new Mapsui.MPoint(smc.x, smc.y), 1.5, 800);
                }

                // 3. Hiển thị Popup chi tiết
                ShowPoiDetail(poi);

                // 4. Tự động phát nhạc nếu yêu cầu
                if (autoPlay && poi.AudioUrls?.Count > 0)
                {
                    await Task.Delay(1000); // Chờ UI ổn định
                    OnPlayAudioClicked(null, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeepLink] Error: {ex.Message}");
            }
        });
    }

    private async Task<PoiModel?> FetchPoiFromApiAsync(int poiId)
    {
        try
        {
            // Dùng _apiService đã có trong Fields.cs
            var allPois = await _apiService.GetPoisAsync(_currentLanguageCode);
            return allPois?.FirstOrDefault(p => p.Id == poiId);
        }
        catch
        {
            return null;
        }
    }
}