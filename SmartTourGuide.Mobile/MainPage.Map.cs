namespace SmartTourGuide.Mobile;

// File này xử lý hiển thị bản đồ: tải POI, geofence, 
//vẽ tour và cập nhật panel chi tiết.

// MainPage được chia thành nhiều file partial để dễ quản lý 
//theo tính năng:
public partial class MainPage
{
    // Dịch vụ route để lấy tuyến đường đi bộ từ user đến các POI còn lại trong tour.
    private readonly RouteService _routeService = new RouteService();

    // Gom các control cần truy cập từ code-behind (tránh null-check lặp lại nhiều nơi).
    private Mapsui.UI.Maui.MapView? MapViewCtrl => this.FindByName<Mapsui.UI.Maui.MapView>("mapView");
    private Label? LblPoiNameCtrl => this.FindByName<Label>("lblPoiName");
    private Label? LblAddressCtrl => this.FindByName<Label>("lblAddress");
    // Các control trong popup chi tiết POI.
    private Label? LblDescriptionCtrl => this.FindByName<Label>("lblDescription");
    private Button? BtnPlayAudioCtrl => this.FindByName<Button>("btnPlayAudio");
    private Border? DetailPopupCtrl => this.FindByName<Border>("DetailPopup");
    private Label? LblTourNameCtrl => this.FindByName<Label>("lblTourName");
    private HorizontalStackLayout? TourPoiListCtrl => this.FindByName<HorizontalStackLayout>("tourPoiList");
    private Border? TourInfoPanelCtrl => this.FindByName<Border>("TourInfoPanel");
    private Entry? PoiSearchBarCtrl => this.FindByName<Entry>("poiSearchBar");
    private CollectionView? PoiSearchSuggestionsCtrl => this.FindByName<CollectionView>("poiSearchSuggestions");
    private Border? SearchSuggestionsPanelCtrl => this.FindByName<Border>("SearchSuggestionsPanel");

