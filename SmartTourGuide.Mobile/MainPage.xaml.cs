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
using MauiLocation = Microsoft.Maui.Devices.Sensors;
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
    // biến cho phần geofence
    private IDispatcherTimer? _geofenceTimer; // Bộ đếm giờ chạy ngầm
    private List<PoiModel> _allPoisCache = new(); // Lưu cache danh sách địa điểm
    private PoiModel? _currentlyPlayingGeofencePoi; // Lưu địa điểm ĐANG PHÁT audio
    private PoiModel? _nearestHighlightedPoi = null; // POI gần nhất đang được highlight trên bản đồ
    private MauiLocation.Location _currentUserLocation = new MauiLocation.Location(DefaultLat, DefaultLon);// Vị trí hiện tại

    private readonly SemaphoreSlim _mapLock = new SemaphoreSlim(1, 1);
    public MainPage()
    {
        // Đọc và gán ngôn ngữ TRƯỚC KHI vẽ XAML
        _currentLanguageCode = Preferences.Get("AppLanguage", "vi-VN");
        SetAppLanguage(_currentLanguageCode);

        // VẼ GIAO DIỆN 
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
        mapView.MapClicked += OnMapClicked_SimulateWalk;
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
            culture = new CultureInfo("vi-VN"); // Khởi tạo culture trung lập
        }
        else
        {
            culture = new CultureInfo(langCode);
        }

        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        SmartTourGuide.Mobile.Resources.Strings.AppResources.Culture = culture;
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

        if (_geofenceTimer == null)
        {
            _geofenceTimer = Application.Current!.Dispatcher.CreateTimer();
            _geofenceTimer.Interval = TimeSpan.FromSeconds(3); // Cứ 3s quét 1 lần
            _geofenceTimer.Tick += (s, e) =>
            {
                CheckGeofences();
                UpdateNearestPoiHighlight();
            };
            _geofenceTimer.Start();
        }
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
            _allPoisCache = pois;
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
            double radius = poi.TriggerRadius > 0 ? poi.TriggerRadius : 50;
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
    // HÀM HỖ TRỢ: Tải file audio về máy và trả về đường dẫn local (Nếu có lỗi sẽ trả về null để dùng Stream trực tiếp)
    private async Task<string?> GetLocalAudioPathAsync(string url)
    {
        try
        {
            // 1. Tạo tên file duy nhất dựa trên URL (loại bỏ ký tự đặc biệt)
            string fileName = Path.GetFileName(url.Split('?')[0]);
            string localPath = Path.Combine(FileSystem.CacheDirectory, fileName);

            // 2. Nếu file đã tồn tại trên máy, trả về đường dẫn luôn
            if (File.Exists(localPath))
            {
                return localPath;
            }

            // 3. Nếu chưa có, tiến hành tải về
            using var client = new HttpClient();
            var data = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, data);

            return localPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi tải file: {ex.Message}");
            return null; // Trả về null nếu tải lỗi để quay lại dùng Stream trực tiếp
        }
    }

    // --- PHÁT FILE AUDIO TỪ URL ---
    private async Task PlayRemoteAudio(string url)
    {
        try
        {
            // 1. Sửa URL cho chuẩn (thay \ thành /)
            string fixedUrl = url.Replace("\\", "/");

            // 2. GỌI HÀM CACHE: Kiểm tra xem file đã tải về máy chưa
            string? localPath = await GetLocalAudioPathAsync(fixedUrl);

            Stream audioStream;

            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            {
                // Trường hợp A: Đã có file trong máy -> Mở trực tiếp (Cực nhanh, không tốn data)
                audioStream = File.OpenRead(localPath);
                System.Diagnostics.Debug.WriteLine($"---> Đang phát từ CACHE: {localPath}");
            }
            else
            {
                // Trường hợp B: Chưa có hoặc lỗi tải -> Tải tạm vào RAM (như cũ)
                using var client = new HttpClient();
                var networkStream = await client.GetStreamAsync(fixedUrl);
                var memoryStream = new MemoryStream();
                await networkStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                audioStream = memoryStream;
                System.Diagnostics.Debug.WriteLine("---> Đang phát từ MẠNG (Download trực tiếp)");
            }

            // 3. Khởi tạo và phát
            if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
                _audioPlayer.Dispose();
            }

            _audioPlayer = AudioManager.Current.CreatePlayer(audioStream);

            _audioPlayer.PlaybackEnded += (s, e) =>
            {
                _isPlaying = false;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (btnPlayAudio != null) btnPlayAudio.Text = "🔊 Nghe lại";
                });
                audioStream.Dispose(); // Giải phóng stream khi kết thúc
            };

            _audioPlayer.Play();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi", $"Không thể phát audio: {ex.Message}", "OK");
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
                    // KIỂM TRA: Nếu chỉ có 1 điểm, hoặc các điểm trùng tọa độ nhau 100%
                    if (tour.Pois.Count == 1 || (minX == maxX && minY == maxY))
                    {
                        // Dùng lệnh đưa về tâm (Resolution: 2 là zoom khá gần)
                        var centerPoint = new MPoint(minX, minY);
                        mapView.Map.Navigator.CenterOnAndZoomTo(centerPoint, resolution: 2, duration: 500);
                    }
                    else
                    {
                        // Dùng lệnh đóng khung cho nhiều điểm
                        var paddingX = (maxX - minX) * 0.15;
                        var paddingY = (maxY - minY) * 0.15;
                        var box = new MRect(minX - paddingX, minY - paddingY, maxX + paddingX, maxY + paddingY);
                        mapView.Map.Navigator.ZoomToBox(box, duration: 500);
                    }
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
    private async void CheckGeofences()
    {
        if (_allPoisCache.Count == 0) return;

        // 1. Tìm TẤT CẢ các vùng mà User đang đứng bên trong
        var poisInRange = new List<PoiModel>();
        foreach (var poi in _allPoisCache)
        {
            var poiLocation = new MauiLocation.Location(poi.Latitude, poi.Longitude);

            // Tính khoảng cách (Kilometer -> đổi ra mét)
            double distanceInMeters = MauiLocation.Location.CalculateDistance(_currentUserLocation, poiLocation, DistanceUnits.Kilometers) * 1000;

            if (distanceInMeters <= poi.TriggerRadius)
            {
                poisInRange.Add(poi);
            }
        }

        // 2. KỊCH BẢN A: Người dùng đã đi ra khỏi TẤT CẢ các vùng
        if (poisInRange.Count == 0)
        {
            if (_currentlyPlayingGeofencePoi != null)
            {
                // Đi ra khỏi vùng -> Tắt nhạc
                StopAudio();
                _currentlyPlayingGeofencePoi = null;
                MainThread.BeginInvokeOnMainThread(() => statusLabel.Text = "Đã rời khỏi vùng tham quan.");
            }
            return;
        }

        // 3. KỊCH BẢN C (MỚI THÊM): Người dùng có vừa bước ra khỏi vùng ĐANG PHÁT không?
        if (_currentlyPlayingGeofencePoi != null)
        {
            // Kiểm tra xem danh sách các vùng đang đứng có chứa vùng đang phát hay không
            bool isStillInActiveZone = poisInRange.Any(p => p.Id == _currentlyPlayingGeofencePoi.Id);

            if (!isStillInActiveZone)
            {
                // Đã đi ra khỏi phạm vi của địa điểm đang phát -> Tắt nhạc ngay lập tức!
                Console.WriteLine($"Đã ra khỏi phạm vi của: {_currentlyPlayingGeofencePoi.Name}");
                StopAudio();
                _currentlyPlayingGeofencePoi = null;

                // LƯU Ý: Không dùng lệnh "return;" ở đây. 
                // Vì user có thể vừa bước ra khỏi vùng A, nhưng lại đang nằm trong vùng B.
                // Ta để code chạy tiếp xuống dưới để nó tự động phát audio của vùng B.
            }
        }

        // 4. Lọc ra địa điểm có ƯU TIÊN CAO NHẤT trong số các vùng đang đứng hiện tại
        var highestPriorityPoi = poisInRange.OrderByDescending(p => p.Priority).First();

        // 5. KỊCH BẢN B: Xử lý luật phát âm thanh
        if (_currentlyPlayingGeofencePoi == null)
        {
            // 5.1. Đang không có gì phát -> Phát ngay vùng ưu tiên cao nhất vừa vào
            _currentlyPlayingGeofencePoi = highestPriorityPoi;
            await TriggerAutoAudio(highestPriorityPoi);
        }
        else if (_currentlyPlayingGeofencePoi.Id != highestPriorityPoi.Id)
        {
            // 5.2. Đang phát vùng A, nhưng lại đi vào vùng B (Vẫn đang đứng trong A)

            // LUẬT: Nếu vùng mới (B) có ưu tiên CAO HƠN vùng đang phát (A)
            if (highestPriorityPoi.Priority > _currentlyPlayingGeofencePoi.Priority)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    statusLabel.Text = $"Chuyển sang: {highestPriorityPoi.Name} (Ưu tiên {highestPriorityPoi.Priority})");

                StopAudio(); // Tắt vùng cũ
                _currentlyPlayingGeofencePoi = highestPriorityPoi;
                await TriggerAutoAudio(highestPriorityPoi); // Phát vùng mới
            }
            else
            {
                // LUẬT: Vùng mới (B) bằng hoặc thấp hơn vùng (A)
                // -> Tiếp tục phát vùng A (Ai đến trước phục vụ trước).
                Console.WriteLine($"Đang ở trong {highestPriorityPoi.Name} nhưng ưu tiên thấp hơn/bằng -> Bỏ qua.");
            }
        }
    }

    // Hàm phụ để gọi phát âm thanh tự động (tái sử dụng logic của nút Play)
    private async Task TriggerAutoAudio(PoiModel poi)
    {
        MainThread.BeginInvokeOnMainThread(() =>
            statusLabel.Text = $"Vào vùng: {poi.Name} - Đang phát tự động!");

        _currentSelectedPoi = poi;
        _isPlaying = true;

        if (btnPlayAudio != null)
            MainThread.BeginInvokeOnMainThread(() => btnPlayAudio.Text = "⏹️ Dừng phát");

        if (poi.AudioUrls != null && poi.AudioUrls.Count > 0)
        {
            string rawPath = poi.AudioUrls[0].Replace("\\", "/").TrimStart('/');
            string fullUrl = $"{BaseApiUrl.TrimEnd('/')}/{rawPath}";
            await PlayRemoteAudio(fullUrl);
        }
        else
        {
            await SpeakDescription(poi.Description);
        }
    }

    // ── HIGHLIGHT POI GẦN NHẤT ──────────────────────────────────────────────
    private void UpdateNearestPoiHighlight()
    {
        if (_allPoisCache.Count == 0 || mapView?.Pins == null || mapView.Pins.Count == 0) return;

        // 1. Duyệt tất cả POI, tính khoảng cách, tìm cái gần nhất
        PoiModel? nearestPoi = null;
        double minDistanceM = double.MaxValue;

        foreach (var poi in _allPoisCache)
        {
            var poiLocation = new MauiLocation.Location(poi.Latitude, poi.Longitude);
            double distanceM = MauiLocation.Location.CalculateDistance(
                _currentUserLocation, poiLocation, DistanceUnits.Kilometers) * 1000;

            if (distanceM < minDistanceM)
            {
                minDistanceM = distanceM;
                nearestPoi = poi;
            }
        }

        if (nearestPoi == null) return;

        // 2. Không làm gì nếu kết quả không đổi → tránh refresh bản đồ thừa
        if (_nearestHighlightedPoi?.Id == nearestPoi.Id) return;

        _nearestHighlightedPoi = nearestPoi;

        // 3. Snapshot để dùng trong lambda (tránh closure bắt biến thay đổi)
        var capturedNearest = nearestPoi;
        var capturedDistance = minDistanceM;
        var distanceText = capturedDistance >= 1000
            ? $"{capturedDistance / 1000:F1} km"
            : $"{capturedDistance:F0} m";

        // 4. Cập nhật màu + kích thước pin trên UI Thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var pin in mapView.Pins)
            {
                bool isNearest = pin.Tag is PoiModel p && p.Id == capturedNearest.Id;

                if (isNearest)
                {
                    // POI gần nhất: xanh lam, to hơn
                    pin.Color = Microsoft.Maui.Graphics.Colors.DeepSkyBlue;
                    pin.Scale = 0.85f;
                }
                else
                {
                    // Các POI khác: về mặc định
                    pin.Color = Microsoft.Maui.Graphics.Colors.Red;
                    pin.Scale = 0.5f;
                }
            }

            mapView.RefreshGraphics();

            // Cập nhật label (chỉ khi không có audio đang phát để không đè thông báo geofence)
            if (!_isPlaying)
                statusLabel.Text = $"📍 Gần nhất: {capturedNearest.Name} · {distanceText}";
        });
    }

    // HÀM GIẢ LẬP ĐI BỘ
    private void OnMapClicked_SimulateWalk(object? sender, MapClickedEventArgs e)
    {
        // Chuyển đổi tọa độ bản đồ sang tọa độ GPS Lat/Lon
        double lat = e.Point.Latitude;
        double lon = e.Point.Longitude;

        // Cập nhật vị trí GPS giả lập
        _currentUserLocation = new MauiLocation.Location(lat, lon);

        // Cập nhật cái chấm xanh dương MyLocation trên bản đồ
        MainThread.BeginInvokeOnMainThread(() => {
            mapView.MyLocationLayer.UpdateMyLocation(new Mapsui.UI.Maui.Position(lat, lon));
            mapView.RefreshGraphics();
        });

        // Cập nhật highlight POI gần nhất ngay lập tức (không chờ timer 3s)
        UpdateNearestPoiHighlight();

        // Đánh dấu sự kiện đã xử lý
        e.Handled = true;
    }
} 