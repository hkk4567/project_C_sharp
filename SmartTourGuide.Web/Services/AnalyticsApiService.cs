using System.Net.Http.Json;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.Web.Services;

public class AnalyticsApiService
{
    private readonly HttpClient _http;

    public AnalyticsApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<TopPoiDto>> GetTopPoisAsync(int top = 10)
    {
        var response = await _http.GetFromJsonAsync<List<TopPoiDto>>($"api/analytics/top-pois?top={top}");
        return response ?? new List<TopPoiDto>();
    }
}