using System.Net.Http.Json;

namespace SmartTourGuide.Web.Services;

public class OwnerNotificationService
{
    private readonly HttpClient _http;

    public OwnerNotificationService(HttpClient http)
    {
        _http = http;
    }

    public async Task<OwnerNotificationResponse?> GetOwnerNotificationsAsync()
    {
        // DelegatingHandler tự động thêm X-User-Name header
        return await _http.GetFromJsonAsync<OwnerNotificationResponse>("api/notifications/owner");
    }

    public async Task<bool> MarkAsReadAsync(int notificationId)
    {
        // DelegatingHandler tự động thêm X-User-Name header
        var response = await _http.PutAsync($"api/notifications/{notificationId}/read", null);
        return response.IsSuccessStatusCode;
    }
}

public class OwnerNotificationResponse
{
    public int UnreadCount { get; set; }
    public List<OwnerNotificationItem> Items { get; set; } = new();
}

public class OwnerNotificationItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string AdminUsername { get; set; } = string.Empty;
    public int? PoiId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}
