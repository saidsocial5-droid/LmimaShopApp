using LmimaShopV2.Desktop.Config;

namespace LmimaShopV2.Desktop.Presentation.Services;

public interface IThemeService
{
    bool IsDark { get; }
    void Apply(string theme);
}

public sealed class ThemeService : IThemeService
{
    private readonly AppConfigService _config;

    public ThemeService(AppConfigService config) => _config = config;

    public bool IsDark => string.Equals(_config.Theme, "dark", StringComparison.OrdinalIgnoreCase);

    public void Apply(string theme)
    {
        var normalized = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var dictionaries = app.Resources.MergedDictionaries;
        var targetIndex = -1;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                targetIndex = i;
                break;
            }
        }

        var dictionary = new System.Windows.ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/LmimaShopV2.Desktop;component/Themes/{(normalized == "dark" ? "Dark" : "Light")}.xaml",
                UriKind.Absolute)
        };

        if (targetIndex >= 0) dictionaries[targetIndex] = dictionary;
        else dictionaries.Insert(0, dictionary);

        _config.Theme = normalized;
    }
}
