using Microsoft.AspNetCore.Hosting; // Đảm bảo có namespace này
using Microsoft.AspNetCore.Http;

namespace SmartTourGuide.API.Services;

public class FileStorageService
{
    private readonly IWebHostEnvironment _env;

    public FileStorageService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<string> SaveFileAsync(IFormFile file, string folderName)
    {
        // 1. Kiểm tra WebRootPath, nếu null thì lấy ContentRootPath + "wwwroot"
        string webRootPath = _env.WebRootPath;

        if (string.IsNullOrEmpty(webRootPath))
        {
            // Fallback: Tự định nghĩa đường dẫn wwwroot nếu hệ thống chưa nhận diện
            webRootPath = Path.Combine(_env.ContentRootPath, "wwwroot");
        }

        // 2. Tạo đường dẫn lưu file: wwwroot/uploads/audio/
        var uploadPath = Path.Combine(webRootPath, "uploads", folderName);

        // Tạo thư mục nếu chưa tồn tại
        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(uploadPath);
        }

        // 3. Tạo tên file ngẫu nhiên để tránh trùng
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(uploadPath, fileName);

        // 4. Lưu file
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // 5. Trả về đường dẫn URL (ví dụ: /uploads/audio/abc.mp3)
        return $"/uploads/{folderName}/{fileName}";
    }
}