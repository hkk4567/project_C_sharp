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
                System.Diagnostics.Debug.WriteLine($"[DeepLink] Nhận tin nhắn xử lý POI ID: {msg.PoiId}");
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

                // 2. Cập nhật biến POI hiện tại (Cực kỳ quan trọng để phát nhạc)
                _currentSelectedPoi = poi;

                // 3. Di chuyển bản đồ đến POI
                var mapView = MapViewCtrl; // Kiểm tra lại tên x:Name trong XAML của bạn
                if (mapView?.Map?.Navigator != null)
                {
                    var smc = Mapsui.Projections.SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
                    mapView.Map.Navigator.CenterOnAndZoomTo(new Mapsui.MPoint(smc.x, smc.y), 1.5, 1000);
                }

                // 4. Hiển thị Popup chi tiết
                ShowPoiDetail(poi);

                // 5. Tự động phát nhạc
                if (autoPlay)
                {
                    await Task.Delay(500); // Chờ Popup hiện lên mượt mà
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
            // Lấy toàn bộ danh sách POI mới nhất và cập nhật cache luôn
            var allPois = await _apiService.GetPoisAsync(_currentLanguageCode);
            if (allPois != null)
            {
                _allPoisCache = allPois.ToList();
                return _allPoisCache.FirstOrDefault(p => p.Id == poiId);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}