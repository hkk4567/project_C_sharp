using System.Net.Http.Json;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.Web.Services;

public class OwnerAnalyticsApiService
{
    private readonly HttpClient _http;

    public OwnerAnalyticsApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<OwnerAnalyticsSummaryDto?> GetSummaryAsync(int ownerId)
        => await _http.GetFromJsonAsync<OwnerAnalyticsSummaryDto>($"api/analytics/owner/{ownerId}/summary");

    public async Task<List<TopPoiDto>> GetTopPoisAsync(int ownerId, int top = 10)
        => await _http.GetFromJsonAsync<List<TopPoiDto>>($"api/analytics/owner/{ownerId}/top-pois?top={top}")
           ?? new List<TopPoiDto>();

    public async Task<List<AvgListenTimeDto>> GetAvgListenTimeAsync(int ownerId, int top = 20)
        => await _http.GetFromJsonAsync<List<AvgListenTimeDto>>($"api/analytics/owner/{ownerId}/avg-listen-time?top={top}")
           ?? new List<AvgListenTimeDto>();

    public async Task<List<OwnerDailyListenDto>?> GetDailyListensAsync(int ownerId, int days = 7)
        => await _http.GetFromJsonAsync<List<OwnerDailyListenDto>>($"api/analytics/owner/{ownerId}/daily-listens?days={days}")
           ?? new List<OwnerDailyListenDto>();

    public async Task<List<LocationLogDto>> GetMovementAsync(int ownerId, int hours = 24, int maxPoints = 300)
        => await _http.GetFromJsonAsync<List<LocationLogDto>>(
               $"api/analytics/owner/{ownerId}/movement?hours={hours}&maxPoints={maxPoints}")
           ?? new List<LocationLogDto>();

    public async Task<List<OwnerHeatmapPointDto>> GetHeatmapAsync(int ownerId, int hours = 24, int maxPoints = 30)
        => await _http.GetFromJsonAsync<List<OwnerHeatmapPointDto>>(
               $"api/analytics/owner/{ownerId}/heatmap?hours={hours}&maxPoints={maxPoints}")
           ?? new List<OwnerHeatmapPointDto>();
}
