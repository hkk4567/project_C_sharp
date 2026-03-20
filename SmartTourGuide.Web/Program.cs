using MudBlazor.Services;
using SmartTourGuide.Web.Components;
using SmartTourGuide.Web.Services;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies; // >>> QUAN TRỌNG: Thêm dòng này

var builder = WebApplication.CreateBuilder(args);

// --- PHẦN 1: ĐĂNG KÝ DỊCH VỤ ---

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 1. Dịch vụ UI MudBlazor
builder.Services.AddMudServices();

// 2. Dịch vụ Local Storage
builder.Services.AddBlazoredLocalStorage();

// >>> QUAN TRỌNG: Đăng ký dịch vụ Authentication để sửa lỗi "Missing IAuthenticationService"
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "auth_token";
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
    });

// 3. Cấu hình Authorization & Cascading State
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// Đăng ký Provider quản lý trạng thái đăng nhập
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

// 4. Cấu hình HttpClient gọi API
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri("http://localhost:5277/")
});

// 5. Đăng ký các Service nghiệp vụ
builder.Services.AddScoped<PoiApiService>();
builder.Services.AddScoped<IUserService, UserService>();
var app = builder.Build();

// --- PHẦN 2: CẤU HÌNH PIPELINE (MIDDLEWARE) ---

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseAntiforgery();

// >>> QUAN TRỌNG: Phải đặt Authentication TRƯỚC Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();