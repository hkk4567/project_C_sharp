namespace SmartTourGuide.Shared.Enums;

public enum UserRole
{
    // 0: Người dùng bình thường (Khách du lịch)
    // - Chỉ xem bản đồ, nghe thuyết minh, tracking vị trí
    Tourist = 0,

    // 1: Chủ gian hàng
    // - Được quyền tạo địa điểm (POI), upload ảnh/audio
    // - Xem danh sách địa điểm của mình
    BoothOwner = 1,

    // 2: Quản trị viên hệ thống
    // - Duyệt bài đăng của chủ shop
    // - Xem bản đồ nhiệt (Tracking toàn bộ user)
    Admin = 2
}