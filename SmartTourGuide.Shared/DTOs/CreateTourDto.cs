namespace SmartTourGuide.Shared.DTOs;

public class CreateTourDto
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    // Danh sách ID các điểm tham quan được chọn
    // Ví dụ: [5, 12, 3] -> Điểm ID 5 đi trước, rồi đến 12, rồi đến 3
    public List<int> PoiIds { get; set; } = new();
}