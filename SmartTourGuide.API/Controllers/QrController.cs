using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace SmartTourGuide.API.Controllers;

// File này dùng để sinh QR Code cho POI.
// - Nhận PoiId và tunnelUrl
// - Tạo deep link nhúng vào mã QR
// - Trả ảnh PNG để in hoặc hiển thị trên màn hình

/// <summary>
/// Sinh ảnh QR Code cho một POI.
/// GET /api/qr/{poiId}?tunnelUrl=https://abc123.devtunnels.ms
///   → trả về PNG (320×320)
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class QrController : ControllerBase
{
    // Logger dùng để ghi nhận quá trình sinh QR và lỗi nếu có.
    private readonly ILogger<QrController> _logger;

    public QrController(ILogger<QrController> logger)
    {
        _logger = logger;
    }

    // GET api/qr/42?tunnelUrl=https://abc123.devtunnels.ms
    [HttpGet("{poiId:int}")]
    public IActionResult GenerateQr(int poiId, [FromQuery] string tunnelUrl)
    {
        // Bắt buộc phải có tunnelUrl để tạo đường dẫn deep link hợp lệ.
        if (string.IsNullOrWhiteSpace(tunnelUrl))
            return BadRequest("tunnelUrl is required.");

        // Chuẩn hóa URL đầu vào trước khi ghép thành deep link.
        tunnelUrl = tunnelUrl.TrimEnd('/');
        if (!tunnelUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return BadRequest("tunnelUrl phải dùng HTTPS (Dev Tunnel / Ngrok đều mặc định HTTPS).");

        // URL này sẽ được nhúng vào QR để App Links / Universal Links có thể bắt xử lý.
        var deepLinkUrl = $"{tunnelUrl}/poi/{poiId}";

        // Ghi log phục vụ debug khi cần đối chiếu QR đã tạo ra trỏ tới đâu.
        _logger.LogInformation("Generating QR for POI {PoiId} → {Url}", poiId, deepLinkUrl);

        try
        {
            // Tạo dữ liệu QR với mức sửa lỗi cao để tăng khả năng quét được.
            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(deepLinkUrl, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrData);

            // Sinh ảnh PNG từ dữ liệu QR.
            var pngBytes = qrCode.GetGraphic(10);

            // Trả thêm header để client/debug tool biết URL deep link bên trong QR.
            Response.Headers["X-Deep-Link-Url"] = deepLinkUrl;
            return File(pngBytes, "image/png", $"qr-poi-{poiId}.png");
        }
        catch (Exception ex)
        {
            // Ghi lỗi để dễ chẩn đoán khi thư viện QR không tạo được ảnh.
            _logger.LogError(ex, "QRCoder error for POI {PoiId}", poiId);
            return StatusCode(500, "Không thể tạo QR Code.");
        }
    }
}
