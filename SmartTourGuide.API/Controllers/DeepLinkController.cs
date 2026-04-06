using Microsoft.AspNetCore.Mvc;
using SmartTourGuide.API.Data;
using Microsoft.EntityFrameworkCore;
using SmartTourGuide.API.Data.Entities;

namespace SmartTourGuide.API.Controllers;

/// <summary>
/// Bắt URL /poi/{poiId} — đây chính là URL được nhúng trong QR Code.
///
/// Luồng xử lý:
///   1. Android có App Links → OS chặn URL, mở MAUI app trực tiếp (controller KHÔNG chạy).
///   2. iOS có Universal Links → tương tự Android.
///   3. Không cài App / xác thực App Links thất bại → trình duyệt gọi endpoint này
///      → trả về Landing Page HTML với nút tải APK và nút bỏ qua.
///
/// Endpoint /.well-known/assetlinks.json và apple-app-site-association
/// được phục vụ qua StaticFiles (wwwroot/.well-known/).
/// </summary>
[Route("poi")]
public class DeepLinkController : Controller
{
  private readonly AppDbContext _context;
  private readonly IWebHostEnvironment _env;

  public DeepLinkController(AppDbContext context, IWebHostEnvironment env)
  {
    _context = context;
    _env = env;
  }

  // GET /poi/42
  [HttpGet("{poiId:int}")]
  public async Task<IActionResult> Index(int poiId)
  {
    // Lấy thông tin POI để hiển thị trên Landing Page
    var poi = await _context.Pois
        .Where(p => p.Id == poiId && p.Status == PoiStatus.Active)
        .Select(p => new { p.Id, p.Name, p.Description, p.Address })
        .FirstOrDefaultAsync();

    // Đường dẫn APK — đặt file tại wwwroot/downloads/SmartTourGuide.apk
    var baseUrl = $"{Request.Scheme}://{Request.Host}";
    var apkUrl = $"{baseUrl}/downloads/SmartTourGuide.apk";
    var deepLinkScheme = $"smarttourguide://poi/{poiId}?autoplay=true";

    // Intent URL cho Android Chrome (fallback khi App Links chưa xác thực)
    var intentUrl =
        $"intent://poi/{poiId}?autoplay=true#Intent;" +
        $"scheme=smarttourguide;" +
        $"package=com.companyname.smarttourguide.mobile;" +
        $"S.browser_fallback_url={Uri.EscapeDataString($"{baseUrl}/poi/{poiId}")};" +
        $"end";

    // Universal Link trực tiếp cho iOS Safari
    var universalLink = $"{baseUrl}/poi/{poiId}";

    var html = BuildLandingHtml(
        poiId,
        poi?.Name ?? $"Điểm tham quan #{poiId}",
        poi?.Address ?? "",
        poi?.Description ?? "",
        deepLinkScheme,
        intentUrl,
        apkUrl,
        baseUrl
    );

    return Content(html, "text/html; charset=utf-8");
  }

