using System.IO;
using System.Text.Json;

namespace Emu8086.App.Services;

/// <summary>Application-wide constants read from config/app.json.</summary>
public sealed class AppConfig
{
    private static readonly Lazy<AppConfig> Instance = new(Load);

    public static AppConfig Current => Instance.Value;

    /// <summary>GitHub "owner/name" whose releases provide updates.</summary>
    public string UpdateRepository { get; init; } = "";

    private static AppConfig Load()
    {
        try
        {
            string path = Path.Combine(AppPaths.ConfigDirectory, "app.json");
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppConfig();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new AppConfig();
        }
    }
}
