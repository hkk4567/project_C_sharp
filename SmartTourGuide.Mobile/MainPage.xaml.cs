using Mapsui;
using Mapsui.Layers;         // Để tạo MemoryLayer
using Mapsui.Nts;            // (Tùy chọn nếu dùng NTS, nhưng ở đây ta dùng logic cơ bản)
using Mapsui.Projections;
using Mapsui.Providers;      // Để chứa dữ liệu MemoryProvider
using Mapsui.Styles;         // Để tạo màu sắc (VectorStyle, Brush, Pen)
using Mapsui.Tiling; // Để load bản đồ OSM
using Mapsui.UI.Maui;
using Microsoft.Maui.Devices.Sensors;
using NetTopologySuite.Geometries;
using SmartTourGuide.Mobile.Services;

namespace SmartTourGuide.Mobile;

public partial class MainPage : ContentPage
{
    private readonly PoiApiService _apiService;
    private MemoryLayer _geofenceLayer;
    private const string BaseApiUrl = "http://localhost:5277";

    public MainPage()
    {
        InitializeComponent();
        _apiService = new PoiApiService();

        var map = new Mapsui.Map();
        map.Layers.Add(OpenStreetMap.CreateTileLayer());

        _geofenceLayer = new MemoryLayer
        {
            Name = "Geofences",
            Style = null // Style sẽ set cho từng feature
        };
        map.Layers.Add(_geofenceLayer);

        mapView.Map = map;
        mapView.PinClicked += OnPinClicked;
        // Bật lớp hiển thị vị trí ở đây thay vì XAML
        mapView.MyLocationLayer.Enabled = true;
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
        var lat = 21.016492;
        var lon = 105.834132;

        // 1. Chuyển đổi tọa độ để Zoom bản đồ
        var smc = SphericalMercator.FromLonLat(lon, lat);
        var mPoint = new MPoint(smc.x, smc.y);

        MainThread.BeginInvokeOnMainThread(() => {
            // 2. ÉP lớp vị trí mặc định về Hà Nội (Sửa lỗi chấm xanh ngoài biển)
            mapView.MyLocationLayer.UpdateMyLocation(new Mapsui.UI.Maui.Position(lat, lon));

            // 3. Zoom đến đó
            mapView.Map?.Navigator.CenterOnAndZoomTo(mPoint, resolution: 2);

            statusLabel.Text = $"Đã định vị: {lat}, {lon}";
        });
    }

    private async Task LoadPoisOnMap()
    {
        statusLabel.Text = "Đang tải dữ liệu...";
        try
        {
            var pois = await _apiService.GetPoisAsync();

            mapView.Pins.Clear();

            var oldLayer = mapView.Map.Layers.FirstOrDefault(l => l.Name == "Geofences");
            if (oldLayer != null) mapView.Map.Layers.Remove(oldLayer);

            var geofenceLayer = CreateGeofenceLayer(pois);
            mapView.Map.Layers.Insert(1, geofenceLayer);

            foreach (var poi in pois)
            {
                // SỬA: Chỉ định rõ Mapsui.UI.Maui.Position
                var pin = new Pin(mapView)
                {
                    Position = new Mapsui.UI.Maui.Position(poi.Latitude, poi.Longitude),
                    Type = PinType.Pin,
                    Label = poi.Name,
                    Address = poi.Address,
                    Color = Microsoft.Maui.Graphics.Colors.Red, // Rõ ràng với MAUI Color
                    Scale = 0.5f,
                    Tag = poi
                };

                mapView.Pins.Add(pin);
            }
            statusLabel.Text = $"Đã tải {pois.Count} địa điểm.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Lỗi: " + ex.Message;
        }
    }

    // Sự kiện nút bấm Tải lại
    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        await LoadPoisOnMap();
    }

    private MemoryLayer CreateGeofenceLayer(List<PoiModel> pois)
    {
        var features = new List<IFeature>();

        foreach (var poi in pois)
        {
            double radius = 50;
            var center = Mapsui.Projections.SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);

            // Tính toán bán kính thực tế đơn giản hơn
            double radiusInMapUnits = radius / Math.Cos(poi.Latitude * (Math.PI / 180));

            // SỬA: Sử dụng Coordinate của NetTopologySuite
            var coordinates = new List<Coordinate>();
            for (int i = 0; i <= 360; i += 10)
            {
                double angle = i * (Math.PI / 180);
                double x = center.x + radiusInMapUnits * Math.Cos(angle);
                double y = center.y + radiusInMapUnits * Math.Sin(angle);
                coordinates.Add(new Coordinate(x, y));
            }

            // Đảm bảo vòng lặp khép kín (điểm đầu = điểm cuối)
            if (!coordinates.First().Equals2D(coordinates.Last()))
                coordinates.Add(new Coordinate(coordinates.First()));

            // SỬA: Tạo Polygon từ NetTopologySuite
            var linearRing = new LinearRing(coordinates.ToArray());
            var polygon = new NetTopologySuite.Geometries.Polygon(linearRing);
            var feature = new Mapsui.Nts.GeometryFeature(polygon);

            // SỬA: Chỉ định rõ Mapsui.Styles.Brush và Color
            feature.Styles.Add(new Mapsui.Styles.VectorStyle
            {
                Fill = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(33, 150, 243, 60)),
                Outline = new Mapsui.Styles.Pen { Color = Mapsui.Styles.Color.Blue, Width = 1 }
            });

            features.Add(feature);
        }

        return new MemoryLayer
        {
            Name = "Geofences",
            Features = features,
            Style = null
        };
    }

    private void ShowPoiDetail(PoiModel poi)
    {
        // 1. Điền dữ liệu vào giao diện
        lblPoiName.Text = poi.Name;
        lblAddress.Text = poi.Address;
        lblDescription.Text = string.IsNullOrEmpty(poi.Description) ? "Chưa có mô tả." : poi.Description;

        // 2. Xử lý ảnh
        if (poi.ImageUrls != null && poi.ImageUrls.Count > 0)
        {
            // Có ảnh
            string fullUrl = $"{BaseApiUrl}{poi.ImageUrls[0]}";
            imgPoi.Source = ImageSource.FromUri(new Uri(fullUrl));
            ImageContainer.IsVisible = true; // Hiện cả khu vực ảnh + nút đóng
        }
        else
        {
            // Không có ảnh
            ImageContainer.IsVisible = false; // Ẩn cả khu vực
        }

        // 3. Hiện Popup lên (Dùng MainThread để chắc ăn)
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DetailPopup.IsVisible = true;
        });
    }

    private void OnClosePopupClicked(object? sender, EventArgs e)
    {
        DetailPopup.IsVisible = false;
    }

    private async void OnPlayAudioClicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync("Audio", "Tính năng đang phát triển (Sắp tới sẽ tích hợp Text-to-Speech)", "OK");
    }

    private void OnPinClicked(object? sender, PinClickedEventArgs e)
    {
        // e.Pin là đối tượng Pin vừa được nhấn
        if (e.Pin?.Tag is PoiModel poi)
        {
            ShowPoiDetail(poi);
            e.Handled = true; // Ngăn bản đồ thực hiện các lệnh mặc định khác
        }
    }
}