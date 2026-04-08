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
    public async Task RenderTourOnMap(TourModel tour)
    {
        if (!await _mapLock.WaitAsync(500))
        {
            await DisplayAlertAsync(AppRes.AlertNotice, AppRes.MsgTourBusy, AppRes.OkButton);
            return;
        }
        try
        {
            _currentTour = tour;

            if (_currentlyPlayingGeofencePoi != null &&
                !tour.Pois.Any(tp => tp.PoiId == _currentlyPlayingGeofencePoi.Id))
            {
                StopAudio();
                _currentlyPlayingGeofencePoi = null;
            }

            await Task.Delay(300);
            var mapView = MapViewCtrl;
            if (mapView?.Map == null) return;

            var allPois = _allPoisCache.Count > 0
                ? _allPoisCache
                : await _apiService.GetPoisAsync(_currentLanguageCode);

            var orderedPois = tour.Pois.OrderBy(p => p.OrderIndex).ToList();
            int total = orderedPois.Count;
            var tourStart = _currentUserLocation;

            // ── Waypoints ─────────────────────────────────────────────────
            // allWaypoints      : [user] + POI1 + POI2 + ... (gửi cho OSRM)
            // waypointsForCache : POI1 + POI2 + ... (không tính vị trí user để cache key ổn định)
            var allWaypoints = new List<MauiLocation.Location> { tourStart };
            allWaypoints.AddRange(orderedPois.Select(p => new MauiLocation.Location(p.Latitude, p.Longitude)));

            var waypointsForCache = orderedPois
                .Select(p => new MauiLocation.Location(p.Latitude, p.Longitude))
                .ToList();

            // ── Lấy tuyến đường (OSRM → cache → đường thẳng) ──────────────
            SetStatus("🗺️ Đang tải tuyến đường...", priority: 2, force: true);

            var routeResult = await _routeService.GetRoadRouteAsync(
                allWaypoints, waypointsForCache);

            // ── Vẽ lên bản đồ ─────────────────────────────────────────────
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                mapView.Pins.Clear();

                // Geofence layer
                var tourPoiIds = orderedPois.Select(p => p.PoiId).ToHashSet();
                var tourPoisOnly = allPois.Where(p => tourPoiIds.Contains(p.Id)).ToList();
                ClearMapLayers("Geofences");
                mapView.Map.Layers.Insert(1, CreateGeofenceLayer(tourPoisOnly));

                // Route layer (đường thực tế hoặc cache)
                ClearMapLayers("TourRoute");
                if (routeResult.Points.Count > 1)
                    mapView.Map.Layers.Add(CreateTourRouteLayer(routeResult.Points, routeResult.Source));

                // ── Bounding box để auto-zoom ─────────────────────────────
                double minX = double.MaxValue, minY = double.MaxValue,
                       maxX = double.MinValue, maxY = double.MinValue;

                var startSmc = SphericalMercator.FromLonLat(tourStart.Longitude, tourStart.Latitude);
                minX = Math.Min(minX, startSmc.x); maxX = Math.Max(maxX, startSmc.x);
                minY = Math.Min(minY, startSmc.y); maxY = Math.Max(maxY, startSmc.y);

                for (int idx = 0; idx < orderedPois.Count; idx++)
                {
                    var poi = orderedPois[idx];
                    var smc = SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
                    minX = Math.Min(minX, smc.x); maxX = Math.Max(maxX, smc.x);
                    minY = Math.Min(minY, smc.y); maxY = Math.Max(maxY, smc.y);

                    var pinColor = idx == 0 ? Microsoft.Maui.Graphics.Colors.Green
                                 : idx == total - 1 ? Microsoft.Maui.Graphics.Colors.OrangeRed
                                 : Microsoft.Maui.Graphics.Colors.Orange;

                    mapView.Pins.Add(new Pin(mapView)
                    {
                        Position = new Mapsui.UI.Maui.Position(poi.Latitude, poi.Longitude),
                        Label = $"{idx + 1}. {poi.PoiName}",
                        Address = string.Format(AppRes.StopCountFormat, idx + 1, total),
                        Color = pinColor,
                        Scale = idx == 0 || idx == total - 1 ? 0.70f : 0.55f,
                        Tag = allPois.FirstOrDefault(p => p.Id == poi.PoiId)
                    });
                }

                if (total == 1 || (minX == maxX && minY == maxY))
                {
                    var smc = SphericalMercator.FromLonLat(orderedPois[0].Longitude, orderedPois[0].Latitude);
                    mapView.Map.Navigator.CenterOnAndZoomTo(new MPoint(smc.x, smc.y), 2, 500);
                }
                else
                {
                    var padX = (maxX - minX) * 0.20;
                    var padY = (maxY - minY) * 0.20;
                    mapView.Map.Navigator.ZoomToBox(
                        new MRect(minX - padX, minY - padY, maxX + padX, maxY + padY),
                        MBoxFit.Fit, duration: 500);
                }

                mapView.RefreshGraphics();
                ShowTourInfoPanel(tour, orderedPois);
            });

            // Hiển thị thông báo nguồn tuyến đường cho user
            SetStatus(routeResult.StatusMessage, priority: 2, autoRevertMs: 4000, force: true);
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

        for (int i = 0; i < orderedPois.Count; i++)
        {
            var poi = orderedPois[i];
            bool isLast = i == orderedPois.Count - 1;
            string icon = i == 0 ? "🟢" : isLast ? "🔴" : "🟠";

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
            stack.Children.Add(new Label { Text = icon, FontSize = 14, HorizontalOptions = LayoutOptions.Center });
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