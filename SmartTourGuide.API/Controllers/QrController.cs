using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace SmartTourGuide.API.Controllers;

/// <summary>
/// Sinh ảnh QR Code cho một POI.
/// GET /api/qr/{poiId}?tunnelUrl=https://abc123.devtunnels.ms
///   → trả về PNG (320×320)
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class QrController : ControllerBase
{
    private readonly ILogger<QrController> _logger;

    public QrController(ILogger<QrController> logger)
    {
        _logger = logger;
    }

    // GET api/qr/42?tunnelUrl=https://abc123.devtunnels.ms
    [HttpGet("{poiId:int}")]
    public IActionResult GenerateQr(int poiId, [FromQuery] string tunnelUrl)
    {
        if (string.IsNullOrWhiteSpace(tunnelUrl))
            return BadRequest("tunnelUrl is required.");

        // Chuẩn hoá URL (bỏ dấu / ở cuối, đảm bảo HTTPS)
        tunnelUrl = tunnelUrl.TrimEnd('/');
        if (!tunnelUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return BadRequest("tunnelUrl phải dùng HTTPS (Dev Tunnel / Ngrok đều mặc định HTTPS).");

        // Deep-link URL được nhúng vào QR
        // → Android App Links hoặc iOS Universal Links sẽ bắt URL này
        // → Nếu chưa cài App, trình duyệt mở trang Landing Page tại cùng endpoint
        var deepLinkUrl = $"{tunnelUrl}/poi/{poiId}";

        _logger.LogInformation("Generating QR for POI {PoiId} → {Url}", poiId, deepLinkUrl);

        try
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(deepLinkUrl, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrData);

            // pixelsPerModule=10 → ảnh 330px (33 modules × 10px)
            var pngBytes = qrCode.GetGraphic(10);

            Response.Headers["X-Deep-Link-Url"] = deepLinkUrl;
            return File(pngBytes, "image/png", $"qr-poi-{poiId}.png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QRCoder error for POI {PoiId}", poiId);
            return StatusCode(500, "Không thể tạo QR Code.");
        }
    }
}
