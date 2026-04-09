// Thêm vào wwwroot/js/qr-helpers.js (Web project)
// Sau đó nhúng vào App.razor hoặc index.html: <script src="js/qr-helpers.js"></script>

/**
 * Tải file PNG từ base64 data URL
 * @param {string} dataUrl  - "data:image/png;base64,..."
 * @param {string} fileName - Tên file khi tải về
 */
window.downloadFile = function (dataUrl, fileName) {
  const link = document.createElement('a');
  link.href = dataUrl;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
};

/**
 * Mở cửa sổ in chứa ảnh QR + nhãn
 * @param {string} dataUrl - base64 image
 */
window.printQrCode = function (dataUrl) {
  const win = window.open('', '_blank', 'width=400,height=500');
  if (!win) return;
  win.document.write(`
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8"/>
  <title>QR Code – Smart Tour Guide</title>
  <style>
    body { font-family: sans-serif; text-align: center; padding: 24px; }
    img  { width: 280px; height: 280px; image-rendering: pixelated; display: block; margin: 0 auto 16px; }
    h2   { font-size: 18px; margin-bottom: 4px; }
    p    { font-size: 13px; color: #666; }
  </style>
</head>
<body>
  <img src="${dataUrl}" alt="QR Code"/>
  <h2>Smart Tour Guide</h2>
  <p>Quét mã để nghe thuyết minh tự động</p>
  <script>window.onload=function(){window.print();window.close();}<\/script>
</body>
</html>`);
  win.document.close();
};
