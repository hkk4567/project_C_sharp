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

    public async Task<List<PoiModel>> GetPoisAsync()
    {
        try
        {
            // API trả về danh sách POI (nhớ API get activie bên controller)
            var response = await _httpClient.GetStringAsync("api/pois");
            return JsonConvert.DeserializeObject<List<PoiModel>>(response) ?? new List<PoiModel>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi API: {ex.Message}");
            return new List<PoiModel>();
        }
    }
}

public class PoiModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
}