  // ─── HTML Builder ──────────────────────────────────────────────────────
  private static string BuildLandingHtml(
      int poiId, string poiName, string address, string description,
      string deepLinkScheme, string intentUrl, string apkUrl, string baseUrl)
  {
    return $$"""
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="UTF-8"/>
  <meta name="viewport" content="width=device-width,initial-scale=1"/>
  <meta name="apple-itunes-app" content="app-id=YOUR_APPSTORE_ID, app-argument={{deepLinkScheme}}"/>
  <title>Smart Tour Guide – {{poiName}}</title>
  <style>
    *{margin:0;padding:0;box-sizing:border-box}
    body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;
         background:linear-gradient(135deg,#1a2a6c,#2a5298,#1a2a6c);
         min-height:100vh;display:flex;align-items:center;justify-content:center;
         padding:16px}
    .card{background:#fff;border-radius:24px;padding:32px 24px;
          max-width:400px;width:100%;text-align:center;
          box-shadow:0 20px 60px rgba(0,0,0,0.3)}
    .logo{font-size:48px;margin-bottom:8px}
    .app-name{font-size:13px;color:#888;letter-spacing:.5px;text-transform:uppercase;
              font-weight:600;margin-bottom:20px}
    .divider{width:40px;height:3px;background:linear-gradient(90deg,#2a5298,#4a90d9);
             border-radius:2px;margin:0 auto 20px}
    .poi-badge{background:#f0f6ff;border:1px solid #c9dff8;border-radius:12px;
               padding:12px 16px;margin-bottom:24px;text-align:left}
    .poi-badge .label{font-size:11px;color:#6b8dc4;font-weight:700;
                      text-transform:uppercase;letter-spacing:.5px}
    .poi-badge .poi-name{font-size:17px;font-weight:700;color:#1a2a6c;margin:4px 0 2px}
    .poi-badge .poi-addr{font-size:13px;color:#666}
    h2{font-size:20px;font-weight:700;color:#1a1a2e;margin-bottom:8px}
    p.sub{font-size:14px;color:#666;line-height:1.6;margin-bottom:24px}
    .btn{display:block;width:100%;padding:16px;border:none;border-radius:14px;
         font-size:16px;font-weight:700;cursor:pointer;text-decoration:none;
         margin-bottom:12px;transition:transform .1s,opacity .2s}
    .btn:active{transform:scale(.97)}
    .btn-primary{background:linear-gradient(135deg,#2a5298,#4a90d9);color:#fff}
    .btn-secondary{background:#f5f5f7;color:#1a1a2e}
    .btn-skip{background:none;color:#aaa;font-size:14px;font-weight:400;padding:12px}
    .step{display:flex;align-items:flex-start;gap:12px;margin-bottom:12px;text-align:left}
    .step-num{min-width:26px;height:26px;background:#2a5298;color:#fff;
              border-radius:50%;display:flex;align-items:center;justify-content:center;
              font-size:12px;font-weight:700;flex-shrink:0}
    .step-text{font-size:13px;color:#555;line-height:1.5;padding-top:4px}
    .steps-wrapper{background:#f9f9fb;border-radius:12px;padding:16px;margin-bottom:24px}
    .steps-title{font-size:12px;color:#999;text-transform:uppercase;
                 letter-spacing:.5px;font-weight:600;margin-bottom:12px;text-align:left}
    footer{margin-top:8px;font-size:12px;color:#ccc}
    .audio-pill{display:inline-flex;align-items:center;gap:6px;
                background:#e8f4ea;color:#2d7a38;border-radius:20px;
                padding:4px 12px;font-size:12px;font-weight:600;margin-bottom:16px}
  </style>
</head>
<body>
<div class="card">
  <div class="logo">🗺️</div>
  <div class="app-name">Smart Tour Guide</div>
  <div class="divider"></div>

  <div class="poi-badge">
    <div class="label">Điểm tham quan</div>
    <div class="poi-name">{{poiName}}</div>
    {{(string.IsNullOrEmpty(address) ? "" : $"<div class='poi-addr'>📍 {address}</div>")}}
  </div>

  <div class="audio-pill">🔊 Audio tự động phát khi mở App</div>

  <h2>Trải nghiệm đầy đủ hơn với App!</h2>
  <p class="sub">Cài đặt Smart Tour Guide để nghe thuyết minh tự động khi bạn đến gần địa điểm này.</p>

  <div class="steps-wrapper">
    <div class="steps-title">Sau khi cài đặt:</div>
    <div class="step">
      <div class="step-num">1</div>
      <div class="step-text">Tải và cài ứng dụng từ nút bên dưới</div>
    </div>
    <div class="step">
      <div class="step-num">2</div>
      <div class="step-text">Mở App, sau đó quét lại mã QR này</div>
    </div>
    <div class="step">
      <div class="step-num">3</div>
      <div class="step-text">App sẽ tự động mở trang <strong>{{poiName}}</strong> và phát audio 🎧</div>
    </div>
  </div>

  <!-- Nút chính: thử mở App trước (Intent), nếu chưa có thì tải APK -->
  <a id="btnInstall" href="{{apkUrl}}" class="btn btn-primary" download>
    ⬇️ Tải ứng dụng (Android APK)
  </a>

  <a href="/" class="btn btn-secondary">
    🍎 App Store (iOS) Coming soon
  </a>

  <button class="btn btn-skip" onclick="skipToWeb()">
    Bỏ qua, xem trên trình duyệt →
  </button>

  <footer>Smart Tour Guide © 2026</footer>
</div>

<script>
  // Thử mở App qua custom scheme / Intent trước khi hiện nút tải
  (function tryOpenApp() {
    var isAndroid = /Android/i.test(navigator.userAgent);
    var isIOS     = /iPhone|iPad/i.test(navigator.userAgent);
    var opened    = false;

    // Khi người dùng vừa vào trang → thử deep link tức thì
    var deepLink = isAndroid
      ? "{{intentUrl}}"
      : "{{deepLinkScheme}}";

    var frame = document.createElement('iframe');
    frame.style.display = 'none';
    frame.src = deepLink;
    document.body.appendChild(frame);

    // Nếu App mở được, trang sẽ bị ẩn (visibilitychange) → flag opened
    document.addEventListener('visibilitychange', function () {
      if (document.hidden) opened = true;
    });

    // Sau 1.5s, nếu App không mở được → để nguyên giao diện tải APK
    setTimeout(function () {
      document.body.removeChild(frame);
      if (opened) {
        // App đã mở → đóng tab này (tuỳ chọn)
        // window.close();
      }
    }, 1500);
  })();

  function skipToWeb() {
    // Ở đây có thể chuyển sang trang web tĩnh mô tả POI
    window.location.href = '{{baseUrl}}/api/pois/{{poiId}}';
  }
</script>
</body>
</html>
""";
  }
}
