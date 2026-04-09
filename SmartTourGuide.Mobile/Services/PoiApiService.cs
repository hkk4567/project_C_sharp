using Newtonsoft.Json;
using System.Net.Http.Json;

namespace SmartTourGuide.Mobile.Services;

public class PoiApiService
{
    private readonly HttpClient _httpClient;

    private const string BaseUrl = "https://2tlcgj8k-7058.asse.devtunnels.ms/";

    public PoiApiService()
    {
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(BaseUrl);

        // 2. QUAN TRỌNG: Thêm header này để HttpClient có thể lấy dữ liệu trực tiếp 
        // xuyên qua trang cảnh báo "Anti-Phishing" của Microsoft Dev Tunnels.
        _httpClient.DefaultRequestHeaders.Add("X-Tunnel-Skip-AntiPhishing-Page", "true");

        // Cấu hình timeout để tránh treo App nếu tunnel bị lag
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<List<PoiModel>> GetPoisAsync(string langCode = "vi-VN")
    {
        try
        {
            // Gửi kèm langCode lên Server
            var response = await _httpClient.GetStringAsync($"api/pois/mobile?langCode={langCode}");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<PoiModel>>(response) ?? new List<PoiModel>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi API: {ex.Message}");
            return new List<PoiModel>();
        }
    }
    public async Task<List<TourModel>> GetToursAsync()
    {
        try
        {
            // Thêm /mobile và truyền langCode
            var langCode = Preferences.Get("AppLanguage", "vi-VN");
            var response = await _httpClient.GetStringAsync($"api/tours/mobile?langCode={langCode}");

            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<TourModel>>(response) ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi tải Tours: {ex.Message}");
            return new List<TourModel>();
        }
    }

    public async Task<TourModel?> GetTourDetailsAsync(int tourId)
    {
        try
        {
            var langCode = Preferences.Get("AppLanguage", "vi-VN");
            var response = await _httpClient.GetStringAsync($"api/tours/mobile/{tourId}?langCode={langCode}");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<TourModel>(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi tải chi tiết Tour: {ex.Message}");
            return null;
        }
    }
    /// <summary>
    /// Gửi log lên server sau khi user nghe audio tại 1 POI.
    /// Phục vụ 2 tính năng analytics:
    ///   - Top địa điểm được nghe nhiều nhất (đếm số lần gọi hàm này)
    ///   - Thời gian trung bình nghe 1 POI (AVG của durationSec)
    /// Không throw exception — lỗi mạng không được crash app.
    /// </summary>
    public async Task LogPoiListenAsync(int poiId, int durationSec, string deviceId, string? sessionId = null)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("api/analytics/poi-listen", new
            {
                PoiId = poiId,
                DeviceId = deviceId,   // ← thay UserId = 0
                SessionId = sessionId,
                ListenDurationSec = durationSec
            });
        }
        catch { }
    }


    /// <summary>
    /// Gửi vị trí GPS hiện tại lên server.
    /// Dùng để vẽ heatmap và lưu tuyến di chuyển ẩn danh.
    /// DeviceId thay cho UserId — không cần đăng nhập.
    /// </summary>
    public async Task SendLocationAsync(double lat, double lng, string deviceId)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("api/tracking", new
            {
                UserId = 0,        // Ẩn danh
                DeviceId = deviceId,
                Latitude = lat,
                Longitude = lng,
                Timestamp = DateTime.UtcNow
            });
        }
        catch { /* Không crash app nếu mất mạng */ }
    }
}

public class PoiModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }

    // API trả về List<string> ImageUrls
    public List<string>? ImageUrls { get; set; }
    public List<string>? AudioUrls { get; set; }
    public double TriggerRadius { get; set; } = 50;
    public int CooldownInSeconds { get; set; } = 300;
    public int Priority { get; set; } = 1;
}
public class TourModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int TotalPois { get; set; }
    public List<TourDetailModel> Pois { get; set; } = new();
}

public class TourDetailModel
{
    public int PoiId { get; set; }
    public string? PoiName { get; set; }
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int OrderIndex { get; set; }
}