    // ════════════════════════════════════════════════════════════════════════
    //  TẢI POI LÊN BẢN ĐỒ
    // ════════════════════════════════════════════════════════════════════════
    // Tải POI từ API và hiển thị trên bản đồ với geofence.
    private async Task LoadPoisOnMap()
    {
        // 1) Báo trạng thái đang tải để người dùng biết app đang xử lý.
        SetStatus(AppRes.StatusLoading, priority: 2, force: true);
        try
        {
            // 2) Lấy POI theo ngôn ngữ hiện tại và cập nhật cache.
            var pois = await _apiService.GetPoisAsync(_currentLanguageCode);
            _allPoisCache = pois;
            _nearestHighlightedPoi = null;

            // 3) Làm sạch ghim cũ trước khi vẽ dữ liệu mới.
            var mapView = MapViewCtrl;
            if (mapView == null) return;
            mapView.Pins.Clear();

            // 4) Vẽ lại lớp geofence theo danh sách POI mới.
            ClearMapLayers("Geofences");
            mapView.Map.Layers.Insert(1, CreateGeofenceLayer(pois));

            // 5) Vẽ ghim POI lên bản đồ.
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

            // 6) Báo tải xong và đồng bộ gợi ý tìm kiếm POI.
            SetStatus(string.Format(AppRes.StatusLoaded, pois.Count),
                priority: 2, autoRevertMs: 3000, force: true);

            MainThread.BeginInvokeOnMainThread(() => UpdatePoiSearchSuggestions(PoiSearchBarCtrl?.Text));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(AppRes.StatusError, ex.Message),
                priority: 4, force: true, autoRevertMs: 4000);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LỚP GEOFENCE
    // ════════════════════════════════════════════════════════════════════════
    private MemoryLayer CreateGeofenceLayer(List<PoiModel> pois)
    {
        // 1) Mỗi POI tạo một polygon hình tròn giả lập geofence.
        var features = new List<IFeature>();
        foreach (var poi in pois)
        {
            // Ưu tiên bán kính từ dữ liệu POI, thiếu thì fallback 50m.
            double radius = poi.TriggerRadius > 0 ? poi.TriggerRadius : 50;
            var center = SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
            double radiusMapUnits = radius / Math.Cos(poi.Latitude * (Math.PI / 180));

            // 2) Sinh các điểm biên theo góc để tạo vòng tròn.
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

            // 3) Gắn style hiển thị geofence và đưa vào layer.
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

    // ════════════════════════════════════════════════════════════════════════
    //  LỚP TUYẾN ĐƯỜNG
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Vẽ polyline từ danh sách tọa độ chi tiết (OSRM hoặc cache).
    /// Dùng 2 lớp: viền trắng và đường chính để tăng độ tương phản.
    /// </summary>
    private MemoryLayer CreateTourRouteLayer(
        IReadOnlyList<MauiLocation.Location> routePoints,
        RouteSource source)
    {
        var routeCoords = new List<Coordinate>(routePoints.Count);
        foreach (var pt in routePoints)
        {
            var m = SphericalMercator.FromLonLat(pt.Longitude, pt.Latitude);
            routeCoords.Add(new Coordinate(m.x, m.y));
        }

        var features = new List<IFeature>();

        if (routeCoords.Count >= 2)
        {
            var line = new LineString(routeCoords.ToArray());

            // Viền trắng giúp tuyến dễ nhìn trên nhiều nền bản đồ.
            var shadow = new GeometryFeature(line);
            shadow.Styles.Add(new VectorStyle
            {
                Line = new Mapsui.Styles.Pen
                {
                    Color = new Mapsui.Styles.Color(255, 255, 255, 210),
                    Width = 10
                }
            });
            features.Add(shadow);

            // Màu tuyến phản ánh nguồn dữ liệu route.
            var lineColor = source == RouteSource.StraightLine
                ? new Mapsui.Styles.Color(158, 158, 158, 200) // xám cho đường thẳng
                : source == RouteSource.CacheFallback
                    ? new Mapsui.Styles.Color(66, 133, 244, 180) // xanh nhạt cho cache cũ
                    : new Mapsui.Styles.Color(26, 115, 232, 230); // #1A73E8 – xanh Google

            var route = new GeometryFeature(line);
            route.Styles.Add(new VectorStyle
            {
                Line = new Mapsui.Styles.Pen { Color = lineColor, Width = 6 }
            });
            features.Add(route);
        }

        return new MemoryLayer { Name = "TourRoute", Features = features, Style = null };
    }

    private void ShowPoiDetail(PoiModel poi)
    {
        // 1) Gán POI hiện tại và lấy các control cần cập nhật.
        _currentSelectedPoi = poi;
        var lblPoiName = LblPoiNameCtrl;
        var lblAddress = LblAddressCtrl;
        var lblDesc = LblDescriptionCtrl;
        var btnPlay = BtnPlayAudioCtrl;
        var popup = DetailPopupCtrl;

        // 2) Cập nhật nội dung text cho popup.
        if (lblPoiName != null) lblPoiName.Text = poi.Name;
        if (lblAddress != null) lblAddress.Text = poi.Address;
        if (lblDesc != null)
            lblDesc.Text = string.IsNullOrEmpty(poi.Description) ? AppRes.NoDescription : poi.Description;

        // 3) Tải ảnh và dừng audio cũ trước khi phát nội dung mới.
        _ = LoadPoiImageAsync(poi);
        StopAudio();

        // 4) Tùy loại media để đặt text nút phát phù hợp.
        if (poi.AudioUrls?.Count > 0)
        {
            _poiAudioIndex.TryGetValue(poi.Id, out int idx);
            int next = idx < poi.AudioUrls.Count ? idx + 1 : 1;
            int total = poi.AudioUrls.Count;
            if (btnPlay != null)
                btnPlay.Text = total > 1
                    ? string.Format(AppRes.BtnListenAudioCount, next, total)
                    : AppRes.BtnListenRecording;
        }
        else
        {
            if (btnPlay != null) btnPlay.Text = AppRes.BtnReadTts;
        }

        // 5) Hiển thị popup trên UI thread.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (popup != null) popup.IsVisible = true;
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  RENDER TOUR (TUYẾN ĐƯỜNG THỰC TẾ + CACHE)
    // ════════════════════════════════════════════════════════════════════════
    public async Task RenderTourOnMap(TourModel tour, bool isInitialLoad = false)
    {
        // Nếu đang bận render thì bỏ qua frame GPS hiện tại để tránh chồng tác vụ.
        if (!await _mapLock.WaitAsync(500))
        {
            System.Diagnostics.Debug.WriteLine("[RenderTour] Đang bận render, bỏ qua GPS frame này.");
            return;
        }
        try
        {
            // 1) Thiết lập trạng thái tour hiện tại.
            _currentTour = tour;
            _lastTourRenderLocation = _currentUserLocation; // Chốt mốc vị trí

            if (isInitialLoad)
            {
                _visitedTourPoiIds.Clear(); // Bắt đầu tour mới thì xóa lịch sử điểm đã đi
            }

            if (_currentlyPlayingGeofencePoi != null &&
                !tour.Pois.Any(tp => tp.PoiId == _currentlyPlayingGeofencePoi.Id))
            {
                // Nếu POI đang phát không thuộc tour mới thì dừng ngay.
                StopAudio();
                _currentlyPlayingGeofencePoi = null;
            }

            await Task.Delay(300);
            var mapView = MapViewCtrl;
            if (mapView?.Map == null) return;

            // Thứ tự dữ liệu: cache -> API -> DB local (fallback khi offline).
            List<PoiModel> allPois;
            if (_allPoisCache.Count > 0)
            {
                allPois = _allPoisCache;
            }
            else
            {
                try
                {
                    allPois = await _apiService.GetPoisAsync(_currentLanguageCode);
                    _allPoisCache = allPois; // Cập nhật cache nếu API thành công
                }
                catch (Exception apiEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[RenderTour] API lỗi ({apiEx.Message}), dùng DB local làm fallback.");
                    allPois = await _localDb.GetPoisAsync();
                    if (allPois.Count > 0)
                        _allPoisCache = allPois; // Cập nhật cache từ DB
                }
            }
            var orderedPois = tour.Pois.OrderBy(p => p.OrderIndex).ToList();
            var tourStart = _currentUserLocation;

            // ── 1. CHECK-IN CÁC ĐIỂM ĐÃ ĐẾN (theo TriggerRadius thực tế) ─────────────────
            // Tránh hard-code bán kính để đồng bộ với geofence audio.
            foreach (var p in orderedPois)
            {
                if (!_visitedTourPoiIds.Contains(p.PoiId))
                {
                    var poiLoc = new MauiLocation.Location(p.Latitude, p.Longitude);
                    double dist = MauiLocation.Location.CalculateDistance(tourStart, poiLoc, DistanceUnits.Kilometers) * 1000;

                    // Lấy TriggerRadius từ dữ liệu POI; thiếu dữ liệu thì fallback 50m.
                    var fullPoi = allPois.FirstOrDefault(x => x.Id == p.PoiId);
                    double checkInRadius = (fullPoi != null && fullPoi.TriggerRadius > 0)
                        ? fullPoi.TriggerRadius
                        : 50;

                    if (dist <= checkInRadius)
                    {
                        _visitedTourPoiIds.Add(p.PoiId);
                    }
                }
            }

            // Lọc các điểm chưa đi qua để vẽ phần tuyến còn lại.
            var remainingPois = orderedPois.Where(p => !_visitedTourPoiIds.Contains(p.PoiId)).ToList();

            // ── 2. LẤY TUYẾN ĐƯỜNG (User -> các điểm chưa đi) ─────────────────────────────
            ClearMapLayers("TourRoute");

            if (remainingPois.Count > 0)
            {
                // Tạo tập waypoint bắt đầu từ vị trí người dùng hiện tại.
                var allWaypoints = new List<MauiLocation.Location> { tourStart };
                allWaypoints.AddRange(remainingPois.Select(p => new MauiLocation.Location(p.Latitude, p.Longitude)));

                SetStatus(AppRes.StatusLoadingRoute, priority: 2, force: true);
                var routeResult = await _routeService.GetRoadRouteAsync(allWaypoints, allWaypoints);

                if (routeResult.Points.Count > 1)
                    mapView.Map.Layers.Add(CreateTourRouteLayer(routeResult.Points, routeResult.Source));

                SetStatus(routeResult.StatusMessage, priority: 2, autoRevertMs: 4000, force: true);
            }
            else
            {
                SetStatus(AppRes.StatusTourCompleted, priority: 2, force: true, autoRevertMs: 5000);
                _currentTour = null; // Cờ này rất quan trọng
                ClearMapLayers("TourRoute");
            }

            // ── 3. VẼ LÊN BẢN ĐỒ ───────────────────────────────────────────────────────────
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Xóa ghim cũ để vẽ lại trạng thái tour mới nhất.
                mapView.Pins.Clear();

                var tourPoiIds = orderedPois.Select(p => p.PoiId).ToHashSet();
                var tourPoisOnly = allPois.Where(p => tourPoiIds.Contains(p.Id)).ToList();
                ClearMapLayers("Geofences");
                mapView.Map.Layers.Insert(1, CreateGeofenceLayer(tourPoisOnly));

                // Vẽ ghim theo trạng thái hành trình: cuối cùng, đã đi, mục tiêu kế tiếp, còn lại.
                for (int idx = 0; idx < orderedPois.Count; idx++)
                {
                    var poi = orderedPois[idx];
                    bool isVisited = _visitedTourPoiIds.Contains(poi.PoiId);
                    bool isLastInTour = (idx == orderedPois.Count - 1);
                    bool isNextTarget = (poi.PoiId == remainingPois.FirstOrDefault()?.PoiId);

                    Microsoft.Maui.Graphics.Color pinColor;

                    // 1) Điểm cuối luôn màu đỏ.
                    if (isLastInTour)
                        pinColor = Microsoft.Maui.Graphics.Colors.Red;
                    // 2) Điểm đã đi qua -> màu xám.
                    else if (isVisited)
                        pinColor = Microsoft.Maui.Graphics.Colors.Gray;
                    // 3) Điểm kế tiếp -> màu xanh lá.
                    else if (isNextTarget)
                        pinColor = Microsoft.Maui.Graphics.Colors.Green;
                    // 4) Các điểm còn lại -> màu cam.
                    else
                        pinColor = Microsoft.Maui.Graphics.Colors.Orange;

                    mapView.Pins.Add(new Pin(mapView)
                    {
                        Position = new Mapsui.UI.Maui.Position(poi.Latitude, poi.Longitude),
                        Label = $"{idx + 1}. {poi.PoiName}{(isVisited ? AppRes.SuffixVisited : "")}",
                        Address = string.Format(AppRes.StopCountFormat, idx + 1, orderedPois.Count),
                        Color = pinColor,
                        Scale = 0.65f, // Giữ kích thước dễ nhìn khi di chuyển
                        Tag = allPois.FirstOrDefault(p => p.Id == poi.PoiId)
                    });
                }

                // ── 4. XỬ LÝ CAMERA ───────────────────────────────────────────────────────
                if (isInitialLoad)
                {
                    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                    var startSmc = SphericalMercator.FromLonLat(tourStart.Longitude, tourStart.Latitude);
                    minX = Math.Min(minX, startSmc.x); maxX = Math.Max(maxX, startSmc.x);
                    minY = Math.Min(minY, startSmc.y); maxY = Math.Max(maxY, startSmc.y);

                    foreach (var poi in orderedPois)
                    {
                        var smc = SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
                        minX = Math.Min(minX, smc.x); maxX = Math.Max(maxX, smc.x);
                        minY = Math.Min(minY, smc.y); maxY = Math.Max(maxY, smc.y);
                    }

                    if (orderedPois.Count == 1 || (minX == maxX && minY == maxY))
                    {
                        mapView.Map.Navigator.CenterOnAndZoomTo(new MPoint(startSmc.x, startSmc.y), 2, 500);
                    }
                    else
                    {
                        var padX = (maxX - minX) * 0.20; var padY = (maxY - minY) * 0.20;
                        mapView.Map.Navigator.ZoomToBox(new MRect(minX - padX, minY - padY, maxX + padX, maxY + padY), MBoxFit.Fit, duration: 500);
                    }
                }
                // Khi GPS cập nhật liên tục, không tự kéo camera về user để tránh giật màn hình.
                // Camera chỉ chỉnh mạnh ở lần đầu load tour hoặc khi người dùng chủ động thao tác.

                mapView.RefreshGraphics();
                ShowTourInfoPanel(tour, orderedPois);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi RenderTour: {ex.Message}");
        }
        finally
        {
            _mapLock.Release();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PANEL THÔNG TIN TOUR
    // ════════════════════════════════════════════════════════════════════════
    private void ShowTourInfoPanel(TourModel tour, List<TourDetailModel> orderedPois)
    {
        // 1) Lấy control và thoát sớm nếu giao diện chưa sẵn sàng.
        var lblTourName = LblTourNameCtrl;
        var tourPoiList = TourPoiListCtrl;
        var tourInfoPanel = TourInfoPanelCtrl;
        if (lblTourName == null || tourPoiList == null || tourInfoPanel == null) return;

        // 2) Vẽ danh sách điểm theo trạng thái đã đi/chưa đi.
        lblTourName.Text = tour.Name ?? "Tour";
        tourPoiList.Children.Clear();

        var nextTargetId = orderedPois.FirstOrDefault(p => !_visitedTourPoiIds.Contains(p.PoiId))?.PoiId;

        for (int i = 0; i < orderedPois.Count; i++)
        {
            var poi = orderedPois[i];
            bool isVisited = _visitedTourPoiIds.Contains(poi.PoiId);
            bool isLast = (i == orderedPois.Count - 1);
            bool isNextTarget = (poi.PoiId == nextTargetId);

            // Đồng bộ icon với màu ghim trên bản đồ.
            string icon;
            if (isLast) icon = "🔴";                   // Điểm cuối
            else if (isVisited) icon = "⚪";           // Đã đi qua
            else if (isNextTarget) icon = "🟢";        // Điểm kế tiếp
            else icon = "🟠";                          // Điểm còn lại

            var card = new Border
            {
                BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#F5F5F5"),
                StrokeThickness = 0,
                Padding = new Thickness(10, 6),
                Margin = new Thickness(0, 0, 6, 0)
            };
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) };

            var stack = new VerticalStackLayout { Spacing = 2 };
            stack.Children.Add(new Label { Text = icon, FontSize = 14, HorizontalOptions = LayoutOptions.Center });

            stack.Children.Add(new Label
            {
                Text = $"{i + 1}. {poi.PoiName}",
                FontSize = 11,
                // Điểm đã qua hiển thị xám và gạch ngang để dễ phân biệt.
                TextColor = (isVisited && !isLast) ? Microsoft.Maui.Graphics.Colors.Gray : Microsoft.Maui.Graphics.Color.FromArgb("#212121"),
                TextDecorations = (isVisited && !isLast) ? TextDecorations.Strikethrough : TextDecorations.None,
                MaxLines = 2,
                LineBreakMode = LineBreakMode.TailTruncation,
                WidthRequest = 80,
                HorizontalTextAlignment = TextAlignment.Center
            });
            card.Content = stack;
            tourPoiList.Children.Add(card);

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

        // 3) Bật panel sau khi render xong danh sách.
        tourInfoPanel.IsVisible = true;
    }

    private async void OnCloseTourPanelClicked(object? sender, EventArgs e)
    {
        // Đóng panel tour và xóa trạng thái tour hiện tại.
        var tourInfoPanel = TourInfoPanelCtrl;
        if (tourInfoPanel != null) tourInfoPanel.IsVisible = false;

        ClearMapLayers("TourRoute");
        _currentTour = null;

        // Xóa lịch sử tour cũ trước khi quay lại chế độ POI thường.
        _visitedTourPoiIds.Clear();

        await LoadPoisWithOfflineFallbackAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GIẢ LẬP ĐI BỘ (DÀNH CHO DEV)
    // ════════════════════════════════════════════════════════════════════════
    private void OnMapClicked_SimulateWalk(object? sender, MapClickedEventArgs e)
    {
        // Dùng cho môi trường dev: chạm bản đồ để giả lập vị trí GPS.
        var mapView = MapViewCtrl;
        if (mapView == null) return;

        _currentUserLocation = new MauiLocation.Location(e.Point.Latitude, e.Point.Longitude);
        _isManualLocationOverride = true;
        _ = SendLocationIfNeededAsync(e.Point.Latitude, e.Point.Longitude);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            mapView.MyLocationLayer.UpdateMyLocation(
                new Mapsui.UI.Maui.Position(e.Point.Latitude, e.Point.Longitude));
            mapView.RefreshGraphics();
        });

        UpdateNearestPoiHighlight();
        if (!_isCheckingGeofences) CheckGeofences();
        if (_currentTour != null)
        {
            _ = MaybeRerenderTourRouteAsync(_currentUserLocation);
        }
        e.Handled = true;
    }

    private void UpdatePopupContentOnly(PoiModel poi)
    {
        // Chỉ cập nhật nội dung popup, không đóng/mở popup và không reset trạng thái khác.
        if (poi == null) return;
        var lblPoiName = LblPoiNameCtrl;
        var lblAddress = LblAddressCtrl;
        var lblDesc = LblDescriptionCtrl;
        var btnPlay = BtnPlayAudioCtrl;

        if (lblPoiName != null) lblPoiName.Text = poi.Name;
        if (lblAddress != null) lblAddress.Text = poi.Address;
        if (lblDesc != null)
            lblDesc.Text = string.IsNullOrEmpty(poi.Description) ? AppRes.NoDescription : poi.Description;

        _ = LoadPoiImageAsync(poi);
        if (btnPlay != null) btnPlay.Text = AppRes.BtnStop;
    }
}