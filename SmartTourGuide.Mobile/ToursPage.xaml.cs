using SmartTourGuide.Mobile.Services;

namespace SmartTourGuide.Mobile;

using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Controls.Shapes;
using SmartTourGuide.Mobile.Models;
using SmartTourGuide.Mobile.Resources.Strings;

using MauiColor = Microsoft.Maui.Graphics.Color;
using MapsuiColor = Mapsui.Styles.Color;

// Mục đích file:
// 1) Tải danh sách tour và hiển thị trên trang chọn tour.
// 2) Mở overlay xem trước lộ trình chi tiết của tour.
// 3) Trả tour đã chọn về MainPage để render trên bản đồ.
public partial class ToursPage : ContentPage
{
    // Service gọi API tour/chi tiết tour.
    private readonly PoiApiService _apiService;
    // Tham chiếu MainPage để điều hướng và phối hợp luồng chọn tour.
    private readonly MainPage _mainPage;
    private const string BaseApiUrl = "https://6b5j1b5h-7058.asse.devtunnels.ms";

    // Cờ chống thao tác chồng khi đang xử lý bất đồng bộ.
    private bool _isBusy = false;
    private string _currentLanguageCode = "vi-VN";

    // Tour đang được xem chi tiết trong overlay.
    private TourModel? _previewTour = null;

