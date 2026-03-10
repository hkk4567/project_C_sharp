using CommunityToolkit.Mvvm.Messaging;
using Mapsui;
using Mapsui.Layers;         // Để tạo MemoryLayer
using Mapsui.Nts;            // (Tùy chọn nếu dùng NTS, nhưng ở đây ta dùng logic cơ bản)
using Mapsui.Projections;
using Mapsui.Providers;      // Để chứa dữ liệu MemoryProvider
using Mapsui.Styles;         // Để tạo màu sắc (VectorStyle, Brush, Pen)
using Mapsui.Tiling; // Để load bản đồ OSM
using Mapsui.UI.Maui;
using Mapsui.Widgets;
using Mapsui.Widgets.ButtonWidgets;
using Microsoft.Maui.Devices.Sensors;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate.Tri;
using Plugin.Maui.Audio;
using SmartTourGuide.Mobile.Models;
using SmartTourGuide.Mobile.Services;
using System.Globalization;

namespace SmartTourGuide.Mobile;

public partial class MainPage : ContentPage
{
    private readonly PoiApiService _apiService;
    private MemoryLayer _geofenceLayer;
    private const string BaseApiUrl = "http://10.0.2.2:5277";
    // BIẾN QUẢN LÝ ÂM THANH
    private IAudioPlayer? _audioPlayer;
    private CancellationTokenSource? _ttsCancellationToken; // Để dừng giọng đọc
    private bool _isPlaying = false; // Trạng thái đang phát hay dừng
    private PoiModel? _currentSelectedPoi;
    // Tọa độ dùng chung (Fake Location)
    private const double DefaultLat = 21.016492;
    private const double DefaultLon = 105.834132;
    // biến lưu ngôn ngữ hiện tại
    private string _currentLanguageCode = "vi-VN";

