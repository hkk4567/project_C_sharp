using System.Globalization;
using System.Text;

namespace SmartTourGuide.Mobile;

// Muc dich file:
// - Quan ly tim kiem POI theo ten/dia chi/mo ta.
// - Goi y ket qua theo diem uu tien (score).
// - Dieu huong ban do va mo popup khi nguoi dung chon ket qua.
public partial class MainPage
{
    // So goi y toi da hien thi trong panel tim kiem.
    private const int SearchSuggestionLimit = 6;
    // Danh sach goi y bind truc tiep len CollectionView.
    private readonly System.Collections.ObjectModel.ObservableCollection<PoiModel> _poiSearchSuggestions = new();
    // Co bao ve tranh lap event khi tu dong gan Text vao SearchBar.
    private bool _isUpdatingPoiSearchText;

    private void InitializePoiSearchUi()
    {
        // 1) Dat placeholder phu hop theo ngon ngu hien tai.
        if (PoiSearchBarCtrl != null)
            PoiSearchBarCtrl.Placeholder = GetSearchPlaceholder(_currentLanguageCode);

        // 2) Gan nguon du lieu goi y cho danh sach.
        if (PoiSearchSuggestionsCtrl != null)
            PoiSearchSuggestionsCtrl.ItemsSource = _poiSearchSuggestions;

        // 3) Khoi tao trang thai an panel goi y.
        HidePoiSearchSuggestions();
    }

    private static string GetSearchPlaceholder(string langCode)
    {
        // Tra ve placeholder theo ma ngon ngu.
        return langCode switch
        {
            "en-US" => "Search places, cafes...",
            "zh-CN" => "搜索地点、咖啡馆...",
            "ja-JP" => "場所や店を検索...",
            "fr-FR" => "Rechercher un lieu, un café...",
            "ko-KR" => "장소, 카페 검색...",
            _ => "Tìm quán, địa điểm...",
        };
    }

    private void OnPoiSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Bo qua su kien phat sinh khi dang cap nhat Text bang code.
        if (_isUpdatingPoiSearchText)
            return;

        // Cap nhat goi y theo chuoi moi.
        UpdatePoiSearchSuggestions(e.NewTextValue);
    }

    private void OnPoiSearchSubmitted(object? sender, EventArgs e)
    {
        // 1) Lay tu khoa hien tai trong SearchBar.
        var query = PoiSearchBarCtrl?.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            // Rong -> an goi y va dung.
            HidePoiSearchSuggestions();
            return;
        }

        // 2) Cap nhat goi y va tu dong chon ket qua dau tien neu co.
        UpdatePoiSearchSuggestions(query);
        var firstMatch = _poiSearchSuggestions.FirstOrDefault();
        if (firstMatch != null)
            SelectPoiFromSearch(firstMatch);
    }

    private void OnPoiSearchSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Chon POI tu danh sach goi y.
        if (e.CurrentSelection.FirstOrDefault() is PoiModel poi)
            SelectPoiFromSearch(poi);

        // Reset SelectedItem de lan chon sau van bat event.
        if (PoiSearchSuggestionsCtrl != null)
            PoiSearchSuggestionsCtrl.SelectedItem = null;
    }

    private void UpdatePoiSearchSuggestions(string? query)
    {
        // 1) Khong co cache POI thi khong the tim kiem.
        if (_allPoisCache.Count == 0)
        {
            HidePoiSearchSuggestions();
            return;
        }

        // 2) Chuan hoa chuoi tim kiem (bo dau, lowercase) de tim nhat quan.
        var normalizedQuery = NormalizeSearchText(query);
        _poiSearchSuggestions.Clear();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            // Chuoi rong -> an panel goi y.
            HidePoiSearchSuggestions();
            return;
        }

        // 3) Tinh diem tung POI, loc ket qua hop le, sap xep theo score va ten.
        var results = _allPoisCache
            .Select(poi => new
            {
                Poi = poi,
                Score = GetSearchScore(poi, normalizedQuery)
            })
            .Where(x => x.Score < int.MaxValue)
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Poi.Name)
            .Take(SearchSuggestionLimit)
            .Select(x => x.Poi)
            .ToList();

        // 4) Day ket qua vao ObservableCollection de cap nhat UI.
        foreach (var poi in results)
            _poiSearchSuggestions.Add(poi);

        // 5) Hien panel neu co ket qua.
        if (SearchSuggestionsPanelCtrl != null)
            SearchSuggestionsPanelCtrl.IsVisible = _poiSearchSuggestions.Count > 0;
    }

    private void HidePoiSearchSuggestions()
    {
        // An panel goi y.
        if (SearchSuggestionsPanelCtrl != null)
            SearchSuggestionsPanelCtrl.IsVisible = false;

        // Bo chon item de UI sach trang thai.
        if (PoiSearchSuggestionsCtrl != null)
            PoiSearchSuggestionsCtrl.SelectedItem = null;
    }

    private void SelectPoiFromSearch(PoiModel poi)
    {
        // 1) Dong panel goi y.
        HidePoiSearchSuggestions();

        // 2) Dong bo text SearchBar theo POI vua chon.
        if (PoiSearchBarCtrl != null && !string.IsNullOrWhiteSpace(poi.Name))
        {
            _isUpdatingPoiSearchText = true;
            PoiSearchBarCtrl.Text = poi.Name;
            _isUpdatingPoiSearchText = false;
        }

        // 3) Bo focus SearchBar, di chuyen camera den POI va mo chi tiet.
        PoiSearchBarCtrl?.Unfocus();
        FocusPoiOnMap(poi);
        ShowPoiDetail(poi);
    }

    private void FocusPoiOnMap(PoiModel poi)
    {
        // Khong co map thi bo qua.
        var mapView = MapViewCtrl;
        if (mapView?.Map == null)
            return;

        // Chuyen toa do sang he map projection truoc khi center.
        var smc = SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
        var point = new MPoint(smc.x, smc.y);

        // Thuc thi tren UI thread de dam bao an toan giao dien.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            mapView.Map.Navigator.CenterOnAndZoomTo(point, 1.5, duration: 500);
        });
    }

    private static string NormalizeSearchText(string? text)
    {
        // Chuan hoa: bo dau, doi ve chu thuong, giu lai ky tu co y nghia tim kiem.
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int GetSearchScore(PoiModel poi, string normalizedQuery)
    {
        // Score cang nho cang uu tien cao.
        // 0: ten bat dau bang query
        // 1: dia chi bat dau bang query
        // 2: ten chua query
        // 3: dia chi chua query
        // 4: mo ta chua query
        // MaxValue: khong khop
        var name = NormalizeSearchText(poi.Name);
        var address = NormalizeSearchText(poi.Address);
        var description = NormalizeSearchText(poi.Description);

        if (!string.IsNullOrWhiteSpace(name) && name.StartsWith(normalizedQuery, StringComparison.Ordinal))
            return 0;

        if (!string.IsNullOrWhiteSpace(address) && address.StartsWith(normalizedQuery, StringComparison.Ordinal))
            return 1;

        if (!string.IsNullOrWhiteSpace(name) && name.Contains(normalizedQuery, StringComparison.Ordinal))
            return 2;

        if (!string.IsNullOrWhiteSpace(address) && address.Contains(normalizedQuery, StringComparison.Ordinal))
            return 3;

        if (!string.IsNullOrWhiteSpace(description) && description.Contains(normalizedQuery, StringComparison.Ordinal))
            return 4;

        return int.MaxValue;
    }
}