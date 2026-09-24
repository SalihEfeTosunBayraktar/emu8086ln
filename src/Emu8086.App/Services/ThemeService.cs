using System.Windows;

namespace Emu8086.App.Services;

public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static string Current { get; private set; } = Dark;

    public static event Action? ThemeChanged;

    public static void Apply(string theme)
    {
        Current = theme == Light ? Light : Dark;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries[0] = new ResourceDictionary { Source = new Uri($"Themes/{Current}.xaml", UriKind.Relative) };
        ThemeChanged?.Invoke();
    }

    public static void Toggle()
    {
        Apply(Current == Dark ? Light : Dark);
        SettingsService.Current.Theme = Current;
    }

    public static T Resource<T>(string key) where T : class =>
        (T)Application.Current.FindResource(key);
}
