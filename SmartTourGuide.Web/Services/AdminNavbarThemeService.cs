using Blazored.LocalStorage;

namespace SmartTourGuide.Web.Services;

public sealed class AdminNavbarThemeService
{
    private const string StorageKey = "admin.navbar.color";
    public const string DefaultColorHex = "#1976D2";

    private readonly ILocalStorageService _localStorage;
    private bool _loaded;

    public event Action? ThemeChanged;

    public string CurrentColorHex { get; private set; } = DefaultColorHex;

    public AdminNavbarThemeService(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        _loaded = true;

        var storedColor = await _localStorage.GetItemAsync<string>(StorageKey);
        if (TryNormalizeHex(storedColor, out var normalized))
            CurrentColorHex = normalized;
        else
            CurrentColorHex = DefaultColorHex;

        ThemeChanged?.Invoke();
    }

    public async Task SetColorAsync(int red, int green, int blue)
    {
        CurrentColorHex = ToHex(red, green, blue);
        _loaded = true;
        await _localStorage.SetItemAsync(StorageKey, CurrentColorHex);
        ThemeChanged?.Invoke();
    }

    public async Task ResetAsync()
    {
        CurrentColorHex = DefaultColorHex;
        _loaded = true;
        await _localStorage.RemoveItemAsync(StorageKey);
        ThemeChanged?.Invoke();
    }

    public static string ToHex(int red, int green, int blue)
    {
        red = Math.Clamp(red, 0, 255);
        green = Math.Clamp(green, 0, 255);
        blue = Math.Clamp(blue, 0, 255);

        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    public static (int Red, int Green, int Blue) FromHex(string? hex)
    {
        if (!TryNormalizeHex(hex, out var normalized))
            return (25, 118, 210);

        var red = Convert.ToInt32(normalized.Substring(1, 2), 16);
        var green = Convert.ToInt32(normalized.Substring(3, 2), 16);
        var blue = Convert.ToInt32(normalized.Substring(5, 2), 16);
        return (red, green, blue);
    }

    private static bool TryNormalizeHex(string? hex, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var value = hex.Trim();
        if (!value.StartsWith('#'))
            value = "#" + value;

        if (value.Length != 7)
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
                return false;
        }

        normalized = value.ToUpperInvariant();
        return true;
    }
}