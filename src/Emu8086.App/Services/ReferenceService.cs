using System.IO;
using System.Text.Json;

namespace Emu8086.App.Services;

public sealed record ReferenceEntry(string Name, string Category, string Syntax, string Description, string Flags, string Example);

/// <summary>
/// Instruction / register / directive reference from config/reference/&lt;lang&gt;.json,
/// shared by the reference window and the editor's completion help.
/// </summary>
public static class ReferenceService
{
    private static readonly char[] NameSeparators = [' ', '/', ',', '(', ')'];
    private static List<ReferenceEntry>? _entries;
    private static Dictionary<string, ReferenceEntry>? _byKeyword;
    private static string? _language;

    public static IReadOnlyList<ReferenceEntry> Entries
    {
        get
        {
            EnsureLoaded();
            return _entries!;
        }
    }

    /// <summary>Entry describing a keyword (e.g. "JNZ" finds "JE / JZ, JNE / JNZ"), or null.</summary>
    public static ReferenceEntry? Find(string keyword)
    {
        EnsureLoaded();
        return _byKeyword!.GetValueOrDefault(keyword);
    }

    private static void EnsureLoaded()
    {
        if (_entries != null && _language == Loc.Instance.CurrentCode) return;
        _language = Loc.Instance.CurrentCode;
        _entries = Load();
        _byKeyword = new Dictionary<string, ReferenceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries)
        {
            // Interrupt entries ("INT 21h / AH=09h") must not claim plain keywords like INT.
            if (entry.Name.Contains('=')) continue;
            foreach (string word in entry.Name.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries))
                _byKeyword.TryAdd(word, entry);
        }
    }

    private static List<ReferenceEntry> Load()
    {
        foreach (var lang in new[] { Loc.Instance.CurrentCode, "en" })
        {
            string path = Path.Combine(AppPaths.ConfigDirectory, "reference", lang + ".json");
            if (!File.Exists(path)) continue;
            try
            {
                return JsonSerializer.Deserialize<List<ReferenceEntry>>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
        return [];
    }
}
