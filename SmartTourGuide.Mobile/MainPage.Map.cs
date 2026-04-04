namespace SmartTourGuide.Mobile;

public partial class MainPage
{
    private Mapsui.UI.Maui.MapView? MapViewCtrl => this.FindByName<Mapsui.UI.Maui.MapView>("mapView");
    private Label? LblPoiNameCtrl => this.FindByName<Label>("lblPoiName");
    private Label? LblAddressCtrl => this.FindByName<Label>("lblAddress");
    private Label? LblDescriptionCtrl => this.FindByName<Label>("lblDescription");
    private Button? BtnPlayAudioCtrl => this.FindByName<Button>("btnPlayAudio");
    private Border? DetailPopupCtrl => this.FindByName<Border>("DetailPopup");
    private Label? LblTourNameCtrl => this.FindByName<Label>("lblTourName");
    private HorizontalStackLayout? TourPoiListCtrl => this.FindByName<HorizontalStackLayout>("tourPoiList");
    private Border? TourInfoPanelCtrl => this.FindByName<Border>("TourInfoPanel");

    // LoadPoisOnMap, CreateGeofenceLayer, ShowPoiDetail
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
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(AppRes.StatusError, ex.Message),
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
        var lblPoiName = LblPoiNameCtrl;
        var lblAddress = LblAddressCtrl;
        var lblDescription = LblDescriptionCtrl;
        var btnPlayAudio = BtnPlayAudioCtrl;
        var detailPopup = DetailPopupCtrl;

        if (lblPoiName != null) lblPoiName.Text = poi.Name;
        if (lblAddress != null) lblAddress.Text = poi.Address;
        if (lblDescription != null)
            lblDescription.Text = string.IsNullOrEmpty(poi.Description)
                ? AppRes.NoDescription
                : poi.Description;

        _ = LoadPoiImageAsync(poi);

        StopAudio();

        if (poi.AudioUrls?.Count > 0)
        {
            _poiAudioIndex.TryGetValue(poi.Id, out int idx);
            int next = (idx < poi.AudioUrls.Count) ? idx + 1 : 1;
            int total = poi.AudioUrls.Count;
            if (btnPlayAudio != null)
            {
                btnPlayAudio.Text = total > 1
                    ? string.Format(AppRes.BtnListenAudioCount, next, total)
                    : AppRes.BtnListenRecording;
            }
        }
        else
        {
            if (btnPlayAudio != null)
                btnPlayAudio.Text = AppRes.BtnReadTts;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (detailPopup != null) detailPopup.IsVisible = true;
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TOUR — HIỂN THỊ CÁC ĐIỂM DỪNG TRÊN BẢN ĐỒ (không vẽ tuyến đường)
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

            // Nếu đang phát audio POI không thuộc tour → dừng ngay
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

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                mapView.Pins.Clear();
                var tourPoiIds = orderedPois.Select(p => p.PoiId).ToHashSet();
                var tourPoisOnly = allPois.Where(p => tourPoiIds.Contains(p.Id)).ToList();
                // Xóa Geofences layer cũ
                ClearMapLayers("Geofences");
                mapView.Map.Layers.Insert(1, CreateGeofenceLayer(tourPoisOnly));

                double minX = double.MaxValue, minY = double.MaxValue,
                       maxX = double.MinValue, maxY = double.MinValue;
                bool hasPoints = false;

                var tourStartSmc = SphericalMercator.FromLonLat(tourStart.Longitude, tourStart.Latitude);
                minX = Math.Min(minX, tourStartSmc.x); minY = Math.Min(minY, tourStartSmc.y);
                maxX = Math.Max(maxX, tourStartSmc.x); maxY = Math.Max(maxY, tourStartSmc.y);
                hasPoints = true;

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
                        Address = string.Format(AppRes.StopCountFormat, idx + 1, total),
                        Color = pinColor,
                        Scale = idx == 0 || idx == total - 1 ? 0.70f : 0.55f,
                        Tag = allPois.FirstOrDefault(p => p.Id == poi.PoiId)
                    });
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

    // ShowTourInfoPanel, OnCloseTourPanelClicked
    /// <summary>Hiện panel tóm tắt lộ trình ở góc dưới bản đồ.</summary>
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

        tourInfoPanel.IsVisible = true;
    }
    /// <summary>Đóng TourInfoPanel nhưng giữ route layer để user xem tuyến đi lâu hơn.</summary>
    private async void OnCloseTourPanelClicked(object? sender, EventArgs e)
    {
        var tourInfoPanel = TourInfoPanelCtrl;
        if (tourInfoPanel != null) tourInfoPanel.IsVisible = false;

        // reset tour state và load lại POI bình thường
        _currentTour = null;
        await LoadPoisWithOfflineFallbackAsync();
    }
    // OnMapClicked_SimulateWalk
    // ════════════════════════════════════════════════════════════════════════
    //  GIẢ LẬP ĐI BỘ (DEV)
    // ════════════════════════════════════════════════════════════════════════
    private void OnMapClicked_SimulateWalk(object? sender, MapClickedEventArgs e)
    {
        var mapView = MapViewCtrl;
        if (mapView == null) return;

        _currentUserLocation = new MauiLocation.Location(e.Point.Latitude, e.Point.Longitude);
        _isManualLocationOverride = true;
        // Gửi vị trí lên server để lưu tuyến di chuyển ẩn danh
        // Dùng cho heatmap và analytics
        _ = _apiService.SendLocationAsync(
            e.Point.Latitude,
            e.Point.Longitude,
            _deviceId);  // ← Device ID ẩn danh
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
        var lblDescription = LblDescriptionCtrl;
        var btnPlayAudio = BtnPlayAudioCtrl;

        // Kiểm tra null cho từng control XAML để an toàn tuyệt đối
        if (lblPoiName != null) lblPoiName.Text = poi.Name;
        if (lblAddress != null) lblAddress.Text = poi.Address;
        if (lblDescription != null)
            lblDescription.Text = string.IsNullOrEmpty(poi.Description)
                ? AppRes.NoDescription
                : poi.Description;

        _ = LoadPoiImageAsync(poi);

        // Cập nhật text nút bấm mà không dừng audio hiện tại
        if (btnPlayAudio != null)
        {
            btnPlayAudio.Text = AppRes.BtnStop;
        }
    }
}