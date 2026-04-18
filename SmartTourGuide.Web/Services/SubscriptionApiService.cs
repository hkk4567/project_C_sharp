using System.Net.Http.Json;
using System.Text.Json;
using SmartTourGuide.Shared.DTOs;

namespace SmartTourGuide.Web.Services;

public class SubscriptionApiService
{
    private readonly HttpClient _http;

    public SubscriptionApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync()
    {
        return await _http.GetFromJsonAsync<List<SubscriptionPlanDto>>("api/subscriptions/plans")
               ?? new List<SubscriptionPlanDto>();
    }

    public async Task<List<SubscriptionPlanDto>> GetAllPlansAsync()
    {
        return await _http.GetFromJsonAsync<List<SubscriptionPlanDto>>("api/subscriptions/plans/all")
               ?? new List<SubscriptionPlanDto>();
    }

    public async Task<OwnerSubscriptionSummaryDto?> GetMySummaryAsync(int ownerId)
    {
        return await _http.GetFromJsonAsync<OwnerSubscriptionSummaryDto>($"api/subscriptions/my-summary/{ownerId}");
    }

    public async Task<List<BoothOwnerSubscriptionDto>> GetByOwnerAsync(int ownerId)
    {
        return await _http.GetFromJsonAsync<List<BoothOwnerSubscriptionDto>>($"api/subscriptions/owner/{ownerId}")
               ?? new List<BoothOwnerSubscriptionDto>();
    }

    public async Task<List<BoothOwnerSubscriptionDto>> GetPendingAsync()
    {
        return await _http.GetFromJsonAsync<List<BoothOwnerSubscriptionDto>>("api/subscriptions/pending")
               ?? new List<BoothOwnerSubscriptionDto>();
    }

    public async Task<List<BoothOwnerSubscriptionDto>> GetAllSubscriptionsAsync(string? status = null)
    {
        var url = string.IsNullOrWhiteSpace(status)
            ? "api/subscriptions"
            : $"api/subscriptions?status={Uri.EscapeDataString(status)}";

        return await _http.GetFromJsonAsync<List<BoothOwnerSubscriptionDto>>(url)
               ?? new List<BoothOwnerSubscriptionDto>();
    }

    public async Task<BoothOwnerSubscriptionDto> SubscribeAsync(int ownerId, CreateSubscriptionDto request)
    {
        var response = await _http.PostAsJsonAsync($"api/subscriptions/subscribe/{ownerId}", request);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<BoothOwnerSubscriptionDto>();
            if (result is null)
            {
                throw new Exception("Khong doc duoc du lieu dang ky tu server.");
            }

            return result;
        }

        var message = await ReadErrorMessageAsync(response);
        throw new Exception(message);
    }

    public async Task CreatePlanAsync(UpsertSubscriptionPlanDto request)
    {
        var response = await _http.PostAsJsonAsync("api/subscriptions/plans", request);
        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorMessageAsync(response);
            throw new Exception(message);
        }
    }

    public async Task UpdatePlanAsync(int id, UpsertSubscriptionPlanDto request)
    {
        var response = await _http.PutAsJsonAsync($"api/subscriptions/plans/{id}", request);
        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorMessageAsync(response);
            throw new Exception(message);
        }
    }

    public async Task DeactivatePlanAsync(int id)
    {
        var response = await _http.DeleteAsync($"api/subscriptions/plans/{id}");
        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorMessageAsync(response);
            throw new Exception(message);
        }
    }

    public async Task ReviewSubscriptionAsync(int id, ReviewSubscriptionDto request)
    {
        var response = await _http.PutAsJsonAsync($"api/subscriptions/{id}/review", request);
        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorMessageAsync(response);
            throw new Exception(message);
        }
    }

    public async Task OwnerCancelPendingAsync(int subscriptionId, int ownerId)
    {
        var response = await _http.PutAsync($"api/subscriptions/{subscriptionId}/owner-cancel?ownerId={ownerId}", null);
        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorMessageAsync(response);
            throw new Exception(message);
        }
    }

    public async Task<string> ExpireCheckAsync()
    {
        var response = await _http.PostAsync("api/subscriptions/expire-check", null);
        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorMessageAsync(response);
            throw new Exception(message);
        }

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Da chay expire-check.";
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("message", out var messageNode))
            {
                return messageNode.GetString() ?? "Da chay expire-check.";
            }
        }
        catch
        {
            // Fallback below.
        }

        return content;
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"Yeu cau that bai ({(int)response.StatusCode}).";
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("message", out var messageNode))
            {
                return messageNode.GetString() ?? content;
            }
        }
        catch
        {
            // Ignore parsing error and fallback to raw content.
        }

        return content;
    }
}
