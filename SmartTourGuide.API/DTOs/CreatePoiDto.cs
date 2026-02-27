using Microsoft.AspNetCore.Http; // Cần cài gói Microsoft.AspNetCore.Http.Features nếu ở project Shared thuần túy, hoặc dùng byte[]

namespace SmartTourGuide.API.DTOs
{
    public class CreatePoiDto
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int OwnerId { get; set; } // ID người tạo
        public string? Address { get; set; }

        // Dữ liệu file upload (sẽ xử lý ở Controller)
        // Lưu ý: Trong Shared Library thường không để IFormFile. 
        // Nếu dùng chung model, tạm thời để các trường text, file xử lý riêng ở tham số API.
    }
}

