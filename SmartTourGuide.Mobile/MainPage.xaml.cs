using Mapsui;
using Mapsui.Projections;
using Mapsui.UI.Maui;
using Mapsui.Tiling; // Để load bản đồ OSM
using SmartTourGuide.Mobile.Services;
using Microsoft.Maui.Devices.Sensors;

namespace SmartTourGuide.Mobile;

public partial class MainPage : ContentPage
{
    private readonly PoiApiService _apiService;

    public MainPage()
    {
        InitializeComponent();
        _apiService = new PoiApiService();

        // Khởi tạo Map cụ thể thay vì dùng dấu ?
        var map = new Mapsui.Map();
        map.Layers.Add(OpenStreetMap.CreateTileLayer());
        mapView.Map = map;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CheckPermissions();
        await LoadCurrentLocation();
        await LoadPoisOnMap();
    }

    private async Task CheckPermissions()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        }
    }

    private async Task LoadCurrentLocation()
    {
        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Medium);
            var location = await Geolocation.GetLocationAsync(request);

            if (location != null)
            {
                // SỬA LỖI 1: Chuyển đổi tọa độ GPS sang MPoint của Mapsui
                var smc = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
                var mPoint = new MPoint(smc.x, smc.y); // Tạo mới MPoint rõ ràng

                // Thêm Pin vị trí của tôi
                var myPin = new Pin(mapView)
                {
                    Position = new Position(location.Latitude, location.Longitude),
                    Label = "Tôi đang ở đây",
                    Color = Colors.Blue,
                    Scale = 0.7f // Chỉnh kích thước chấm
                };

                MainThread.BeginInvokeOnMainThread(() => {
                    mapView.Map?.Navigator.CenterOnAndZoomTo(mPoint, 2);
                    mapView.Pins.Add(myPin);
                    statusLabel.Text = "Đã tìm thấy vị trí của bạn!";
                });
            }
        }
        catch (Exception)
        {
            statusLabel.Text = "Lỗi GPS (Bật vị trí trên điện thoại chưa?)";
        }
    }

    private async Task LoadPoisOnMap()
    {
        statusLabel.Text = "Đang tải dữ liệu...";
        try
        {
            var pois = await _apiService.GetPoisAsync();

            // Xóa Pin cũ (Giữ lại pin màu xanh của user nếu muốn, ở đây ta xóa hết cho sạch)
            mapView.Pins.Clear();

            foreach (var poi in pois)
            {
                // SỬA LỖI 2: PinType.Pin thay vì PinType.Icon
                var pin = new Pin(mapView)
                {
                    Position = new Position(poi.Latitude, poi.Longitude),
                    Type = PinType.Pin, // Dùng loại Pin mặc định (hình giọt nước)
                    Label = poi.Name,
                    Address = poi.Address,
                    Color = Colors.Red,
                    Scale = 0.5f
                };

                // Sự kiện bấm vào Pin
                pin.Callout.CalloutClicked += (s, e) =>
                {
                    DisplayAlertAsync("Thông tin", $"Địa điểm: {poi.Name}\n{poi.Address}", "OK");
                };

                mapView.Pins.Add(pin);
            }
            statusLabel.Text = $"Đã tải {pois.Count} địa điểm xung quanh.";
        }
        catch (Exception)
        {
            statusLabel.Text = "Không thể kết nối API.";
        }
    }

    // Sự kiện nút bấm Tải lại
    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        await LoadPoisOnMap();
    }
}