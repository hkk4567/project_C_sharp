using System.Net.Http.Json;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.Web.Services;

public class AdminDashboardService
{
    private readonly HttpClient _http;

    public AdminDashboardService(HttpClient http)
    {
        _http = http;
    }

    public async Task<AdminDashboardSummaryDto?> GetSummaryAsync()
        => await _http.GetFromJsonAsync<AdminDashboardSummaryDto>("api/analytics/admin/summary");
}