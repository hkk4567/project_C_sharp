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
}