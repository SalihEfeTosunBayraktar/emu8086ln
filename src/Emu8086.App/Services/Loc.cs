using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace Emu8086.App.Services;

public sealed record LanguageInfo(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// UI text lookup. All strings come from config/lang/&lt;code&gt;.json; switching the language
/// updates every binding immediately.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private const string NameKey = "_language";
    private const string FallbackCode = "en";

    public static Loc Instance { get; } = new();

    private Dictionary<string, string> _strings = new();
    private Dictionary<string, string> _fallback = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public string CurrentCode { get; private set; } = FallbackCode;

    public string this[string key] =>
        _strings.TryGetValue(key, out var s) ? s : _fallback.TryGetValue(key, out var f) ? f : $"[{key}]";

    public string Format(string key, params object?[] args)
    {
        try { return string.Format(this[key], args); }
        catch (FormatException) { return this[key]; }
    }

    public IReadOnlyList<LanguageInfo> Available()
    {
        if (!Directory.Exists(AppPaths.LanguageDirectory)) return [];
        return Directory.GetFiles(AppPaths.LanguageDirectory, "*.json")
            .Select(f => new LanguageInfo(Path.GetFileNameWithoutExtension(f), Read(f).GetValueOrDefault(NameKey, Path.GetFileNameWithoutExtension(f))))
            .OrderBy(l => l.Name)
            .ToList();
    }

    public void Load(string code)
    {
        _fallback = Read(Path.Combine(AppPaths.LanguageDirectory, FallbackCode + ".json"));
        string path = Path.Combine(AppPaths.LanguageDirectory, code + ".json");
        _strings = File.Exists(path) ? Read(path) : _fallback;
        CurrentCode = File.Exists(path) ? code : FallbackCode;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        LanguageChanged?.Invoke();
    }

    private static Dictionary<string, string> Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new();
        }
    }
}

/// <summary>XAML: Text="{svc:Tr menu.file}" binds to the localized string.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
