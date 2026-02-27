using System.Net.Http.Json;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.Web.Services; // Nhớ thêm namespace này

public class PoiApiService
{
    private readonly HttpClient _http;

    public PoiApiService(HttpClient http)
    {
        _http = http;
    }

    // 1. Lấy bài của chính mình (Owner)
    public async Task<List<PoiDto>> GetMyPois(int ownerId)
    {
        // SỬA LỖI Ở ĐÂY:
        // Thêm "?? new List<PoiDto>()" vào cuối.
        // Ý nghĩa: Nếu API trả về null, thì trả về danh sách rỗng để UI không bị crash.
        return await _http.GetFromJsonAsync<List<PoiDto>>($"api/pois/owner/{ownerId}")
               ?? new List<PoiDto>();
    }

    // 2. Admin duyệt bài
    public async Task ApprovePoi(int poiId)
    {
        var response = await _http.PutAsync($"api/pois/{poiId}/approve", null);

        // Nên kiểm tra xem API có chạy thành công không
        if (!response.IsSuccessStatusCode)
        {
            // Có thể ném lỗi hoặc log ra console
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Lỗi duyệt bài: {error}");
        }
    }

    // 3. Tạo bài mới
    public async Task CreatePoi(MultipartFormDataContent content)
    {
        var response = await _http.PostAsync("api/pois", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Lỗi tạo bài: {error}");
        }
    }
}