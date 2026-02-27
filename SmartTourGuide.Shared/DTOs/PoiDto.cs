namespace SmartTourGuide.Shared.DTOs
{
    public class PoiDto
    {
        public int Id { get; set; }

        // Sử dụng 'required' để bắt buộc phải có giá trị khi khởi tạo và hết cảnh báo CS8618
        public required string Name { get; set; }

        public required string Description { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public required string Address { get; set; }

        // Mặc định trạng thái là 'Pending' nếu không được gán
        public string Status { get; set; } = "Pending";

        // Slide 2: Cấu hình Geofence (Bán kính tính bằng mét)
        public double TriggerRadius { get; set; }

        public int Priority { get; set; }

        // Slide 3 & 4: Danh sách file (Khởi tạo sẵn list trống để tránh null reference)
        public List<string> AudioUrls { get; set; } = new();

        public List<string> ImageUrls { get; set; } = new();
    }
}

