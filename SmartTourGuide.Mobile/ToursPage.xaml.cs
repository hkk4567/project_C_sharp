using SmartTourGuide.Mobile.Services;

namespace SmartTourGuide.Mobile;

using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Controls.Shapes;
using SmartTourGuide.Mobile.Models;
using SmartTourGuide.Mobile.Resources.Strings;

using MauiColor = Microsoft.Maui.Graphics.Color;
using MapsuiColor = Mapsui.Styles.Color;
public partial class ToursPage : ContentPage
{
    private readonly PoiApiService _apiService;
    private readonly MainPage _mainPage;
    private const string BaseApiUrl = "http://10.0.2.2:5277";

    private bool _isBusy = false;
    private string _currentLanguageCode = "vi-VN";

    // Tour đang được xem chi tiết trong overlay
    private TourModel? _previewTour = null;

    public ToursPage(MainPage mainPage)
    {
        InitializeComponent();
        _apiService = new PoiApiService();
        _mainPage = mainPage;
        _currentLanguageCode = Preferences.Get("AppLanguage", "vi-VN");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var tours = await _apiService.GetToursAsync();

            if (tours == null || !tours.Any())
            {
                await DisplayAlertAsync(AppResources.AlertInfo,
                    AppResources.NoToursAvailable, AppResources.OkButton);
                cvTours.ItemsSource = null;
                return;
            }

            // Chuẩn hóa URL ảnh bìa
            foreach (var t in tours)
            {
                if (!string.IsNullOrEmpty(t.ThumbnailUrl))
                {
                    var clean = t.ThumbnailUrl.Replace("\\", "/").TrimStart('/');
                    t.ThumbnailUrl = $"{BaseApiUrl}/{clean}";
                }
            }

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

    // ── BACK ──────────────────────────────────────────────────────────────────
    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (_isBusy) return;
        CloseItinerary();
        await Navigation.PopModalAsync();
    }

    // ── MỞ OVERLAY LỘ TRÌNH KHI BẤM "Xem lộ trình ›" ───────────────────────
    private async void OnViewItineraryClicked(object? sender, EventArgs e)
    {
        if (_isBusy || sender is not Button btn) return;
        if (btn.CommandParameter is not TourModel selectedTour) return;

        try
        {
            _isBusy = true;

            // Lấy chi tiết POI của tour
            var tourDetail = await _apiService.GetTourDetailsAsync(selectedTour.Id);
            if (tourDetail == null || tourDetail.Pois.Count == 0)
            {
                await DisplayAlertAsync(AppResources.AlertInfo, "Tour chưa có điểm dừng nào.", "OK");
                return;
            }

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

    // ── HIỆN OVERLAY CHI TIẾT LỘ TRÌNH ──────────────────────────────────────
    private void ShowItineraryOverlay(TourModel tour)
    {
        lblOverlayTourName.Text = tour.Name ?? "Tour";

        var orderedPois = tour.Pois.OrderBy(p => p.OrderIndex).ToList();
        int total = orderedPois.Count;

        itineraryPoiList.Children.Clear();

        for (int i = 0; i < orderedPois.Count; i++)
        {
            var poi = orderedPois[i];
            bool isFirst = i == 0;
            bool isLast = i == total - 1;

            // ── Dòng POI ──────────────────────────────────────────────────
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = 36 },   // icon + line
                    new ColumnDefinition { Width = GridLength.Star }
                },
                Margin = new Thickness(0, 0, 0, 0)
            };

            // Cột trái: timeline (circle + line)
            var timelineCol = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Star }
                }
            };

            // Circle với số thứ tự
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

            // Đường kẻ dọc nối với điểm tiếp theo
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

            // Cột phải: thông tin POI
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

            // Badge: Bắt đầu / Kết thúc
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
                        Text = isFirst ? "🚩 Xuất phát" : "🏁 Kết thúc",
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

        // Hiện overlay
        dimOverlay.IsVisible = true;
        itineraryPanel.IsVisible = true;
    }

    // ── ĐÓNG OVERLAY ─────────────────────────────────────────────────────────
    private void CloseItinerary()
    {
        dimOverlay.IsVisible = false;
        itineraryPanel.IsVisible = false;
        _previewTour = null;
    }

    private void OnCloseItineraryClicked(object? sender, EventArgs e) => CloseItinerary();
    private void OnDimOverlayTapped(object? sender, TappedEventArgs e) => CloseItinerary();

    // ── CHỌN TOUR → VẼ LÊN BẢN ĐỒ ──────────────────────────────────────────
    private async void OnSelectTourClicked(object? sender, EventArgs e)
    {
        if (_isBusy || _previewTour == null) return;
        try
        {
            _isBusy = true;
            var tourToRender = _previewTour;
            CloseItinerary();

            // Đóng modal trước
            await Navigation.PopModalAsync();

            // Gửi message để MainPage render
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