    private readonly SemaphoreSlim _mapLock = new SemaphoreSlim(1, 1);
    public MainPage()
    {
        InitializeComponent();

        WeakReferenceMessenger.Default.Register<SelectTourMessage>(this, (r, m) =>
        {
            // m.Value chính là đối tượng tourDetail bạn đã gửi
            var tourDetail = m.Value;

            // Cho Android "nghỉ" 500ms để đóng trang Modal hoàn tất
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(500);
                await RenderTourOnMap(tourDetail);
            });
        });

        _apiService = new PoiApiService();
        _currentLanguageCode = Preferences.Get("AppLanguage", "vi-VN");
        SetAppLanguage(_currentLanguageCode);

        UpdateLanguageButtonUI();

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

    // HÀM SET NGÔN NGỮ CHO TOÀN BỘ APP
    private void SetAppLanguage(string langCode)
    {
        CultureInfo culture;

        // Nếu là vi-VN, ta gọi Culture mặc định (rỗng) để nó trỏ chính xác vào file AppResources.resx
        if (langCode == "vi-VN")
        {
            culture = new CultureInfo(""); // Khởi tạo culture trung lập
        }
        else
        {
            culture = new CultureInfo(langCode);
        }

        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private void UpdateLanguageButtonUI()
    {
        if (_currentLanguageCode == "en-US")
        {
            btnLanguage.Text = "🇺🇸 English";
        }
        else
        {
            // Mặc định là Tiếng Việt
            btnLanguage.Text = "🇻🇳 Tiếng Việt";
        }
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

    private void MoveMapToDefaultLocation(double resolution = 2)
    {
        // 1. Chuyển đổi tọa độ
        var smc = SphericalMercator.FromLonLat(DefaultLon, DefaultLat);
        var mPoint = new MPoint(smc.x, smc.y);

        MainThread.BeginInvokeOnMainThread(() => {
            // 2. Cập nhật "chấm xanh" MyLocationLayer
            mapView.MyLocationLayer.UpdateMyLocation(new Mapsui.UI.Maui.Position(DefaultLat, DefaultLon));

            // 3. Zoom và di chuyển bản đồ (thêm duration để lướt cho mượt)
            mapView.Map?.Navigator.CenterOnAndZoomTo(mPoint, resolution: resolution, duration: 500);

        });
    }

    private async Task LoadCurrentLocation()
    {
        MoveMapToDefaultLocation(resolution: 2);
        await Task.CompletedTask;
    }

    private async void OnCenterMyLocationClicked(object? sender, EventArgs e)
    {
        if (sender is View view)
        {
            // 1. Hiệu ứng thị giác: Thu nhỏ rồi phóng to lại
            await view.ScaleToAsync(0.9, 100, Easing.CubicOut);
            await view.ScaleToAsync(1, 100, Easing.CubicIn);
        }

        MoveMapToDefaultLocation(resolution: 1.5);
    }

    private async Task LoadPoisOnMap()
    {
        statusLabel.Text = SmartTourGuide.Mobile.Resources.Strings.AppResources.StatusLoading;
        try
        {
            var pois = await _apiService.GetPoisAsync(_currentLanguageCode);

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
            statusLabel.Text = string.Format(SmartTourGuide.Mobile.Resources.Strings.AppResources.StatusLoaded, pois.Count);
        }
        catch (Exception ex)
        {
            statusLabel.Text = string.Format(SmartTourGuide.Mobile.Resources.Strings.AppResources.StatusError, ex.Message);
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
        _currentSelectedPoi = poi;
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

        // 3. Reset trạng thái nút Audio về mặc định
        StopAudio();

        // Cập nhật text cho nút bấm dựa theo dữ liệu
        if (poi.AudioUrls != null && poi.AudioUrls.Count > 0)
            btnPlayAudio.Text = "🔊 Nghe File Ghi Âm";
        else
            btnPlayAudio.Text = "🗣️ Đọc Tự Động (TTS)";

        // Hiện popup
        MainThread.BeginInvokeOnMainThread(() => DetailPopup.IsVisible = true);
    }

    // --- SỬA SỰ KIỆN BẤM NÚT (OnPlayAudioClicked) ---
    private async void OnPlayAudioClicked(object? sender, EventArgs e)
    {
        // 1. Nếu đang phát -> Bấm phát nữa là Dừng
        if (_isPlaying)
        {
            StopAudio();
            return;
        }

        if (_currentSelectedPoi == null) return;

        _isPlaying = true;
        btnPlayAudio.Text = "⏹️ Dừng phát"; // Đổi icon thành Stop

        try
        {
            // TRƯỜNG HỢP 1: CÓ FILE AUDIO -> PHÁT CLOUD AUDIO
            if (_currentSelectedPoi.AudioUrls != null && _currentSelectedPoi.AudioUrls.Count > 0)
            {
                // Lấy đường dẫn gốc từ API (VD: uploads\audio\nhac.mp3)
                string rawPath = _currentSelectedPoi.AudioUrls[0];

                // QUAN TRỌNG: Đổi dấu gạch chéo ngược "\" thành gạch chéo thuận "/"
                string fixPath = rawPath.Replace("\\", "/");

                // Đảm bảo không bị dư dấu / ở đầu nếu BaseApiUrl đã có
                if (fixPath.StartsWith("/")) fixPath = fixPath.Substring(1);

                // Ghép với BaseApiUrl
                string fullUrl = $"{BaseApiUrl.TrimEnd('/')}/{fixPath}";

                await PlayRemoteAudio(fullUrl);
            }
            // TRƯỜNG HỢP 2: KHÔNG CÓ FILE -> DÙNG TEXT-TO-SPEECH
            else
            {
                await SpeakDescription(_currentSelectedPoi.Description);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi", "Không thể phát âm thanh: " + ex.Message, "OK");
            StopAudio();
        }
    }

    // --- PHÁT FILE AUDIO TỪ URL ---
    private async Task PlayRemoteAudio(string url)
    {
        try
        {
            // 1. Xử lý URL (Thay thế dấu gạch chéo ngược nếu có)
            string fixedUrl = url.Replace("\\", "/");

            using var client = new HttpClient();

            // 2. Tải luồng dữ liệu từ mạng
            var networkStream = await client.GetStreamAsync(fixedUrl);

            // 3. Copy sang MemoryStream (RAM) để hỗ trợ Seeking (Sửa lỗi ảnh 1)
            var memoryStream = new MemoryStream();
            await networkStream.CopyToAsync(memoryStream);

            // QUAN TRỌNG: Phải tua băng về đầu sau khi ghi xong
            memoryStream.Position = 0;

            // 4. Tạo player từ MemoryStream
            _audioPlayer = AudioManager.Current.CreatePlayer(memoryStream);

            _audioPlayer.PlaybackEnded += (s, e) =>
            {
                _isPlaying = false;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (btnPlayAudio != null) btnPlayAudio.Text = "🔊 Nghe lại";
                });

                // Giải phóng RAM sau khi nghe xong
                memoryStream.Dispose();
            };

            _audioPlayer.Play();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi", $"Không thể phát: {ex.Message}", "OK");
            StopAudio();
        }
    }

    // --- ĐỌC TEXT-TO-SPEECH (TIẾNG VIỆT) ---
    private async Task SpeakDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Tạo token để có thể hủy (khi bấm Stop)
        _ttsCancellationToken = new CancellationTokenSource();

        // Cấu hình ngôn ngữ Tiếng Việt
        var locales = await TextToSpeech.GetLocalesAsync();
        var vnLocale = locales.FirstOrDefault(l => l.Language == "vi"); // Tìm gói tiếng Việt

        var options = new SpeechOptions
        {
            Locale = vnLocale,
            Pitch = 1.0f, // Độ cao
            Volume = 1.0f
        };

        // Đọc
        await TextToSpeech.SpeakAsync(text, options, _ttsCancellationToken.Token);

        // Sau khi đọc xong
        _isPlaying = false;
        btnPlayAudio.Text = "🗣️ Đọc lại";
    }

    // --- HÀM DỪNG TẤT CẢ ---
    private void StopAudio()
    {
        // 1. Dừng Audio Player
        if (_audioPlayer != null && _audioPlayer.IsPlaying)
        {
            _audioPlayer.Stop();
            _audioPlayer.Dispose();
            _audioPlayer = null;
        }

        // 2. Dừng TTS
        if (_ttsCancellationToken != null && !_ttsCancellationToken.IsCancellationRequested)
        {
            _ttsCancellationToken.Cancel();
            _ttsCancellationToken = null;
        }

        _isPlaying = false;

        // Reset tên nút
        if (_currentSelectedPoi?.AudioUrls?.Count > 0)
            btnPlayAudio.Text = "🔊 Nghe File Ghi Âm";
        else
            btnPlayAudio.Text = "🗣️ Đọc Tự Động (TTS)";
    }

    private void OnClosePopupClicked(object? sender, EventArgs e)
    {
        StopAudio();
        DetailPopup.IsVisible = false;
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

    // --- MỚI: Mở trang Danh sách Tour ---
    private async void OnShowToursClicked(object? sender, EventArgs e)
    {
        // Truyền chữ "this" (chính là MainPage) qua ToursPage
        await Navigation.PushModalAsync(new ToursPage(this));
    }

    // --- MỚI: Hàm được gọi từ ToursPage để vẽ lại bản đồ ---
    public async Task RenderTourOnMap(TourModel tour)
    {
        // Đợi để lấy "chìa khóa" truy cập bản đồ, nếu đang có người dùng thì sẽ đợi
        if (!await _mapLock.WaitAsync(500))
        {
            await DisplayAlertAsync("Thông báo", "Hệ thống đang xử lý tour trước, vui lòng đợi chút!", "OK");
            return;
        }

        try
        {
            // Cho UI "thở" một chút để trang Modal đóng hoàn toàn (Tránh xung đột UI)
            await Task.Delay(300);

            if (mapView?.Map == null) return;

            // Tải dữ liệu POI từ API (Làm ngoài MainThread cho mượt)
            var allPois = await _apiService.GetPoisAsync();

            // Bây giờ mới nhảy vào MainThread để vẽ
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // 1. Dọn dẹp sạch sẽ và an toàn
                mapView.Pins.Clear();

                // Xóa Layer Geofences cũ bằng cách lọc danh sách an toàn
                var layersToRemove = mapView.Map.Layers.Where(l => l.Name == "Geofences").ToList();
                foreach (var layer in layersToRemove)
                {
                    mapView.Map.Layers.Remove(layer);
                }

                // 2. Vẽ Pins
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                bool hasPoints = false;

                foreach (var poi in tour.Pois)
                {
                    var smc = SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
                    minX = Math.Min(minX, smc.x); minY = Math.Min(minY, smc.y);
                    maxX = Math.Max(maxX, smc.x); maxY = Math.Max(maxY, smc.y);
                    hasPoints = true;

                    var pin = new Pin(mapView)
                    {
                        Position = new Mapsui.UI.Maui.Position(poi.Latitude, poi.Longitude),
                        Label = poi.PoiName,
                        Color = Colors.Orange,
                        Tag = allPois.FirstOrDefault(p => p.Id == poi.PoiId)
                    };
                    mapView.Pins.Add(pin);
                }

                // 3. Zoom
                if (hasPoints)
                {
                    var padding = (maxX - minX) * 0.15;
                    var box = new MRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
                    mapView.Map.Navigator.ZoomToBox(box, duration: 500);
                }

                mapView.RefreshGraphics();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi RenderTour: {ex.Message}");
        }
        finally
        {
            // Luôn luôn phải thả "chìa khóa" ra ở block finally
            _mapLock.Release();
        }
    }

    // HÀM MỚI: Xử lý khi bấm nút "Ngôn ngữ"
    private async void OnChangeLanguageClicked(object? sender, EventArgs e)
    {
        // Hiển thị menu chọn
        string action = await DisplayActionSheetAsync("Chọn ngôn ngữ / Select Language", "Hủy/Cancel", null,
            "🇻🇳 Tiếng Việt",
            "🇺🇸 English");

        string selectedCode = _currentLanguageCode;

        if (action == "🇻🇳 Tiếng Việt") selectedCode = "vi-VN";
        else if (action == "🇺🇸 English") selectedCode = "en-US";

        // Nếu người dùng CHỌN MỘT NGÔN NGỮ KHÁC với ngôn ngữ hiện tại
        if (selectedCode != _currentLanguageCode && action != "Hủy/Cancel" && !string.IsNullOrEmpty(action))
        {
            _currentLanguageCode = selectedCode;

            // 1. Lưu vào bộ nhớ máy
            Preferences.Set("AppLanguage", _currentLanguageCode);

            // 2. Set lại Culture cho hệ thống
            SetAppLanguage(_currentLanguageCode);

            // 3. QUAN TRỌNG NHẤT: Khởi động lại trang để XAML dịch lại toàn bộ chữ
            // (Không cần gọi LoadPoisOnMap() ở đây nữa vì khi tạo MainPage mới, hàm OnAppearing sẽ tự chạy)
            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page = new MainPage();
            }
            else if (this.Window != null)
            {
                this.Window.Page = new MainPage();
            }
        }
    }
} 