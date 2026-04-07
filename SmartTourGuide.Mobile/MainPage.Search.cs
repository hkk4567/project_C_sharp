using System.Globalization;
using System.Text;

namespace SmartTourGuide.Mobile;

public partial class MainPage
{
    private const int SearchSuggestionLimit = 6;
    private readonly System.Collections.ObjectModel.ObservableCollection<PoiModel> _poiSearchSuggestions = new();
    private bool _isUpdatingPoiSearchText;

    private void InitializePoiSearchUi()
    {
        if (PoiSearchBarCtrl != null)
            PoiSearchBarCtrl.Placeholder = GetSearchPlaceholder(_currentLanguageCode);

        if (PoiSearchSuggestionsCtrl != null)
            PoiSearchSuggestionsCtrl.ItemsSource = _poiSearchSuggestions;

        HidePoiSearchSuggestions();
    }

    private static string GetSearchPlaceholder(string langCode)
    {
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
        if (_isUpdatingPoiSearchText)
            return;

        UpdatePoiSearchSuggestions(e.NewTextValue);
    }

    private void OnPoiSearchSubmitted(object? sender, EventArgs e)
    {
        var query = PoiSearchBarCtrl?.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            HidePoiSearchSuggestions();
            return;
        }

        UpdatePoiSearchSuggestions(query);
        var firstMatch = _poiSearchSuggestions.FirstOrDefault();
        if (firstMatch != null)
            SelectPoiFromSearch(firstMatch);
    }

    private void OnPoiSearchSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is PoiModel poi)
            SelectPoiFromSearch(poi);

        if (PoiSearchSuggestionsCtrl != null)
            PoiSearchSuggestionsCtrl.SelectedItem = null;
    }

    private void UpdatePoiSearchSuggestions(string? query)
    {
        if (_allPoisCache.Count == 0)
        {
            HidePoiSearchSuggestions();
            return;
        }

        var normalizedQuery = NormalizeSearchText(query);
        _poiSearchSuggestions.Clear();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            HidePoiSearchSuggestions();
            return;
        }

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

        foreach (var poi in results)
            _poiSearchSuggestions.Add(poi);

        if (SearchSuggestionsPanelCtrl != null)
            SearchSuggestionsPanelCtrl.IsVisible = _poiSearchSuggestions.Count > 0;
    }

    private void HidePoiSearchSuggestions()
    {
        if (SearchSuggestionsPanelCtrl != null)
            SearchSuggestionsPanelCtrl.IsVisible = false;

        if (PoiSearchSuggestionsCtrl != null)
            PoiSearchSuggestionsCtrl.SelectedItem = null;
    }

    private void SelectPoiFromSearch(PoiModel poi)
    {
        HidePoiSearchSuggestions();

        if (PoiSearchBarCtrl != null && !string.IsNullOrWhiteSpace(poi.Name))
        {
            _isUpdatingPoiSearchText = true;
            PoiSearchBarCtrl.Text = poi.Name;
            _isUpdatingPoiSearchText = false;
        }

        PoiSearchBarCtrl?.Unfocus();
        FocusPoiOnMap(poi);
        ShowPoiDetail(poi);
    }

    private void FocusPoiOnMap(PoiModel poi)
    {
        var mapView = MapViewCtrl;
        if (mapView?.Map == null)
            return;

        var smc = SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
        var point = new MPoint(smc.x, smc.y);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            mapView.Map.Navigator.CenterOnAndZoomTo(point, 1.5, duration: 500);
        });
    }

    private static string NormalizeSearchText(string? text)
    {
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