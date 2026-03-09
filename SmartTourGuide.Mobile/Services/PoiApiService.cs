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
        // Sử dụng GetAsync thay vì GetStringAsync để kiểm tra mã lỗi HTTP
        var response = await _httpClient.GetAsync("api/pois");
        
        if (!response.IsSuccessStatusCode)
        {
            // Nếu lỗi 404 hoặc 500, nó sẽ nhảy vào đây
            Console.WriteLine($"Lỗi HTTP: {response.StatusCode}");
            return new List<PoiModel>();
        }

        var json = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Dữ liệu nhận được: {json}"); // Xem ở cửa sổ Output/Console

        var result = JsonConvert.DeserializeObject<List<PoiModel>>(json);
        return result ?? new List<PoiModel>();
    }
    catch (Exception ex)
    {
        // Nếu lỗi do Android chặn HTTP, nó sẽ báo "Cleartext HTTP traffic not permitted"
        Console.WriteLine($"Lỗi kết nối API: {ex.Message}");
        return new List<PoiModel>();
    }
}
    public async Task<List<TourModel>> GetToursAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync("api/tours");
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
            var response = await _httpClient.GetStringAsync($"api/tours/{tourId}");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<TourModel>(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi tải chi tiết Tour: {ex.Message}");
            return null;
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
    public string? Description { get; set; } 

    // API trả về List<string> ImageUrls
    public List<string>? ImageUrls { get; set; }
    public List<string>? AudioUrls { get; set; }
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