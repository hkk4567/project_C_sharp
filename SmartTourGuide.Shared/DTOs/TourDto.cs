namespace SmartTourGuide.Shared.DTOs;

public class TourDto
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string ThumbnailUrl { get; set; }
    public int TotalPois { get; set; } // Tổng số điểm tham quan

    // Danh sách các điểm bên trong (đã sắp xếp)
    public List<TourDetailDto> Pois { get; set; } = new();
}

public class TourDetailDto
{
    public int PoiId { get; set; }
    public required string PoiName { get; set; }
    public required string Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int OrderIndex { get; set; } // Thứ tự 1, 2, 3
}