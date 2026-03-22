namespace SmartTourGuide.Mobile;

public partial class MainPage
{
    // LoadPoisOnMap, CreateGeofenceLayer, ShowPoiDetail
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
    // RenderTourOnMap, GetRoadRouteAsync, DecodePolyline6
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
    // ShowTourInfoPanel, OnCloseTourPanelClicked
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
    // OnMapClicked_SimulateWalk
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