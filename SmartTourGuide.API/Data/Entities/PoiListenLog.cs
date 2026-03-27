using System.ComponentModel.DataAnnotations;

namespace SmartTourGuide.API.Data.Entities
{
    /// <summary>
    /// Bảng lưu lịch sử nghe audio tại từng POI.
    /// Mỗi record = 1 lần user nghe (dù hết hay dừng giữa chừng).
    /// Cùng một thiết bị có thể tạo nhiều record cho cùng một POI.
    /// Dùng để tính: Top POI + Thời gian trung bình nghe.
    /// </summary>
    public class PoiListenLog
    {
        [Key]
        public long Id { get; set; }
         // ID địa điểm đang nghe
        public int PoiId { get; set; }
        // ID ẩn danh của thiết bị (GUID, không cần đăng nhập)
        public string DeviceId { get; set; } = string.Empty;
        public int ListenDurationSec { get; set; } // Số giây đã nghe (dù hết hay bấm dừng giữa chừng)
        public DateTime Timestamp { get; set; }  // Thời điểm ghi log
    }
}