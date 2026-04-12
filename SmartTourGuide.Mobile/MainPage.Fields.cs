namespace SmartTourGuide.Mobile;

// File này gom toàn bộ biến trạng thái và cấu hình dùng chung cho MainPage.
public partial class MainPage : ContentPage
{
    private readonly PoiApiService _apiService;
    private MemoryLayer _geofenceLayer;
    private const string BaseApiUrl = "https://6b5j1b5h-7058.asse.devtunnels.ms";

    // ── AUDIO CƠ BẢN ───────────────────────────────────────────────────────
    private IAudioPlayer? _audioPlayer;
    private CancellationTokenSource? _ttsCancellationToken;
    private bool _isPlaying = false;
    private PoiModel? _currentSelectedPoi;

    // ── HÀNG ĐỢI AUDIO ──────────────────────────────────────────────────────
    // Lưu "đang ở audio index mấy" cho từng POI (chỉ trong RAM, reset khi tắt app).
    private readonly Dictionary<int, int> _poiAudioIndex = new();
    // Token để hủy queue khi rời POI hoặc bấm Stop.
    private CancellationTokenSource? _queueCts = null;
    // Cờ tạm dừng do cuộc gọi hoặc app vào nền.
    private bool _isPausedByInterruption = false;
    // Lưu giờ bắt đầu phát để tính tổng thời gian nghe.
    private DateTime _playStartTime;
    // Lưu POI ID đang phát (dùng khi ghi log stop/cancel).
    private int _currentAudioPoiId = 0;
    // Chống ghi trùng log cho cùng một lần ghé geofence.
    private bool _isGeofenceVisitActive = false;
    private readonly HashSet<int> _loggedPoisInCurrentGeofenceVisit = new();
    // Session ID cho lượt nghe hiện tại (manual/geofence cùng POI visit dùng chung).
    private string _currentListenSessionId = string.Empty;
    private int _currentListenSessionPoiId = 0;

    // ── THANH TRẠNG THÁI ───────────────────────────────────────────────────
    // Ưu tiên: 0=idle (POI gần nhất), 1=info, 2=zone-event, 3=playing, 4=error.
    private int _statusPriority = 0;
    private CancellationTokenSource? _statusRevertCts;

    // ── VỊ TRÍ & BẢN ĐỒ ────────────────────────────────────────────────────
    private const double DefaultLat = 10.776889;
    private const double DefaultLon = 106.700806;
    private string _currentLanguageCode = "vi-VN";
    private IDispatcherTimer? _geofenceTimer;
    private List<PoiModel> _allPoisCache = new();
    private PoiModel? _currentlyPlayingGeofencePoi;
    private PoiModel? _nearestHighlightedPoi = null;
    private MauiLocation.Location _currentUserLocation =
        new MauiLocation.Location(DefaultLat, DefaultLon);

    private readonly SemaphoreSlim _mapLock = new SemaphoreSlim(1, 1);
    // Cờ chống gọi chồng (re-entrancy guard).
    private bool _isCheckingGeofences = false;
    // Dùng để tính "bao lâu kể từ lần cuối còn ở trong vùng".
    private DateTime _lastGeofenceInsideAt = DateTime.MinValue;
    // Cooldown riêng cho từng POI:
    //   key   = POI Id
    //   value = thời điểm bắt đầu tính CD (sau khi phát đủ N/N audio và rời vùng)
    // CD của POI A hoàn toàn độc lập với POI B.
    private readonly Dictionary<int, DateTime> _poiLastTriggerAt = new();
    private bool _isManualLocationOverride = false;
    private readonly SemaphoreSlim _locationSendLock = new SemaphoreSlim(1, 1);
    private MauiLocation.Location? _lastReportedLocation = null;
    private DateTime _lastReportedLocationAt = DateTime.MinValue;
    // ── TOUR ────────────────────────────────────────────────────────────────
    // Tour đang hiển thị lộ trình (null = chế độ POI thông thường).
    private TourModel? _currentTour = null;

    // ID ẩn danh định danh thiết bị: sinh một lần và lưu lâu dài.
    // Không cần đăng nhập, mỗi điện thoại có một ID riêng để thống kê lượt nghe POI.
    private readonly HashSet<int> _visitedTourPoiIds = new();
    private string _deviceId = GetOrCreateDeviceId();

    /// <summary>
    /// Tạo hoặc đọc Device ID từ bộ nhớ cục bộ.
    /// Lần đầu: sinh GUID mới → lưu vào Preferences.
    /// Lần sau: đọc lại đúng ID cũ → cùng 1 người dùng.
    /// </summary>
    private static string GetOrCreateDeviceId()
    {
        var id = Preferences.Get("device_id", null);
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString("N");
            Preferences.Set("device_id", id);
        }
        return id;
    }
}