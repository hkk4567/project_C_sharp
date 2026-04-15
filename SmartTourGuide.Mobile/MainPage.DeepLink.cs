using CommunityToolkit.Mvvm.Messaging;
using SmartTourGuide.Mobile.Models;

namespace SmartTourGuide.Mobile;

// File này xử lý luồng deep link: nhận POI từ QR, điều hướng và tự động phát audio.
public partial class MainPage
{
    // Đăng ký nhận tin nhắn deep link khi trang bắt đầu hiển thị.
    // Hàm này được gọi trong OnAppearing() của MainPage.xaml.cs.
    private void RegisterDeepLinkHandler()
    {
        WeakReferenceMessenger.Default
            .Register<DeepLinkPoiMessage>(this, async (_, msg) =>
            {
                System.Diagnostics.Debug.WriteLine($"[DeepLink] Nhận tin nhắn xử lý POI ID: {msg.PoiId}");
                await HandleDeepLinkPoiAsync(msg.PoiId, msg.AutoPlay);
            });
    }

    // Hủy đăng ký khi rời trang để tránh xử lý trùng và rò rỉ bộ nhớ.
    // Hàm này được gọi trong OnDisappearing() của MainPage.xaml.cs.
    private void UnregisterDeepLinkHandler()
    {
        WeakReferenceMessenger.Default.Unregister<DeepLinkPoiMessage>(this);
    }

    // Luồng xử lý chính khi nhận deep link POI.
    private async Task HandleDeepLinkPoiAsync(int poiId, bool autoPlay)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                // 1) Tìm POI từ cache; nếu chưa có thì gọi API để lấy mới.
                var poi = _allPoisCache?.FirstOrDefault(p => p.Id == poiId)
                          ?? await FetchPoiFromApiAsync(poiId);

                if (poi == null)
                {
                    await DisplayAlertAsync(AppRes.AlertNotice, "Không tìm thấy điểm tham quan này.", AppRes.OkButton);
                    return;
                }

                // 2) Gán POI hiện tại để các chức năng chi tiết/audio dùng đúng dữ liệu.
                _currentSelectedPoi = poi;

                // 3) Di chuyển camera bản đồ đến vị trí POI.
                var mapView = MapViewCtrl; // Kiểm tra lại tên x:Name trong XAML của bạn
                if (mapView?.Map?.Navigator != null)
                {
                    var smc = Mapsui.Projections.SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
                    mapView.Map.Navigator.CenterOnAndZoomTo(new Mapsui.MPoint(smc.x, smc.y), 1.5, 1000);
                }

                // 4) Mở popup thông tin POI.
                ShowPoiDetail(poi);

                // 5) Bắn API ghi nhận lượt quét QR
                // Lấy Device ID để API có thể áp dụng luật chống quét trùng (debounce)
                var deviceId = Preferences.Get("device_uuid", "unknown");
                if (deviceId == "unknown")
                {
                    deviceId = Guid.NewGuid().ToString();
                    Preferences.Set("device_uuid", deviceId);
                }
                _ = Task.Run(() => _apiService.LogQrScanAsync(poiId, deviceId));

                // 6) Nếu có cờ autoplay thì tự động phát audio sau khi popup hiển thị.
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

    // Tải POI từ API khi cache cục bộ chưa có dữ liệu tương ứng.
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