    public ToursPage(MainPage mainPage)
    {
        // 1) Khởi tạo UI.
        InitializeComponent();

        // 2) Khởi tạo dependency và đọc ngôn ngữ đã lưu.
        _apiService = new PoiApiService();
        _mainPage = mainPage;
        _currentLanguageCode = Preferences.Get("AppLanguage", "vi-VN");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            // 1) Tải danh sách tour từ server.
            var tours = await _apiService.GetToursAsync();

            if (tours == null || !tours.Any())
            {
                // 2) Không có dữ liệu tour: báo cho người dùng.
                await DisplayAlertAsync(AppResources.AlertInfo,
                    AppResources.NoToursAvailable, AppResources.OkButton);
                cvTours.ItemsSource = null;
                return;
            }

            // 3) Chuẩn hóa URL thumbnail để hiển thị ảnh đúng đường dẫn tuyệt đối.
            foreach (var t in tours)
            {
                if (!string.IsNullOrEmpty(t.ThumbnailUrl))
                {
                    var clean = t.ThumbnailUrl.Replace("\\", "/").TrimStart('/');
                    t.ThumbnailUrl = $"{BaseApiUrl}/{clean}";
                }
            }

            // 4) Bind dữ liệu lên CollectionView.
            cvTours.ItemsSource = tours;
            cvTours.IsVisible = true;
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync(AppResources.ConnectionError,
                AppResources.ConnectionErrorMessage,
                $"Lỗi/error: {ex.Message}", AppResources.RetryButton);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(AppResources.SystemError,
                string.Format(AppResources.SystemErrorMessage, ex.Message),
                AppResources.CloseButton);
        }
    }

    // ── NÚT QUAY LẠI ─────────────────────────────────────────────────────────
    private async void OnBackClicked(object? sender, EventArgs e)
    {
        // Đóng overlay nếu còn mở rồi quay lại MainPage.
        if (_isBusy) return;
        CloseItinerary();
        await Navigation.PopModalAsync();
    }

    // ── MỞ OVERLAY LỘ TRÌNH KHI BẤM "Xem lộ trình" ─────────────────────────
    private async void OnViewItineraryClicked(object? sender, EventArgs e)
    {
        // 1) Chặn click chồng và đảm bảo command parameter hợp lệ.
        if (_isBusy || sender is not Button btn) return;
        if (btn.CommandParameter is not TourModel selectedTour) return;

        try
        {
            _isBusy = true;

            // 2) Lấy chi tiết POI của tour để dựng overlay timeline.
            var tourDetail = await _apiService.GetTourDetailsAsync(selectedTour.Id);
            if (tourDetail == null || tourDetail.Pois.Count == 0)
            {
                await DisplayAlertAsync(AppResources.AlertInfo, AppResources.MsgNoStops, AppResources.OkButton);
                return;
            }

            // 3) Lưu tour preview hiện tại và hiển thị overlay.
            _previewTour = tourDetail;
            ShowItineraryOverlay(tourDetail);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(AppResources.AlertError,
                string.Format(AppResources.SystemErrorMessage, ex.Message), AppResources.OkButton);
        }
        finally { _isBusy = false; }
    }

    // ── HIỂN THỊ OVERLAY CHI TIẾT LỘ TRÌNH ─────────────────────────────────
    private void ShowItineraryOverlay(TourModel tour)
    {
        // 1) Cập nhật tên tour trên header overlay.
        lblOverlayTourName.Text = tour.Name ?? "Tour";

        // 2) Sắp xếp điểm dừng theo thứ tự hành trình.
        var orderedPois = tour.Pois.OrderBy(p => p.OrderIndex).ToList();
        int total = orderedPois.Count;

        // 3) Xóa nội dung cũ trước khi dựng lại.
        itineraryPoiList.Children.Clear();

        // 4) Dựng UI timeline từng điểm dừng.
        for (int i = 0; i < orderedPois.Count; i++)
        {
            var poi = orderedPois[i];
            bool isFirst = i == 0;
            bool isLast = i == total - 1;

            // Dòng hiển thị một POI trong timeline.
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = 36 },   // icon + line
                    new ColumnDefinition { Width = GridLength.Star }
                },
                Margin = new Thickness(0, 0, 0, 0)
            };

            // Cột trái: timeline (vòng tròn số + đường nối).
            var timelineCol = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Star }
                }
            };

            // Vòng tròn đánh số thứ tự điểm dừng.
            var circle = new Border
            {
                BackgroundColor = isFirst ? MauiColor.FromArgb("#4CAF50")
                                : isLast ? MauiColor.FromArgb("#F44336")
                                : MauiColor.FromArgb("#FF9800"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                WidthRequest = 28,
                HeightRequest = 28,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 8, 0, 0),
                Content = new Label
                {
                    Text = $"{i + 1}",
                    TextColor = Colors.White,
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            timelineCol.Children.Add(circle);
            Grid.SetRow(circle, 0);

            // Đường dọc nối sang điểm kế tiếp.
            if (!isLast)
            {
                var line = new BoxView
                {
                    Color = MauiColor.FromArgb("#E0E0E0"),
                    WidthRequest = 2,
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                timelineCol.Children.Add(line);
                Grid.SetRow(line, 1);
            }

            row.Children.Add(timelineCol);
            Grid.SetColumn(timelineCol, 0);

            // Cột phải: thông tin POI.
            var infoCol = new VerticalStackLayout
            {
                Padding = new Thickness(10, 8, 0, isLast ? 12 : 20),
                Spacing = 2
            };

            var nameLbl = new Label
            {
                Text = poi.PoiName ?? "",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = MauiColor.FromArgb("#212121"),
                LineBreakMode = LineBreakMode.TailTruncation
            };

            var addrLbl = new Label
            {
                Text = poi.Address ?? "",
                FontSize = 13,
                TextColor = MauiColor.FromArgb("#757575"),
                LineBreakMode = LineBreakMode.TailTruncation,
                IsVisible = !string.IsNullOrEmpty(poi.Address)
            };

            // Badge cho điểm bắt đầu/kết thúc để người dùng nhận diện nhanh.
            if (isFirst || isLast)
            {
                var badge = new Border
                {
                    BackgroundColor = isFirst ? MauiColor.FromArgb("#E8F5E9") : MauiColor.FromArgb("#FFEBEE"),
                    Stroke = isFirst ? MauiColor.FromArgb("#A5D6A7") : MauiColor.FromArgb("#FFCDD2"),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 8 },
                    Padding = new Thickness(8, 3),
                    HorizontalOptions = LayoutOptions.Start,
                    Content = new Label
                    {
                        Text = isFirst ? AppResources.BadgeStart : AppResources.BadgeEnd,
                        FontSize = 11,
                        TextColor = isFirst ? MauiColor.FromArgb("#388E3C") : MauiColor.FromArgb("#D32F2F"),
                        FontAttributes = FontAttributes.Bold
                    }
                };
                infoCol.Children.Add(badge);
            }

            infoCol.Children.Add(nameLbl);
            if (!string.IsNullOrEmpty(poi.Address))
                infoCol.Children.Add(addrLbl);

            row.Children.Add(infoCol);
            Grid.SetColumn(infoCol, 1);

            itineraryPoiList.Children.Add(row);
        }

        // 5) Bật lớp nền mờ và panel chi tiết.
        dimOverlay.IsVisible = true;
        itineraryPanel.IsVisible = true;
    }

    // ── ĐÓNG OVERLAY ────────────────────────────────────────────────────────
    private void CloseItinerary()
    {
        // Ẩn overlay và xóa trạng thái tour đang preview.
        dimOverlay.IsVisible = false;
        itineraryPanel.IsVisible = false;
        _previewTour = null;
    }

    private void OnCloseItineraryClicked(object? sender, EventArgs e) => CloseItinerary();
    private void OnDimOverlayTapped(object? sender, TappedEventArgs e) => CloseItinerary();

    // ── CHỌN TOUR -> GỬI VỀ MAINPAGE ĐỂ RENDER ────────────────────────────
    private async void OnSelectTourClicked(object? sender, EventArgs e)
    {
        if (_isBusy || _previewTour == null) return;
        try
        {
            _isBusy = true;

            // 1) Lưu tour được chọn trước khi đóng overlay.
            var tourToRender = _previewTour;
            CloseItinerary();

            // 2) Đóng modal ToursPage.
            await Navigation.PopModalAsync();

            // 3) Gửi message để MainPage nhận và render tour.
            WeakReferenceMessenger.Default.Send(new SelectTourMessage(tourToRender));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(AppResources.AlertError,
                string.Format(AppResources.SystemErrorMessage, ex.Message), AppResources.OkButton);
        }
        finally { _isBusy = false; }
    }
}