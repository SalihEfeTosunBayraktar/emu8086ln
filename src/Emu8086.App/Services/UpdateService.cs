using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Emu8086.App.Services;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string DownloadUrl, string PageUrl);

/// <summary>
/// Checks GitHub Releases for a newer version and installs it: the release zip is downloaded,
/// extracted, and a small script copies it over the install folder after this process exits.
/// </summary>
public static class UpdateService
{
    private const string AssetSuffix = "-win-x64.zip";
    private const string ExecutableName = "emu8086ln.exe";
    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : new Version(0, 0, 0);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"emu8086ln/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Returns the latest release if it is newer than this build, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        string url = $"https://api.github.com/repos/{AppConfig.Current.UpdateRepository}/releases/latest";
        using var response = await Http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = json.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= CurrentVersion) return null;

        string? download = root.GetProperty("assets").EnumerateArray()
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault(u => u != null && u.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase));
        if (download == null) return null;

        return new UpdateInfo(version, tag,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            download,
            root.GetProperty("html_url").GetString() ?? "");
    }

    /// <summary>Downloads and extracts the update, then starts the installer script.</summary>
    /// <returns>True when the application should now exit so the files can be replaced.</returns>
    public static async Task<bool> DownloadAndInstallAsync(UpdateInfo update, IProgress<int> progress, CancellationToken token = default)
    {
        string work = Path.Combine(Path.GetTempPath(), "emu8086ln-update");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        string zipPath = Path.Combine(work, "update.zip");

        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? -1;
            await using var source = await response.Content.ReadAsStreamAsync(token);
            await using var target = File.Create(zipPath);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), token);
                done += read;
                if (total > 0) progress.Report((int)(done * 100 / total));
            }
        }

        string extracted = Path.Combine(work, "files");
        ZipFile.ExtractToDirectory(zipPath, extracted);
        string payload = FindPayload(extracted) ?? throw new InvalidDataException(ExecutableName);

        string installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string script = Path.Combine(work, "install.cmd");
        File.WriteAllText(script, InstallerScript(Environment.ProcessId, payload, installDir, work), Encoding.Default);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = work,
        });
        return true;
    }

    /// <summary>The folder inside the zip that contains the executable.</summary>
    private static string? FindPayload(string root) =>
        Directory.GetFiles(root, ExecutableName, SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .OrderBy(d => d!.Length)
            .FirstOrDefault();

    private static string InstallerScript(int pid, string source, string target, string work) => $"""
        @echo off
        :wait
        tasklist /FI "PID eq {pid}" 2>nul | find "{pid}" >nul && (timeout /t 1 /nobreak >nul & goto wait)
        robocopy "{source}" "{target}" /E /NFL /NDL /NJH /NJS /NP >nul
        start "" "{Path.Combine(target, ExecutableName)}"
        cd /d "%TEMP%"
        rmdir /s /q "{work}"
        """;
}
