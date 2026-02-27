using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace SmartTourGuide.Web.Services;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly ILocalStorageService _localStorage;

    public CustomAuthStateProvider(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            // Bọc try-catch để tránh lỗi khi chạy Blazor Server Prerendering (khi chưa có JS)
            var role = await _localStorage.GetItemAsync<string>("userRole");
            var username = await _localStorage.GetItemAsync<string>("username");

            if (!string.IsNullOrEmpty(role) && !string.IsNullOrEmpty(username))
            {
                var identity = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(ClaimTypes.Role, role)
                }, "FakeAuth"); // AuthenticationType không được rỗng

                var user = new ClaimsPrincipal(identity);
                return new AuthenticationState(user);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi Auth khi F5: {ex.Message}");
        }

        // Mặc định: Chưa đăng nhập (Anonymous)
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    public void NotifyUserLogin(string username, string role)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role)
        }, "FakeAuth");

        var user = new ClaimsPrincipal(identity);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }

    public async Task NotifyUserLogout()
    {
        await _localStorage.RemoveItemAsync("userRole");
        await _localStorage.RemoveItemAsync("username");
        await _localStorage.RemoveItemAsync("userId");
        await _localStorage.RemoveItemAsync("authToken");

        var identity = new ClaimsIdentity();
        var user = new ClaimsPrincipal(identity);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }
}