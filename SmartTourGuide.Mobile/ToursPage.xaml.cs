using SmartTourGuide.Mobile.Services;

namespace SmartTourGuide.Mobile;

using CommunityToolkit.Mvvm.Messaging; // Thêm namespace này
using SmartTourGuide.Mobile.Models;
public partial class ToursPage : ContentPage
{
    private readonly PoiApiService _apiService;
    private readonly MainPage _mainPage;
    private const string BaseApiUrl = "http://10.0.2.2:5277";

    // BIẾN QUẢN LÝ TRẠNG THÁI
    private bool _isBusy = false;

    public ToursPage(MainPage mainPage)
    {
        InitializeComponent();
        _apiService = new PoiApiService();
        _mainPage = mainPage;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            // 1. Gọi API lấy danh sách Tour
            var tours = await _apiService.GetToursAsync();

            // 2. Kiểm tra dữ liệu trống (Tránh để màn hình trắng xóa mà không báo gì)
            if (tours == null || !tours.Any())
            {
                await DisplayAlertAsync("Thông báo", "Hiện tại chưa có tour nào được cập nhật.", "OK");
                cvTours.ItemsSource = null;
                return;
            }

            // 3. Xử lý chuẩn hóa URL ảnh
            foreach (var t in tours)
            {
                if (!string.IsNullOrEmpty(t.ThumbnailUrl))
                {
                    // Xử lý cả trường hợp path bắt đầu bằng / hoặc \
                    var cleanPath = t.ThumbnailUrl.Replace("\\", "/").TrimStart('/');
                    t.ThumbnailUrl = $"{BaseApiUrl}/{cleanPath}";
                }
            }

            // 4. Cập nhật giao diện
            cvTours.ItemsSource = tours;
            cvTours.IsVisible = true;
        }
        catch (HttpRequestException ex)
        {
            // Lỗi kết nối mạng hoặc server không phản hồi
            await DisplayAlertAsync("Lỗi kết nối", "Không thể kết nối đến máy chủ. Vui lòng kiểm tra mạng hoặc địa chỉ API.", $"Đã xảy ra lỗi: {ex.Message}", "Thử lại");
        }
        catch (Exception ex)
        {
            // Lỗi phát sinh khác (Lỗi code, lỗi parse dữ liệu...)
            await DisplayAlertAsync("Lỗi hệ thống", $"Đã xảy ra lỗi: {ex.Message}", "Đóng");

            // Ghi log ra màn hình Debug để lập trình viên dễ theo dõi
            System.Diagnostics.Debug.WriteLine($"[ToursPage Error] {ex.StackTrace}");
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (_isBusy) return;
        await Navigation.PopModalAsync();
    }

    private async void OnTourSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isBusy) return;

        if (e.CurrentSelection.FirstOrDefault() is TourModel selectedTour)
        {
            try
            {
                _isBusy = true;
                var tourDetail = await _apiService.GetTourDetailsAsync(selectedTour.Id);

                if (tourDetail != null && tourDetail.Pois.Count > 0)
                {
                    // 1. Đóng trang Modal trước
                    await Navigation.PopModalAsync();

                    // 2. Gửi bản tin thông báo đã chọn Tour
                    WeakReferenceMessenger.Default.Send(new SelectTourMessage(tourDetail));
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Lỗi", ex.Message, "OK");
            }
            finally { _isBusy = false; }
        }
    }
}