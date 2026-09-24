using System.IO;
using System.Text.Json;

namespace Emu8086.App.Services;

/// <summary>An emu8086ln project: a folder with a *.e86proj file and its source files.</summary>
public sealed class Project
{
    public const string Extension = ".e86proj";

    public required string FilePath { get; init; }
    public required string Name { get; init; }
    public required string MainFile { get; set; }

    public string Directory => Path.GetDirectoryName(FilePath)!;
    public string MainFilePath => Path.Combine(Directory, MainFile);

    public IEnumerable<string> SourceFiles =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.*", SearchOption.AllDirectories)
                .Where(f => ProjectService.SourceExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            : [];
}

public sealed record ProjectTemplate(string Id, string File, string OutputName);

public static class ProjectService
{
    public static readonly HashSet<string> SourceExtensions = [".asm", ".inc", ".txt"];

    private sealed record ProjectFile(string Name, string MainFile);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Templates listed in config/templates/templates.json.</summary>
    public static IReadOnlyList<ProjectTemplate> Templates()
    {
        string path = Path.Combine(AppPaths.TemplateDirectory, "templates.json");
        try
        {
            return JsonSerializer.Deserialize<List<ProjectTemplate>>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Template source in the UI language, falling back to English.</summary>
    public static string TemplateText(ProjectTemplate template)
    {
        foreach (var lang in new[] { Loc.Instance.CurrentCode, "en" })
        {
            string path = Path.Combine(AppPaths.TemplateDirectory, lang, template.File);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        return "";
    }

    public static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    public static Project Create(string name, string parentDirectory, ProjectTemplate template)
    {
        string dir = Path.Combine(parentDirectory, name);
        System.IO.Directory.CreateDirectory(dir);
        string mainFile = template.OutputName;
        File.WriteAllText(Path.Combine(dir, mainFile), TemplateText(template));
        var project = new Project { FilePath = Path.Combine(dir, name + Project.Extension), Name = name, MainFile = mainFile };
        Save(project);
        return project;
    }

    public static void Save(Project project) =>
        File.WriteAllText(project.FilePath, JsonSerializer.Serialize(new ProjectFile(project.Name, project.MainFile), Options));

    public static Project? Open(string path)
    {
        try
        {
            var data = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path));
            if (data == null) return null;
            return new Project { FilePath = path, Name = data.Name, MainFile = data.MainFile };
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Returns a folder name that does not exist yet (HelloWorld, HelloWorld2, ...).</summary>
    public static string UniqueName(string parentDirectory, string baseName)
    {
        string name = baseName;
        for (int i = 2; System.IO.Directory.Exists(Path.Combine(parentDirectory, name)); i++) name = baseName + i;
        return name;
    }
}
