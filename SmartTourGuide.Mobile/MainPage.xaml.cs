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
        await PreWarmAudioAsync(); // Khởi động sẵn audio pipeline → tránh rè lần đầu
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

    private async Task LoadPoisOnMap()
    {
        SetStatus(SmartTourGuide.Mobile.Resources.Strings.AppResources.StatusLoading, priority: 2, force: true);
        try
        {
            var pois = await _apiService.GetPoisAsync(_currentLanguageCode);
            _allPoisCache = pois;
            _nearestHighlightedPoi = null; // reset vì pins sắp được tạo lại
            mapView.Pins.Clear();

            var oldLayer = mapView.Map.Layers.FirstOrDefault(l => l.Name == "Geofences");
            if (oldLayer != null) mapView.Map.Layers.Remove(oldLayer);
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
                priority: 1, autoRevertMs: 3000);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(
                SmartTourGuide.Mobile.Resources.Strings.AppResources.StatusError, ex.Message),
                priority: 4, force: true);
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

        // Hiển thị số audio và vị trí hiện tại trong queue
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

    public async Task RenderTourOnMap(TourModel tour)
    {
        if (!await _mapLock.WaitAsync(500))
        {
            await DisplayAlertAsync("Thông báo", "Hệ thống đang xử lý tour trước, vui lòng đợi chút!", "OK");
            return;
        }
        try
        {
            await Task.Delay(300);
            if (mapView?.Map == null) return;
            var allPois = await _apiService.GetPoisAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                mapView.Pins.Clear();
                foreach (var l in mapView.Map.Layers.Where(l => l.Name == "Geofences").ToList())
                    mapView.Map.Layers.Remove(l);

                double minX = double.MaxValue, minY = double.MaxValue,
                       maxX = double.MinValue, maxY = double.MinValue;
                bool hasPoints = false;

                foreach (var poi in tour.Pois)
                {
                    var smc = SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
                    minX = Math.Min(minX, smc.x); minY = Math.Min(minY, smc.y);
                    maxX = Math.Max(maxX, smc.x); maxY = Math.Max(maxY, smc.y);
                    hasPoints = true;

                    mapView.Pins.Add(new Pin(mapView)
                    {
                        Position = new Mapsui.UI.Maui.Position(poi.Latitude, poi.Longitude),
                        Label = poi.PoiName,
                        Color = Colors.Orange,
                        Tag = allPois.FirstOrDefault(p => p.Id == poi.PoiId)
                    });
                }

                if (hasPoints)
                {
                    if (tour.Pois.Count == 1 || (minX == maxX && minY == maxY))
                        mapView.Map.Navigator.CenterOnAndZoomTo(new MPoint(minX, minY), 2, 500);
                    else
                    {
                        var padX = (maxX - minX) * 0.15;
                        var padY = (maxY - minY) * 0.15;
                        mapView.Map.Navigator.ZoomToBox(
                            new MRect(minX - padX, minY - padY, maxX + padX, maxY + padY),
                            MBoxFit.Fit, duration: 500);
                    }
                }
                mapView.RefreshGraphics();
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Lỗi RenderTour: {ex.Message}"); }
        finally { _mapLock.Release(); }
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
    /// <summary>
    /// Phát lần lượt TẤT CẢ audio của POI bắt đầu từ index đã lưu.
    /// - Bị cancel (rời POI / bấm Stop) → lưu index để lần sau tiếp tục.
    /// - Phát hết tất cả → reset index về 0.
    /// </summary>
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
                // Đang phát audio[i] bị gián đoạn → lần sau vào phát audio KẾ TIẾP (i+1)
                // Nếu đây là audio cuối thì reset về 0 (lần sau phát lại từ đầu)
                int nextIdx = i + 1;
                _poiAudioIndex[poi.Id] = nextIdx < urls.Count ? nextIdx : 0;
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioQueue] Lỗi audio {i}: {ex.Message}");
            }
        }

        // Phát hết toàn bộ → reset về 0, lần sau vào phát lại từ đầu
        _poiAudioIndex[poi.Id] = 0;
        _isPlaying = false;
        int played = urls.Count;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // priority=1 + autoRevert=3s: hiện thông báo xong rồi tự trở về "📍 Gần nhất"
            SetStatus($"✅ {poi.Name}  ·  Phát xong {played} audio", priority: 1, autoRevertMs: 3000);
            if (btnPlayAudio != null) btnPlayAudio.Text = "🔊 Nghe lại";
        });
    }

    /// <summary>
    /// Phát một file audio và CHỜ đến khi track thật sự kết thúc (hoặc bị cancel).
    /// Dùng TaskCompletionSource để await được. Tích hợp cache + fade-in.
    /// </summary>
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

        // SỬA LỖI Ở ĐÂY: Lưu player vào biến cục bộ để tránh bị xóa nhầm
        var currentPlayer = AudioManager.Current.CreatePlayer(audioStream);
        _audioPlayer = currentPlayer; // Gán ra biến global để quản lý

        currentPlayer.PlaybackEnded += (s, e) =>
        {
            audioStream.Dispose();
            tcs.TrySetResult(true);
        };

        using var reg = ct.Register(() =>
        {
            try
            {
                // Thêm check null và bắt mọi Exception (đề phòng Native Android/iOS ném lỗi lạ khi đã dispose)
                currentPlayer?.Stop();
            }
            catch (Exception) { /* Bỏ qua lỗi do player đã bị hủy */ }
            try { audioStream?.Dispose(); } catch (Exception) { }
            tcs.TrySetCanceled();
        });

        // Fade-in che tiếng rè hardware
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
    //  Android/iOS tắt audio hardware khi không dùng để tiết kiệm pin.
    //  Phát 200ms WAV silence ở volume 0 lúc khởi động → hardware đã sẵn sàng
    //  → mọi lần Play() thật sự tiếp theo không còn bị rè ở đầu.
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
        catch { /* Warm-up thất bại → app vẫn chạy, lần đầu có thể rè nhẹ */ }
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

    /// <summary>Tăng dần volume 0 → 1 trong 150ms để che tiếng rè hardware Android/iOS.</summary>
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
    //  TEXT-TO-SPEECH (fallback khi không có file audio)
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
        // 0. Huỷ queue + reset status priority
        _queueCts?.Cancel();
        _queueCts = null;
        _isPausedByInterruption = false;
        _statusRevertCts?.Cancel();
        _statusRevertCts = null;
        _statusPriority = 0;

        // 1. Dừng Audio Player
        if (_audioPlayer != null)
        {
            if (_audioPlayer.IsPlaying) _audioPlayer.Stop();
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

        // Reset text nút, hiển thị vị trí trong queue nếu có nhiều audio
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
    }

    // ════════════════════════════════════════════════════════════════════════
    //  STATUS BAR MANAGER
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cập nhật status bar với hệ thống ưu tiên.
    /// Message có priority thấp hơn hiện tại sẽ bị bỏ qua.
    /// Dùng force=true để luôn hiển thị (vd: loading, error).
    /// </summary>
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

    /// <summary>Hiển thị trạng thái idle: POI gần nhất + khoảng cách.</summary>
    private void ShowIdleStatus()
    {
        if (_isPlaying || _nearestHighlightedPoi == null) return;
        var poiLoc = new MauiLocation.Location(
            _nearestHighlightedPoi.Latitude, _nearestHighlightedPoi.Longitude);
        double dist = MauiLocation.Location.CalculateDistance(
            _currentUserLocation, poiLoc, DistanceUnits.Kilometers) * 1000;
        string distText = dist >= 1000
            ? $"{dist / 1000:F1} km"
            : $"{dist:F0} m";
        statusLabel.Text = $"📍 Gần nhất: {_nearestHighlightedPoi.Name} · {distText}";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GEOFENCE ENGINE
    // ════════════════════════════════════════════════════════════════════════
    private async void CheckGeofences()
    {
        // CHỐNG SPAM: Đang check thì không check chồng lên
        if (_isCheckingGeofences) return;
        if (_allPoisCache.Count == 0) return;

        try
        {
            _isCheckingGeofences = true; // Khóa cửa

            var poisInRange = new List<PoiModel>();
            foreach (var poi in _allPoisCache)
            {
                var poiLoc = new MauiLocation.Location(poi.Latitude, poi.Longitude);
                double dist = MauiLocation.Location.CalculateDistance(
                    _currentUserLocation, poiLoc, DistanceUnits.Kilometers) * 1000;

                double radius = poi.TriggerRadius > 0 ? poi.TriggerRadius : 50;
                if (dist <= radius) poisInRange.Add(poi);
            }

            // KỊCH BẢN A: Ra khỏi tất cả vùng
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

            // KỊCH BẢN C: Vẫn ở trong 1 vùng nào đó, nhưng ĐÃ RA KHỎI vùng đang phát
            if (_currentlyPlayingGeofencePoi != null)
            {
                bool stillInZone = poisInRange.Any(p => p.Id == _currentlyPlayingGeofencePoi.Id);
                if (!stillInZone)
                {
                    StopAudio();
                    _currentlyPlayingGeofencePoi = null;

                    // NGHỈ NGƠI 1 CHÚT ĐỂ PHẦN CỨNG AUDIO DỌN DẸP
                    await Task.Delay(300);
                }
            }

            // Lọc ra điểm có Ưu tiên cao nhất
            var highestPri = poisInRange.OrderByDescending(p => p.Priority).First();

            // KỊCH BẢN B: Xử lý bật/đổi nhạc
            if (_currentlyPlayingGeofencePoi == null)
            {
                // Chưa có gì phát -> Bật cái ưu tiên cao nhất lên
                _currentlyPlayingGeofencePoi = highestPri;
                TriggerAutoAudio(highestPri); // Bật nhạc
            }
            else if (_currentlyPlayingGeofencePoi.Id != highestPri.Id)
            {
                // Đang phát Vùng 1, nhưng Vùng 2 mới là cao nhất
                if (highestPri.Priority > _currentlyPlayingGeofencePoi.Priority || !_isPlaying)
                {
                    // 1. Tắt Vùng 1
                    StopAudio();

                    // 2. QUAN TRỌNG NHẤT: Đợi 300ms để OS giải phóng Audio/TTS cũ
                    await Task.Delay(300);

                    // 3. Bật Vùng 2
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
            _isCheckingGeofences = false; // Mở cửa cho lần quét tiếp theo
        }
    }

    /// <summary>Kích hoạt queue khi Geofence phát hiện user vào vùng POI.</summary>
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

        // VÁ LỖI 3: Bắt lỗi nếu task chạy ngầm bị Crash
        _ = PlayAudioQueueAsync(poi, _queueCts.Token).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                // Lấy lỗi gốc để in ra Console
                var ex = t.Exception?.GetBaseException();
                System.Diagnostics.Debug.WriteLine($"[TriggerAudio] Lỗi phát ngầm: {ex?.Message}");

                // Cập nhật lại UI
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (btnPlayAudio != null)
                        btnPlayAudio.Text = poi.AudioUrls?.Count > 0 ? "🔊 Nghe File Ghi Âm" : "🗣️ Đọc Tự Động (TTS)";

                    SetStatus("Lỗi phát âm thanh tự động", priority: 2, autoRevertMs: 3000);
                });
            }
        }, TaskContinuationOptions.OnlyOnFaulted); // Chỉ chạy block này nếu Task bị lỗi
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HIGHLIGHT POI GẦN NHẤT
    // ════════════════════════════════════════════════════════════════════════
    private void UpdateNearestPoiHighlight()
    {
        if (_allPoisCache.Count == 0 || mapView?.Pins == null || mapView.Pins.Count == 0) return;

        PoiModel? nearestPoi = null;
        double minDistanceM = double.MaxValue;

        // 1. Tìm POI gần nhất
        foreach (var poi in _allPoisCache)
        {
            var poiLoc = new MauiLocation.Location(poi.Latitude, poi.Longitude);
            double dist = MauiLocation.Location.CalculateDistance(
                _currentUserLocation, poiLoc, DistanceUnits.Kilometers) * 1000;
            if (dist < minDistanceM) { minDistanceM = dist; nearestPoi = poi; }
        }

        if (nearestPoi == null) return;

        // 2. Tính toán text khoảng cách mới nhất
        var distanceText = minDistanceM >= 1000
            ? $"{minDistanceM / 1000:F1} km"
            : $"{minDistanceM:F0} m";

        // LUÔN LUÔN CẬP NHẬT TEXT TRẠNG THÁI ĐỂ SỐ MÉT NHẢY LIÊN TỤC
        SetStatus($"📍 Gần nhất: {nearestPoi.Name} · {distanceText}", priority: 0);

        // 3. CHỈ VẼ LẠI BẢN ĐỒ NẾU MỤC TIÊU THAY ĐỔI
        // (Để tiết kiệm tài nguyên và không làm giật bản đồ)
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

        UpdateNearestPoiHighlight(); // Cập nhật ngay, không chờ timer 3s

        if (!_isCheckingGeofences) CheckGeofences();

        e.Handled = true;
    }
}