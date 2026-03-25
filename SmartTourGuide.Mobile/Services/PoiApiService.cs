using Newtonsoft.Json;
using System.Net.Http.Json;

namespace SmartTourGuide.Mobile.Services;

public class PoiApiService
{
    private readonly HttpClient _httpClient;

    // Thay đổi PORT theo đúng port API của bạn
    private const string Port = "5277";

    public PoiApiService()
    {
        _httpClient = new HttpClient();

        // Cấu hình URL đặc biệt cho Android giả lập
#if ANDROID
        _httpClient.BaseAddress = new Uri($"http://10.0.2.2:{Port}/");
#else
        _httpClient.BaseAddress = new Uri($"http://localhost:{Port}/");
#endif
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
    public async Task LogPoiListenAsync(int poiId, int durationSec, string deviceId)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("api/analytics/poi-listen", new
            {
                PoiId = poiId,
                DeviceId = deviceId,   // ← thay UserId = 0
                ListenDurationSec = durationSec
            });
        }
        catch { }
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


