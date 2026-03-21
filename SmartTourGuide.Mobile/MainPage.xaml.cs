using CommunityToolkit.Mvvm.Messaging;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Providers;
using Mapsui.Styles;
using Mapsui.Tiling;
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

    // ── AUDIO CƠ BẢN ────────────────────────────────────────────────────────
    private IAudioPlayer? _audioPlayer;
    private CancellationTokenSource? _ttsCancellationToken;
    private bool _isPlaying = false;
    private PoiModel? _currentSelectedPoi;

    // ── AUDIO QUEUE ──────────────────────────────────────────────────────────
    // Lưu "đang ở audio index mấy" cho từng POI — RAM only, reset khi tắt app
    private readonly Dictionary<int, int> _poiAudioIndex = new();
    // Token để huỷ queue khi rời POI hoặc bấm Stop
    private CancellationTokenSource? _queueCts = null;
    // Flag tạm dừng do cuộc gọi / app vào background
    private bool _isPausedByInterruption = false;

    // ── STATUS BAR ──────────────────────────────────────────────────────────────
    // Ưu tiên: 0=idle(📍nearest)  1=info(auto-revert)  2=zone-event  3=playing  4=error
    private int _statusPriority = 0;
    private CancellationTokenSource? _statusRevertCts;

    // ── VỊ TRÍ & BẢN ĐỒ ────────────────────────────────────────────────────
    private const double DefaultLat = 21.016492;
    private const double DefaultLon = 105.834132;
    private string _currentLanguageCode = "vi-VN";
    private IDispatcherTimer? _geofenceTimer;
    private List<PoiModel> _allPoisCache = new();
    private PoiModel? _currentlyPlayingGeofencePoi;
    private PoiModel? _nearestHighlightedPoi = null;
    private MauiLocation.Location _currentUserLocation =
        new MauiLocation.Location(DefaultLat, DefaultLon);

    private readonly SemaphoreSlim _mapLock = new SemaphoreSlim(1, 1);
    // Biến cờ chống spam (Re-entrancy guard)
    private bool _isCheckingGeofences = false;

    // ── TOUR ─────────────────────────────────────────────────────────────────
    // Tour đang hiển thị lộ trình (null = chế độ POI thường)
    private TourModel? _currentTour = null;

    // ════════════════════════════════════════════════════════════════════════
    //  CONSTRUCTOR
    // ════════════════════════════════════════════════════════════════════════
    public MainPage()
    {
        _currentLanguageCode = Preferences.Get("AppLanguage", "vi-VN");
        SetAppLanguage(_currentLanguageCode);

        InitializeComponent();

        // Tạm dừng / tiếp tục audio khi có cuộc gọi hoặc app vào background
        WeakReferenceMessenger.Default.Register<AppSleepMessage>(this,
            (r, m) => PauseForInterruption());
        WeakReferenceMessenger.Default.Register<AppResumeMessage>(this,
            (r, m) => ResumeFromInterruption());

        WeakReferenceMessenger.Default.Register<SelectTourMessage>(this, (r, m) =>
        {
            var tourDetail = m.Value;
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

        _geofenceLayer = new MemoryLayer { Name = "Geofences", Style = null };
        map.Layers.Add(_geofenceLayer);

        mapView.Map = map;
        Mapsui.Logging.Logger.LogDelegate = (level, message, ex) =>
            System.Diagnostics.Debug.WriteLine($"[Mapsui] {message}");

        mapView.PinClicked += OnPinClicked;
        mapView.MapClicked += OnMapClicked_SimulateWalk;
        mapView.MyLocationLayer.Enabled = true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VÒNG ĐỜI TRANG
    // ════════════════════════════════════════════════════════════════════════
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CheckPermissions();
        await PreWarmAudioAsync();
        await LoadCurrentLocation();
        await LoadPoisOnMap();

        if (_geofenceTimer == null)
        {
            _geofenceTimer = Application.Current!.Dispatcher.CreateTimer();
            _geofenceTimer.Interval = TimeSpan.FromSeconds(3);
            _geofenceTimer.Tick += (s, e) =>
            {
                CheckGeofences();
                UpdateNearestPoiHighlight();
            };
            _geofenceTimer.Start();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  NGÔN NGỮ
    // ════════════════════════════════════════════════════════════════════════
    private void SetAppLanguage(string langCode)
    {
        var culture = new CultureInfo(langCode);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        SmartTourGuide.Mobile.Resources.Strings.AppResources.Culture = culture;
    }

    private void UpdateLanguageButtonUI()
    {
        btnLanguage.Text = _currentLanguageCode == "en-US" ? "🇺🇸 English" : "🇻🇳 Tiếng Việt";
    }

    private async void OnChangeLanguageClicked(object? sender, EventArgs e)
    {
        string action = await DisplayActionSheetAsync(
            "Chọn ngôn ngữ / Select Language", "Hủy/Cancel", null,
            "🇻🇳 Tiếng Việt", "🇺🇸 English");

        string selectedCode = _currentLanguageCode;
        if (action == "🇻🇳 Tiếng Việt") selectedCode = "vi-VN";
        else if (action == "🇺🇸 English") selectedCode = "en-US";

        if (selectedCode != _currentLanguageCode &&
            action != "Hủy/Cancel" && !string.IsNullOrEmpty(action))
        {
            _currentLanguageCode = selectedCode;
            Preferences.Set("AppLanguage", _currentLanguageCode);
            SetAppLanguage(_currentLanguageCode);

            if (Application.Current?.Windows.Count > 0)
                Application.Current.Windows[0].Page = new MainPage();
            else if (this.Window != null)
                this.Window.Page = new MainPage();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BẢN ĐỒ & POI
    // ════════════════════════════════════════════════════════════════════════
    private async Task CheckPermissions()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
    }

    private void MoveMapToDefaultLocation(double resolution = 2)
    {
        var smc = SphericalMercator.FromLonLat(DefaultLon, DefaultLat);
        var mPoint = new MPoint(smc.x, smc.y);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            mapView.MyLocationLayer.UpdateMyLocation(
                new Mapsui.UI.Maui.Position(DefaultLat, DefaultLon));
            mapView.Map?.Navigator.CenterOnAndZoomTo(mPoint, resolution, duration: 500);
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
            await view.ScaleToAsync(0.9, 100, Easing.CubicOut);
            await view.ScaleToAsync(1, 100, Easing.CubicIn);
        }
        MoveMapToDefaultLocation(resolution: 1.5);
    }
    private void ClearMapLayers(params string[] layerNames)
    {
        foreach (var name in layerNames)
        {
            var layer = mapView.Map.Layers.FirstOrDefault(l => l.Name == name);
            if (layer != null) mapView.Map.Layers.Remove(layer);
        }
    }

    private async Task LoadPoisOnMap()
    {
        SetStatus(SmartTourGuide.Mobile.Resources.Strings.AppResources.StatusLoading, priority: 2, force: true);
        try
        {
            var pois = await _apiService.GetPoisAsync(_currentLanguageCode);
            _allPoisCache = pois;
            _nearestHighlightedPoi = null;
            mapView.Pins.Clear();

            ClearMapLayers("Geofences", "TourRoute");

            mapView.Map.Layers.Insert(1, CreateGeofenceLayer(pois));

            foreach (var poi in pois)
            {
                mapView.Pins.Add(new Pin(mapView)
                {
                    Position = new Mapsui.UI.Maui.Position(poi.Latitude, poi.Longitude),
                    Type = PinType.Pin,
                    Label = poi.Name,
                    Address = poi.Address,
                    Color = Microsoft.Maui.Graphics.Colors.Red,
                    Scale = 0.5f,
                    Tag = poi
                });
            }

            SetStatus(string.Format(
                SmartTourGuide.Mobile.Resources.Strings.AppResources.StatusLoaded, pois.Count),
                priority: 2, autoRevertMs: 3000, force: true);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(
                SmartTourGuide.Mobile.Resources.Strings.AppResources.StatusError, ex.Message),
                priority: 4, force: true, autoRevertMs: 4000);
        }
    }

    private async void OnReloadClicked(object? sender, EventArgs e) => await LoadPoisOnMap();

    private MemoryLayer CreateGeofenceLayer(List<PoiModel> pois)
    {
        var features = new List<IFeature>();
        foreach (var poi in pois)
        {
            double radius = poi.TriggerRadius > 0 ? poi.TriggerRadius : 50;
            var center = SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
            double radiusMapUnits = radius / Math.Cos(poi.Latitude * (Math.PI / 180));

            var coords = new List<Coordinate>();
            for (int i = 0; i <= 360; i += 10)
            {
                double angle = i * (Math.PI / 180);
                coords.Add(new Coordinate(
                    center.x + radiusMapUnits * Math.Cos(angle),
                    center.y + radiusMapUnits * Math.Sin(angle)));
            }
            if (!coords.First().Equals2D(coords.Last()))
                coords.Add(new Coordinate(coords.First()));

            var feature = new GeometryFeature(
                new NetTopologySuite.Geometries.Polygon(new LinearRing(coords.ToArray())));
            feature.Styles.Add(new VectorStyle
            {
                Fill = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(33, 150, 243, 60)),
                Outline = new Mapsui.Styles.Pen { Color = Mapsui.Styles.Color.Blue, Width = 1 }
            });
            features.Add(feature);
        }
        return new MemoryLayer { Name = "Geofences", Features = features, Style = null };
    }

    private void ShowPoiDetail(PoiModel poi)
    {
        _currentSelectedPoi = poi;
        lblPoiName.Text = poi.Name;
        lblAddress.Text = poi.Address;
        lblDescription.Text = string.IsNullOrEmpty(poi.Description) ? "Chưa có mô tả." : poi.Description;

        if (poi.ImageUrls?.Count > 0)
        {
            imgPoi.Source = ImageSource.FromUri(new Uri($"{BaseApiUrl}{poi.ImageUrls[0]}"));
            ImageContainer.IsVisible = true;
        }
        else
        {
            ImageContainer.IsVisible = false;
        }

        StopAudio();

        if (poi.AudioUrls?.Count > 0)
        {
            _poiAudioIndex.TryGetValue(poi.Id, out int idx);
            int next = (idx < poi.AudioUrls.Count) ? idx + 1 : 1;
            int total = poi.AudioUrls.Count;
            btnPlayAudio.Text = total > 1
                ? $"🔊 Nghe audio ({next}/{total})"
                : "🔊 Nghe File Ghi Âm";
        }
        else
        {
            btnPlayAudio.Text = "🗣️ Đọc Tự Động (TTS)";
        }

        MainThread.BeginInvokeOnMainThread(() => DetailPopup.IsVisible = true);
    }

    private void OnPinClicked(object? sender, PinClickedEventArgs e)
    {
        if (e.Pin?.Tag is PoiModel poi) { ShowPoiDetail(poi); e.Handled = true; }
    }

    private void OnClosePopupClicked(object? sender, EventArgs e)
    {
        StopAudio();
        DetailPopup.IsVisible = false;
    }

    private async void OnShowToursClicked(object? sender, EventArgs e)
        => await Navigation.PushModalAsync(new ToursPage(this));

    // ════════════════════════════════════════════════════════════════════════
    //  TOUR — VẼ LỘ TRÌNH TRÊN BẢN ĐỒ
    // ════════════════════════════════════════════════════════════════════════
    public async Task RenderTourOnMap(TourModel tour)
    {
        if (!await _mapLock.WaitAsync(500))
        {
            await DisplayAlertAsync("Thông báo", "Hệ thống đang xử lý tour trước, vui lòng đợi chút!", "OK");
            return;
        }
        try
        {
            _currentTour = tour;
            await Task.Delay(300);
            if (mapView?.Map == null) return;

            var allPois = await _apiService.GetPoisAsync();
            var orderedPois = tour.Pois.OrderBy(p => p.OrderIndex).ToList();
            int total = orderedPois.Count;

            // ── Gọi OSRM lấy đường đi thực tế (ngoài MainThread) ────────
            // OSRM public demo — miễn phí, không cần API key, dùng dữ liệu OSM
            List<Coordinate>? roadCoords = null;
            if (total >= 2)
            {
                SetStatus("🗺️ Đang tính lộ trình...", priority: 2, force: true);
                roadCoords = await GetRoadRouteAsync(orderedPois);
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                mapView.Pins.Clear();

                // Xóa Geofences + TourRoute layer cũ
                ClearMapLayers("Geofences", "TourRoute");

                double minX = double.MaxValue, minY = double.MaxValue,
                       maxX = double.MinValue, maxY = double.MinValue;
                bool hasPoints = false;

                // ── Pins có số thứ tự + màu phân cấp ─────────────────────
                for (int idx = 0; idx < orderedPois.Count; idx++)
                {
                    var poi = orderedPois[idx];
                    var smc = SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
                    minX = Math.Min(minX, smc.x); minY = Math.Min(minY, smc.y);
                    maxX = Math.Max(maxX, smc.x); maxY = Math.Max(maxY, smc.y);
                    hasPoints = true;

                    // 🟢 Xuất phát | 🔴 Kết thúc | 🟠 Điểm giữa
                    var pinColor = idx == 0 ? Microsoft.Maui.Graphics.Colors.Green
                                 : idx == total - 1 ? Microsoft.Maui.Graphics.Colors.OrangeRed
                                 : Microsoft.Maui.Graphics.Colors.Orange;

                    mapView.Pins.Add(new Pin(mapView)
                    {
                        Position = new Mapsui.UI.Maui.Position(poi.Latitude, poi.Longitude),
                        Label = $"{idx + 1}. {poi.PoiName}",
                        Address = $"Điểm dừng {idx + 1}/{total}",
                        Color = pinColor,
                        Scale = idx == 0 || idx == total - 1 ? 0.70f : 0.55f,
                        Tag = allPois.FirstOrDefault(p => p.Id == poi.PoiId)
                    });
                }

                // ── Vẽ lộ trình theo đường đi thực tế ────────────────────
                if (total >= 2)
                {
                    // Nếu OSRM trả về → dùng đường thực; nếu lỗi → fallback đường thẳng
                    var coords = roadCoords ?? orderedPois.Select(p =>
                    {
                        var smc = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
                        return new Coordinate(smc.x, smc.y);
                    }).ToList();

                    bool isRealRoute = roadCoords != null;

                    var lineString = new NetTopologySuite.Geometries.LineString(coords.ToArray());
                    var routeFeature = new GeometryFeature(lineString);
                    routeFeature.Styles.Add(new VectorStyle
                    {
                        Line = new Mapsui.Styles.Pen
                        {
                            // Đường thực: xanh dương đậm, nét liền
                            // Fallback (thẳng): cam đậm, nét đứt
                            Color = isRealRoute
                                         ? new Mapsui.Styles.Color(25, 118, 210, 220)   // Blue 700
                                         : new Mapsui.Styles.Color(255, 152, 0, 200), // Orange
                            Width = isRealRoute ? 4 : 3,
                            PenStyle = isRealRoute ? PenStyle.Solid : PenStyle.Dash
                        },
                        Fill = null
                    });

                    var routeLayer = new MemoryLayer
                    {
                        Name = "TourRoute",
                        Features = new List<IFeature> { routeFeature },
                        Style = null
                    };
                    mapView.Map.Layers.Insert(1, routeLayer);
                }

                // ── Zoom vừa khung tất cả POI ─────────────────────────────
                if (hasPoints)
                {
                    if (total == 1 || (minX == maxX && minY == maxY))
                        mapView.Map.Navigator.CenterOnAndZoomTo(new MPoint(minX, minY), 2, 500);
                    else
                    {
                        var padX = (maxX - minX) * 0.20;
                        var padY = (maxY - minY) * 0.20;
                        mapView.Map.Navigator.ZoomToBox(
                            new MRect(minX - padX, minY - padY, maxX + padX, maxY + padY),
                            MBoxFit.Fit, duration: 500);
                    }
                }

                mapView.RefreshGraphics();
                ShowTourInfoPanel(tour, orderedPois);
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Lỗi RenderTour: {ex.Message}"); }
        finally { _mapLock.Release(); }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  OSRM ROUTING — lấy đường đi thực tế qua các POI
    // ════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Gọi OSRM public API để lấy tuyến đường thực tế qua tất cả các POI.
    /// OSRM dùng dữ liệu OpenStreetMap — miễn phí, không cần API key.
    /// Trả về null nếu network lỗi (caller sẽ fallback về đường thẳng).
    /// </summary>
    private async Task<List<Coordinate>?> GetRoadRouteAsync(List<TourDetailModel> orderedPois)
    {
        try
        {
            // Ghép tọa độ: lon,lat;lon,lat;... (OSRM dùng lon trước lat)
            // BẮT BUỘC dùng InvariantCulture — tránh dấu phẩy thập phân theo locale device
            // vd: locale vi-VN sẽ format 21,016492 thay vì 21.016492 → OSRM trả 400
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var coords = string.Join(";",
                orderedPois.Select(p =>
                    $"{p.Longitude.ToString("F6", ic)},{p.Latitude.ToString("F6", ic)}"));

            // OSRM public demo server — driving profile, full geometry dạng polyline6
            var url = $"https://router.project-osrm.org/route/v1/driving/{coords}" +
                      "?overview=full&geometries=polyline6&continue_straight=false";

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Add("User-Agent", "SmartTourGuide/1.0");

            var response = await client.GetStringAsync(url);
            var json = System.Text.Json.JsonDocument.Parse(response);
            var root = json.RootElement;

            // Kiểm tra OSRM trả về OK
            if (root.GetProperty("code").GetString() != "Ok") return null;

            // Lấy encoded polyline của toàn bộ tuyến
            var encodedPolyline = root
                .GetProperty("routes")[0]
                .GetProperty("geometry")
                .GetString();

            if (string.IsNullOrEmpty(encodedPolyline)) return null;

            // Giải mã Polyline6 → danh sách tọa độ GPS
            var gpsPoints = DecodePolyline6(encodedPolyline);

            // Chuyển GPS → tọa độ bản đồ Mapsui (SphericalMercator)
            return gpsPoints.Select(pt =>
            {
                var smc = SphericalMercator.FromLonLat(pt.lon, pt.lat);
                return new Coordinate(smc.x, smc.y);
            }).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OSRM] Lỗi routing: {ex.Message}");
            return null; // Fallback về đường thẳng
        }
    }

    /// <summary>
    /// Giải mã Google Encoded Polyline6 (precision=6) thành list (lat, lon).
    /// OSRM dùng precision 6 thay vì 5 như Google Maps.
    /// </summary>
    private static List<(double lat, double lon)> DecodePolyline6(string encoded)
    {
        var result = new List<(double, double)>();
        int index = 0;
        int lat = 0;
        int lon = 0;

        while (index < encoded.Length)
        {
            lat += DecodePolylineChunk(encoded, ref index);
            lon += DecodePolylineChunk(encoded, ref index);
            result.Add((lat / 1e6, lon / 1e6));
        }
        return result;
    }

    private static int DecodePolylineChunk(string encoded, ref int index)
    {
        int result = 0;
        int shift = 0;
        int b;
        do
        {
            b = encoded[index++] - 63;
            result |= (b & 0x1F) << shift;
            shift += 5;
        } while (b >= 0x20);

        return (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
    }

    /// <summary>Hiện panel tóm tắt lộ trình ở góc dưới bản đồ.</summary>
    private void ShowTourInfoPanel(TourModel tour, List<TourDetailModel> orderedPois)
    {
        lblTourName.Text = tour.Name ?? "Tour";
        tourPoiList.Children.Clear();

        for (int i = 0; i < orderedPois.Count; i++)
        {
            var poi = orderedPois[i];
            bool isLast = i == orderedPois.Count - 1;
            string icon = i == 0 ? "🟢" : isLast ? "🔴" : "🟠";

            // Card từng POI dạng ngang
            var card = new Border
            {
                BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#F5F5F5"),
                StrokeThickness = 0,
                Padding = new Thickness(10, 6),
                Margin = new Thickness(0, 0, 6, 0)
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            { CornerRadius = new CornerRadius(10) };

            var stack = new VerticalStackLayout { Spacing = 2 };
            stack.Children.Add(new Label
            {
                Text = icon,
                FontSize = 14,
                HorizontalOptions = LayoutOptions.Center
            });
            stack.Children.Add(new Label
            {
                Text = $"{i + 1}. {poi.PoiName}",
                FontSize = 11,
                TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#212121"),
                MaxLines = 2,
                LineBreakMode = LineBreakMode.TailTruncation,
                WidthRequest = 80,
                HorizontalTextAlignment = TextAlignment.Center
            });
            card.Content = stack;
            tourPoiList.Children.Add(card);

            // Mũi tên nối (trừ điểm cuối)
            if (!isLast)
                tourPoiList.Children.Add(new Label
                {
                    Text = "→",
                    FontSize = 14,
                    TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#BDBDBD"),
                    VerticalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
        }

        TourInfoPanel.IsVisible = true;
    }

    /// <summary>Đóng TourInfoPanel và xóa route layer khỏi bản đồ.</summary>
    private async void OnCloseTourPanelClicked(object? sender, EventArgs e)
    {
        TourInfoPanel.IsVisible = false;
        _currentTour = null;

        // LoadPoisOnMap vẽ lại toàn bộ POI + Geofence circles
        await LoadPoisOnMap();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  NÚT PHÁT / DỪNG
    // ════════════════════════════════════════════════════════════════════════
    private async void OnPlayAudioClicked(object? sender, EventArgs e)
    {
        if (_isPlaying) { StopAudio(); return; }
        if (_currentSelectedPoi == null) return;

        _isPlaying = true;
        btnPlayAudio.Text = "⏹️ Dừng phát";

        _queueCts?.Cancel();
        _queueCts = new CancellationTokenSource();

        try
        {
            await PlayAudioQueueAsync(_currentSelectedPoi, _queueCts.Token);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi", "Không thể phát âm thanh: " + ex.Message, "OK");
            StopAudio();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  AUDIO QUEUE ENGINE
    // ════════════════════════════════════════════════════════════════════════
    private async Task PlayAudioQueueAsync(PoiModel poi, CancellationToken ct)
    {
        var urls = poi.AudioUrls;

        if (urls == null || urls.Count == 0)
        {
            await SpeakDescription(poi.Description);
            return;
        }

        if (!_poiAudioIndex.TryGetValue(poi.Id, out int startIndex) || startIndex >= urls.Count)
            startIndex = 0;

        for (int i = startIndex; i < urls.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                _poiAudioIndex[poi.Id] = i;
                return;
            }

            int displayIdx = i + 1;
            int total = urls.Count;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SetStatus($"🎵 {poi.Name}  ·  {displayIdx}/{total}", priority: 3);
                if (btnPlayAudio != null && total > 1)
                    btnPlayAudio.Text = $"⏹️ Dừng  ({displayIdx}/{total})";
            });

            string rawPath = urls[i].Replace("\\", "/").TrimStart('/');
            string fullUrl = $"{BaseApiUrl.TrimEnd('/')}/{rawPath}";

            try
            {
                await PlayRemoteAudioAndWaitAsync(fullUrl, ct);
            }
            catch (OperationCanceledException)
            {
                int nextIdx = i + 1;
                _poiAudioIndex[poi.Id] = nextIdx < urls.Count ? nextIdx : 0;
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioQueue] Lỗi audio {i}: {ex.Message}");
            }
        }

        // Phát hết toàn bộ → reset về 0
        _poiAudioIndex[poi.Id] = 0;
        _isPlaying = false;
        int played = urls.Count;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SetStatus($"✅ {poi.Name}  ·  Phát xong {played} audio", priority: 1, autoRevertMs: 3000);
            if (btnPlayAudio != null) btnPlayAudio.Text = "🔊 Nghe lại";
        });
    }

    private async Task PlayRemoteAudioAndWaitAsync(string url, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        string fixedUrl = url.Replace("\\", "/");
        string? localPath = await GetLocalAudioPathAsync(fixedUrl);

        Stream audioStream;
        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
        {
            audioStream = File.OpenRead(localPath);
        }
        else
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var networkStream = await client.GetStreamAsync(fixedUrl, ct);
            var mem = new MemoryStream();
            await networkStream.CopyToAsync(mem, ct);
            mem.Position = 0;
            audioStream = mem;
        }

        _audioPlayer?.Stop();
        _audioPlayer?.Dispose();

        var currentPlayer = AudioManager.Current.CreatePlayer(audioStream);
        _audioPlayer = currentPlayer;

        currentPlayer.PlaybackEnded += (s, e) =>
        {
            audioStream.Dispose();
            tcs.TrySetResult(true);
        };

        using var reg = ct.Register(() =>
        {
            try { currentPlayer?.Stop(); } catch (Exception) { }
            try { audioStream?.Dispose(); } catch (Exception) { }
            tcs.TrySetCanceled();
        });

        currentPlayer.Volume = 0;
        currentPlayer.Play();
        _ = FadeInVolumeAsync(currentPlayer);

        await tcs.Task;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TẠMM DỪNG / TIẾP TỤC KHI CÓ CUỘC GỌI / APP VÀO BACKGROUND
    // ════════════════════════════════════════════════════════════════════════
    private void PauseForInterruption()
    {
        if (_isPlaying && _audioPlayer != null && !_isPausedByInterruption)
        {
            _audioPlayer.Pause();
            _isPausedByInterruption = true;
        }
    }

    private void ResumeFromInterruption()
    {
        if (_isPausedByInterruption && _audioPlayer != null)
        {
            _audioPlayer.Play();
            _isPausedByInterruption = false;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PRE-WARM AUDIO PIPELINE
    // ════════════════════════════════════════════════════════════════════════
    private async Task PreWarmAudioAsync()
    {
        try
        {
            var silenceBytes = CreateSilenceWav(durationMs: 200);
            var silenceStream = new MemoryStream(silenceBytes);
            var warmupPlayer = AudioManager.Current.CreatePlayer(silenceStream);
            warmupPlayer.Volume = 0;
            warmupPlayer.Play();
            await Task.Delay(250);
            warmupPlayer.Stop();
            warmupPlayer.Dispose();
            silenceStream.Dispose();
        }
        catch { }
    }

    private static byte[] CreateSilenceWav(int durationMs = 200)
    {
        const int sampleRate = 22050;
        const int channels = 1;
        const int bitsPerSample = 16;
        int numSamples = (sampleRate * durationMs) / 1000;
        int dataSize = numSamples * channels * (bitsPerSample / 8);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write((short)bitsPerSample);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        return ms.ToArray();
    }

    private async Task FadeInVolumeAsync(IAudioPlayer player)
    {
        const int steps = 15;
        const int intervalMs = 10;
        const double target = 1.0;

        for (int i = 1; i <= steps; i++)
        {
            await Task.Delay(intervalMs);
            if (player != _audioPlayer || !_isPlaying) break;
            player.Volume = target * i / steps;
        }
        if (player == _audioPlayer && _isPlaying) player.Volume = target;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CACHE AUDIO
    // ════════════════════════════════════════════════════════════════════════
    private async Task<string?> GetLocalAudioPathAsync(string url)
    {
        try
        {
            string fileName = Path.GetFileName(url.Split('?')[0]);
            string localPath = Path.Combine(FileSystem.CacheDirectory, fileName);
            if (File.Exists(localPath)) return localPath;

            using var client = new HttpClient();
            var data = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, data);
            return localPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cache] Lỗi: {ex.Message}");
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TEXT-TO-SPEECH
    // ════════════════════════════════════════════════════════════════════════
    private async Task SpeakDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _ttsCancellationToken = new CancellationTokenSource();
        var locales = await TextToSpeech.GetLocalesAsync();
        var vnLocale = locales.FirstOrDefault(l => l.Language == "vi");

        await TextToSpeech.SpeakAsync(text, new SpeechOptions
        {
            Locale = vnLocale,
            Pitch = 1.0f,
            Volume = 1.0f
        }, _ttsCancellationToken.Token);

        _isPlaying = false;
        btnPlayAudio.Text = "🗣️ Đọc lại";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  DỪNG TẤT CẢ
    // ════════════════════════════════════════════════════════════════════════
    private void StopAudio()
    {
        _queueCts?.Cancel();
        _queueCts = null;
        _isPausedByInterruption = false;
        _statusRevertCts?.Cancel();
        _statusRevertCts = null;
        _statusPriority = 0;

        if (_audioPlayer != null)
        {
            if (_audioPlayer.IsPlaying) _audioPlayer.Stop();
            _audioPlayer.Dispose();
            _audioPlayer = null;
        }

        if (_ttsCancellationToken != null && !_ttsCancellationToken.IsCancellationRequested)
        {
            _ttsCancellationToken.Cancel();
            _ttsCancellationToken = null;
        }

        _isPlaying = false;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_currentSelectedPoi?.AudioUrls?.Count > 0)
            {
                _poiAudioIndex.TryGetValue(_currentSelectedPoi.Id, out int idx);
                int next = (idx < _currentSelectedPoi.AudioUrls.Count) ? idx + 1 : 1;
                int total = _currentSelectedPoi.AudioUrls.Count;
                btnPlayAudio.Text = total > 1
                    ? $"🔊 Nghe audio ({next}/{total})"
                    : "🔊 Nghe File Ghi Âm";
            }
            else
            {
                btnPlayAudio.Text = "🗣️ Đọc Tự Động (TTS)";
            }
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  STATUS BAR MANAGER
    // ════════════════════════════════════════════════════════════════════════
    private void SetStatus(string text, int priority, int autoRevertMs = 0, bool force = false)
    {
        if (!force && priority < _statusPriority) return;

        _statusRevertCts?.Cancel();
        _statusRevertCts = null;
        _statusPriority = priority;

        MainThread.BeginInvokeOnMainThread(() => statusLabel.Text = text);

        if (autoRevertMs > 0)
        {
            var cts = new CancellationTokenSource();
            _statusRevertCts = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(autoRevertMs, cts.Token);
                    _statusPriority = 0;
                    MainThread.BeginInvokeOnMainThread(ShowIdleStatus);
                }
                catch (OperationCanceledException) { }
            });
        }
    }

    private void ShowIdleStatus()
    {
        if (_isPlaying || _nearestHighlightedPoi == null) return;
        var poiLoc = new MauiLocation.Location(
            _nearestHighlightedPoi.Latitude, _nearestHighlightedPoi.Longitude);
        double dist = MauiLocation.Location.CalculateDistance(
            _currentUserLocation, poiLoc, DistanceUnits.Kilometers) * 1000;
        string distText = dist >= 1000 ? $"{dist / 1000:F1} km" : $"{dist:F0} m";
        statusLabel.Text = $"📍 Gần nhất: {_nearestHighlightedPoi.Name} · {distText}";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GEOFENCE ENGINE
    // ════════════════════════════════════════════════════════════════════════
    private async void CheckGeofences()
    {
        try
        {
            if (_isCheckingGeofences || _allPoisCache.Count == 0) return;
            _isCheckingGeofences = true;

            var poisInRange = new List<PoiModel>();
            foreach (var poi in _allPoisCache)
            {
                var poiLoc = new MauiLocation.Location(poi.Latitude, poi.Longitude);
                double dist = MauiLocation.Location.CalculateDistance(
                    _currentUserLocation, poiLoc, DistanceUnits.Kilometers) * 1000;
                double radius = poi.TriggerRadius > 0 ? poi.TriggerRadius : 50;
                if (dist <= radius) poisInRange.Add(poi);
            }

            // Kịch bản A: Ra khỏi tất cả vùng
            if (poisInRange.Count == 0)
            {
                if (_currentlyPlayingGeofencePoi != null)
                {
                    var leftPoiName = _currentlyPlayingGeofencePoi.Name;
                    StopAudio();
                    _currentlyPlayingGeofencePoi = null;
                    SetStatus($"👋 Rời vùng: {leftPoiName}", priority: 2, autoRevertMs: 2000);
                }
                return;
            }

            // Kịch bản C: Ra khỏi vùng đang phát, còn vùng khác
            if (_currentlyPlayingGeofencePoi != null)
            {
                bool stillInZone = poisInRange.Any(p => p.Id == _currentlyPlayingGeofencePoi.Id);
                if (!stillInZone)
                {
                    StopAudio();
                    _currentlyPlayingGeofencePoi = null;
                    await Task.Delay(300);
                }
            }

            var highestPri = poisInRange.OrderByDescending(p => p.Priority).First();

            // Kịch bản B: Bật / đổi nhạc
            if (_currentlyPlayingGeofencePoi == null)
            {
                _currentlyPlayingGeofencePoi = highestPri;
                TriggerAutoAudio(highestPri);
            }
            else if (_currentlyPlayingGeofencePoi.Id != highestPri.Id)
            {
                if (highestPri.Priority > _currentlyPlayingGeofencePoi.Priority || !_isPlaying)
                {
                    StopAudio();
                    await Task.Delay(300);
                    _currentlyPlayingGeofencePoi = highestPri;
                    TriggerAutoAudio(highestPri);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi Geofence: {ex.Message}");
        }
        finally
        {
            _isCheckingGeofences = false;
        }
    }

    private void TriggerAutoAudio(PoiModel poi)
    {
        _queueCts?.Cancel();
        _queueCts = new CancellationTokenSource();

        _currentSelectedPoi = poi;
        _isPlaying = true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (btnPlayAudio != null) btnPlayAudio.Text = "⏹️ Dừng phát";
        });

        _ = PlayAudioQueueAsync(poi, _queueCts.Token).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                var ex = t.Exception?.GetBaseException();
                System.Diagnostics.Debug.WriteLine($"[TriggerAudio] Lỗi phát ngầm: {ex?.Message}");

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (btnPlayAudio != null)
                        btnPlayAudio.Text = poi.AudioUrls?.Count > 0 ? "🔊 Nghe File Ghi Âm" : "🗣️ Đọc Tự Động (TTS)";
                    SetStatus("Lỗi phát âm thanh tự động", priority: 2, autoRevertMs: 3000);
                });
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HIGHLIGHT POI GẦN NHẤT
    // ════════════════════════════════════════════════════════════════════════
    private void UpdateNearestPoiHighlight()
    {
        if (_allPoisCache.Count == 0 || mapView?.Pins == null || mapView.Pins.Count == 0) return;

        // Khi đang hiển thị Tour, không highlight nearest — tránh đổi màu pin tour
        if (_currentTour != null) return;

        PoiModel? nearestPoi = null;
        double minDistanceM = double.MaxValue;

        foreach (var poi in _allPoisCache)
        {
            var poiLoc = new MauiLocation.Location(poi.Latitude, poi.Longitude);
            double dist = MauiLocation.Location.CalculateDistance(
                _currentUserLocation, poiLoc, DistanceUnits.Kilometers) * 1000;
            if (dist < minDistanceM) { minDistanceM = dist; nearestPoi = poi; }
        }

        if (nearestPoi == null) return;

        var distanceText = minDistanceM >= 1000
            ? $"{minDistanceM / 1000:F1} km"
            : $"{minDistanceM:F0} m";

        SetStatus($"📍 Gần nhất: {nearestPoi.Name} · {distanceText}", priority: 0);

        if (_nearestHighlightedPoi?.Id != nearestPoi.Id)
        {
            _nearestHighlightedPoi = nearestPoi;
            var capturedNearest = nearestPoi;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var pin in mapView.Pins)
                {
                    bool isNearest = pin.Tag is PoiModel p && p.Id == capturedNearest.Id;
                    pin.Color = isNearest
                        ? Microsoft.Maui.Graphics.Colors.DeepSkyBlue
                        : Microsoft.Maui.Graphics.Colors.Red;
                    pin.Scale = isNearest ? 0.85f : 0.5f;
                }
                mapView.RefreshGraphics();
            });
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GIẢ LẬP ĐI BỘ (DEV)
    // ════════════════════════════════════════════════════════════════════════
    private void OnMapClicked_SimulateWalk(object? sender, MapClickedEventArgs e)
    {
        _currentUserLocation = new MauiLocation.Location(e.Point.Latitude, e.Point.Longitude);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            mapView.MyLocationLayer.UpdateMyLocation(
                new Mapsui.UI.Maui.Position(e.Point.Latitude, e.Point.Longitude));
            mapView.RefreshGraphics();
        });

        UpdateNearestPoiHighlight();

        if (!_isCheckingGeofences) CheckGeofences();

        e.Handled = true;
    }
}