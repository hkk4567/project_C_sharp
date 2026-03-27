using Blazored.LocalStorage;

namespace SmartTourGuide.Web.Services;

public class HttpClientHeaderHandler : DelegatingHandler
{
    private readonly ILocalStorageService _localStorage;

    public HttpClientHeaderHandler(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Tự động add X-User-Name header từ localStorage cho mỗi request
        var username = await _localStorage.GetItemAsync<string>("username");
        if (!string.IsNullOrEmpty(username))
        {
            request.Headers.Remove("X-User-Name");
            request.Headers.Add("X-User-Name", username);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
