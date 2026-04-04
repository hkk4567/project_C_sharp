using System.Net.Http.Json;

namespace SmartTourGuide.Web.Services;

public class AdminNotificationService
{
    private readonly HttpClient _http;

    public AdminNotificationService(HttpClient http)
    {
        _http = http;
    }

    public async Task<AdminNotificationResponse?> GetAdminNotificationsAsync()
    {
        // DelegatingHandler tự động thêm X-User-Name header
        return await _http.GetFromJsonAsync<AdminNotificationResponse>("api/notifications/admin");
    }

    public async Task<bool> MarkAsReadAsync(int notificationId)
    {
        // DelegatingHandler tự động thêm X-User-Name header
        var response = await _http.PutAsync($"api/notifications/admin/{notificationId}/read", null);
        return response.IsSuccessStatusCode;
    }
}

public class AdminNotificationResponse
{
    public int UnreadCount { get; set; }
    public List<AdminNotificationItem> Items { get; set; } = new();
}

public class AdminNotificationItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string OwnerUsername { get; set; } = string.Empty;
    public int? PoiId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}
