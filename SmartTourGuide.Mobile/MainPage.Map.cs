namespace SmartTourGuide.Mobile;

public partial class MainPage
{
    private readonly RouteService _routeService = new RouteService();

    private Mapsui.UI.Maui.MapView? MapViewCtrl => this.FindByName<Mapsui.UI.Maui.MapView>("mapView");
    private Label? LblPoiNameCtrl => this.FindByName<Label>("lblPoiName");
    private Label? LblAddressCtrl => this.FindByName<Label>("lblAddress");
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
    //  LOAD POI LÊN BẢN ĐỒ
    // ════════════════════════════════════════════════════════════════════════
    private async Task LoadPoisOnMap()
    {
        SetStatus(AppRes.StatusLoading, priority: 2, force: true);
        try
        {
            var pois = await _apiService.GetPoisAsync(_currentLanguageCode);
            _allPoisCache = pois;
            _nearestHighlightedPoi = null;
            var mapView = MapViewCtrl;
            if (mapView == null) return;
            mapView.Pins.Clear();

            ClearMapLayers("Geofences");
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
    //  LAYER GEOFENCE
    // ════════════════════════════════════════════════════════════════════════
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

    // ════════════════════════════════════════════════════════════════════════
    //  LAYER TUYẾN ĐƯỜNG
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Vẽ polyline từ danh sách tọa độ chi tiết (OSRM hoặc cache).
    /// Dùng 2 lớp: viền trắng + đường xanh Google-Maps style.
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

            // Viền trắng (làm nổi bật đường trên nền bản đồ)
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

            // Màu đường phụ thuộc nguồn: xanh đậm (online) hoặc xanh nhạt hơn (cache cũ)
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
        _currentSelectedPoi = poi;
        var lblPoiName = LblPoiNameCtrl;
        var lblAddress = LblAddressCtrl;
        var lblDesc = LblDescriptionCtrl;
        var btnPlay = BtnPlayAudioCtrl;
        var popup = DetailPopupCtrl;

        if (lblPoiName != null) lblPoiName.Text = poi.Name;
        if (lblAddress != null) lblAddress.Text = poi.Address;
        if (lblDesc != null)
            lblDesc.Text = string.IsNullOrEmpty(poi.Description) ? AppRes.NoDescription : poi.Description;

        _ = LoadPoiImageAsync(poi);
        StopAudio();

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

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (popup != null) popup.IsVisible = true;
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  RENDER TOUR — TUYẾN ĐƯỜNG THỰC TẾ + CACHE
    // ════════════════════════════════════════════════════════════════════════
    public async Task RenderTourOnMap(TourModel tour, bool isInitialLoad = false)
    {
        // FIX BUG 1: Không hiện popup khi đang bận render (do GPS trigger liên tục).
        // Nếu lock chưa được giải phóng sau 500ms → âm thầm bỏ qua frame GPS này,
        // tránh spam hàng chục Alert dialog khi người dùng di chuyển nhanh.
        if (!await _mapLock.WaitAsync(500))
        {
            System.Diagnostics.Debug.WriteLine("[RenderTour] Đang bận render, bỏ qua GPS frame này.");
            return;
        }
        try
        {
            _currentTour = tour;
            _lastTourRenderLocation = _currentUserLocation; // Chốt mốc vị trí

            if (isInitialLoad)
            {
                _visitedTourPoiIds.Clear(); // Xóa lịch sử khi bắt đầu tour mới
            }

            if (_currentlyPlayingGeofencePoi != null &&
                !tour.Pois.Any(tp => tp.PoiId == _currentlyPlayingGeofencePoi.Id))
            {
                StopAudio();
                _currentlyPlayingGeofencePoi = null;
            }

            await Task.Delay(300);
            var mapView = MapViewCtrl;
            if (mapView?.Map == null) return;

            // FIX BUG 4: Không gọi thẳng _apiService khi cache trống — nếu mất mạng sẽ
            // văng Exception bị nuốt trong catch, bỏ qua hoàn toàn _localDb fallback.
            // Giờ: dùng cache → thử API → thử DB local → nếu DB cũng rỗng mới chịu thua.
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

            // ── 1. CHECK-IN CÁC ĐIỂM ĐÃ ĐẾN (Dùng TriggerRadius thực của POI) ────────────
            // FIX BUG 3: Không hardcode 50m — phải lấy TriggerRadius từ POI thật trong cache
            // để đồng bộ với Geofence audio (vốn đã dùng TriggerRadius đúng).
            // Nếu người dùng nghe xong audio (vào vùng 150m) nhưng check-in chỉ nhận 50m
            // → tour sẽ cứ bắt họ quay lại dù đã nghe hết.
            foreach (var p in orderedPois)
            {
                if (!_visitedTourPoiIds.Contains(p.PoiId))
                {
                    var poiLoc = new MauiLocation.Location(p.Latitude, p.Longitude);
                    double dist = MauiLocation.Location.CalculateDistance(tourStart, poiLoc, DistanceUnits.Kilometers) * 1000;

                    // Lấy TriggerRadius thực từ cache; nếu không tìm thấy thì fallback 50m
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

            // Lọc ra danh sách các điểm CHƯA ĐI QUA để tìm đường
            var remainingPois = orderedPois.Where(p => !_visitedTourPoiIds.Contains(p.PoiId)).ToList();

            // ── 2. LẤY TUYẾN ĐƯỜNG (Chỉ vẽ từ User -> Các điểm chưa đi) ──
            ClearMapLayers("TourRoute");

            if (remainingPois.Count > 0)
            {
                var allWaypoints = new List<MauiLocation.Location> { tourStart };
                allWaypoints.AddRange(remainingPois.Select(p => new MauiLocation.Location(p.Latitude, p.Longitude)));

                // Chỉ dùng tọa độ POI (không có vị trí user) để tạo cache key ổn định
                var poisOnly = remainingPois.Select(p => new MauiLocation.Location(p.Latitude, p.Longitude)).ToList();
                SetStatus("🗺️ Đang tải tuyến đường...", priority: 2, force: true);
                var routeResult = await _routeService.GetRoadRouteAsync(allWaypoints, poisOnly);

                if (routeResult.Points.Count > 1)
                    mapView.Map.Layers.Add(CreateTourRouteLayer(routeResult.Points, routeResult.Source));

                SetStatus(routeResult.StatusMessage, priority: 2, autoRevertMs: 4000, force: true);
            }
            else
            {
                SetStatus("🎉 Bạn đã hoàn thành toàn bộ Tour!", priority: 2, force: true, autoRevertMs: 5000);
                // Tự động thoát chế độ Tour để ngăn thông báo lặp lại mỗi 15m
                _currentTour = null;
                ClearMapLayers("TourRoute");
            }

            // ── 3. VẼ LÊN BẢN ĐỒ ──────────────────────────────────────────
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                mapView.Pins.Clear();

                var tourPoiIds = orderedPois.Select(p => p.PoiId).ToHashSet();
                var tourPoisOnly = allPois.Where(p => tourPoiIds.Contains(p.Id)).ToList();
                ClearMapLayers("Geofences");
                mapView.Map.Layers.Insert(1, CreateGeofenceLayer(tourPoisOnly));

                // Vẽ ghim (Ghim đã đi qua sẽ có màu xám nhạt)
                for (int idx = 0; idx < orderedPois.Count; idx++)
                {
                    var poi = orderedPois[idx];
                    bool isVisited = _visitedTourPoiIds.Contains(poi.PoiId);
                    bool isLastInTour = (idx == orderedPois.Count - 1);
                    bool isNextTarget = (poi.PoiId == remainingPois.FirstOrDefault()?.PoiId);

                    Microsoft.Maui.Graphics.Color pinColor;

                    // 1. Điểm cuối cùng LUÔN LUÔN màu Đỏ (kể cả khi đã đến nơi)
                    if (isLastInTour)
                        pinColor = Microsoft.Maui.Graphics.Colors.Red;
                    // 2. Điểm đã đi qua -> Màu Xám
                    else if (isVisited)
                        pinColor = Microsoft.Maui.Graphics.Colors.Gray;
                    // 3. Điểm chuẩn bị đi tới -> Màu Xanh lá
                    else if (isNextTarget)
                        pinColor = Microsoft.Maui.Graphics.Colors.Green;
                    // 4. Các điểm còn lại chờ đi -> Màu Cam
                    else
                        pinColor = Microsoft.Maui.Graphics.Colors.Orange;

                    mapView.Pins.Add(new Pin(mapView)
                    {
                        Position = new Mapsui.UI.Maui.Position(poi.Latitude, poi.Longitude),
                        Label = $"{idx + 1}. {poi.PoiName}{(isVisited ? " (Đã đến)" : "")}",
                        Address = string.Format(AppRes.StopCountFormat, idx + 1, orderedPois.Count),
                        Color = pinColor,
                        Scale = 0.65f, // Giữ nguyên kích thước to rõ, KHÔNG thu nhỏ nữa
                        Tag = allPois.FirstOrDefault(p => p.Id == poi.PoiId)
                    });
                }

                // ── 4. XỬ LÝ CAMERA ───────────────────────────────────────
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
                // FIX BUG 2: Không CenterOn khi GPS cập nhật vị trí (isInitialLoad == false).
                // Người dùng có thể đang pan bản đồ ra xa để xem điểm đến — việc giật
                // camera về vị trí user mỗi 15m là hành vi cực kỳ khó chịu.
                // Camera chỉ được center lại khi người dùng bấm nút "Center My Location"
                // hoặc khi lần đầu tiên load Tour (isInitialLoad == true ở trên).

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
    //  TOUR INFO PANEL
    // ════════════════════════════════════════════════════════════════════════
    private void ShowTourInfoPanel(TourModel tour, List<TourDetailModel> orderedPois)
    {
        var lblTourName = LblTourNameCtrl;
        var tourPoiList = TourPoiListCtrl;
        var tourInfoPanel = TourInfoPanelCtrl;
        if (lblTourName == null || tourPoiList == null || tourInfoPanel == null) return;

        lblTourName.Text = tour.Name ?? "Tour";
        tourPoiList.Children.Clear();

        var nextTargetId = orderedPois.FirstOrDefault(p => !_visitedTourPoiIds.Contains(p.PoiId))?.PoiId;

        for (int i = 0; i < orderedPois.Count; i++)
        {
            var poi = orderedPois[i];
            bool isVisited = _visitedTourPoiIds.Contains(poi.PoiId);
            bool isLast = (i == orderedPois.Count - 1);
            bool isNextTarget = (poi.PoiId == nextTargetId);

            // Đồng bộ icon với màu ghim trên bản đồ
            string icon;
            if (isLast) icon = "🔴";                   // Cuối cùng luôn đỏ
            else if (isVisited) icon = "⚪";           // Đã qua là chấm xám
            else if (isNextTarget) icon = "🟢";        // Tiếp theo xanh lá
            else icon = "🟠";                          // Còn lại cam

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
                // Chữ điểm đã qua sẽ có màu xám, điểm chưa qua màu đen
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

        tourInfoPanel.IsVisible = true;
    }

    private async void OnCloseTourPanelClicked(object? sender, EventArgs e)
    {
        var tourInfoPanel = TourInfoPanelCtrl;
        if (tourInfoPanel != null) tourInfoPanel.IsVisible = false;

        ClearMapLayers("TourRoute");
        _currentTour = null;

        // 👉 THÊM DÒNG NÀY: Xóa bộ nhớ tour cũ
        _visitedTourPoiIds.Clear();

        await LoadPoisWithOfflineFallbackAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GIẢ LẬP ĐI BỘ (DEV)
    // ════════════════════════════════════════════════════════════════════════
    private void OnMapClicked_SimulateWalk(object? sender, MapClickedEventArgs e)
    {
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