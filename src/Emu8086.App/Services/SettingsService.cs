using System.IO;
using System.Text.Json;

namespace Emu8086.App.Services;

public sealed class AppSettings
{
    public const string DefaultEditorFont = "Cascadia Mono";
    public const string DefaultLanguage = "en";

    public string Language { get; set; } = "";
    public string Theme { get; set; } = "Dark";
    public string EditorFontFamily { get; set; } = DefaultEditorFont;
    public double EditorFontSize { get; set; } = 15;
    public double UiFontSize { get; set; } = 13;
    public bool ShowLineNumbers { get; set; } = true;
    public bool WordWrap { get; set; }
    public int IndentSize { get; set; } = 4;
    public bool HighlightCurrentLine { get; set; } = true;
    public bool AutoComplete { get; set; } = true;
    /// <summary>Duration of one phase in the CPU visualizer animation (ms).</summary>
    public int VisualizerPhaseMs { get; set; } = 650;
    public bool CheckForUpdates { get; set; }
    /// <summary>Mirror ports without a built-in device to the shared emu8086.io file.</summary>
    public bool ExternalIo { get; set; }
    /// <summary>Delay between instructions in milliseconds; 0 runs at full speed.</summary>
    public int StepDelayMs { get; set; } = 0;
    public bool FirstRunDone { get; set; }
    public string? LastProject { get; set; }
    public List<string> RecentProjects { get; set; } = new();
    public double WindowWidth { get; set; } = 1500;
    public double WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }
}

public static class SettingsService
{
    private const int MaxRecent = 10;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = new();

    /// <summary>Raised when editor or appearance settings change so open views re-apply them.</summary>
    public static event Action? Changed;

    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>Resets editor and appearance settings, keeping projects, language and window placement.</summary>
    public static void ResetAppearance()
    {
        var defaults = new AppSettings();
        Current.EditorFontFamily = defaults.EditorFontFamily;
        Current.EditorFontSize = defaults.EditorFontSize;
        Current.UiFontSize = defaults.UiFontSize;
        Current.ShowLineNumbers = defaults.ShowLineNumbers;
        Current.WordWrap = defaults.WordWrap;
        Current.IndentSize = defaults.IndentSize;
        Current.HighlightCurrentLine = defaults.HighlightCurrentLine;
        Current.AutoComplete = defaults.AutoComplete;
        NotifyChanged();
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Current = new AppSettings();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UserDataDirectory);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, Options));
        }
        catch (IOException)
        {
            // Settings are a convenience; failing to save must not break the app.
        }
    }

    public static void AddRecent(string path)
    {
        Current.RecentProjects.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        Current.RecentProjects.Insert(0, path);
        if (Current.RecentProjects.Count > MaxRecent) Current.RecentProjects.RemoveRange(MaxRecent, Current.RecentProjects.Count - MaxRecent);
        Current.LastProject = path;
    }
}
