using System.IO;

namespace Emu8086.App.Services;

/// <summary>Well-known folders. Everything user-specific lives under Documents\emu8086ln and %AppData%.</summary>
public static class AppPaths
{
    public const string AppFolderName = "emu8086ln";

    public static string InstallDirectory => AppContext.BaseDirectory;
    public static string ConfigDirectory => Path.Combine(InstallDirectory, "config");
    public static string LanguageDirectory => Path.Combine(ConfigDirectory, "lang");
    public static string TemplateDirectory => Path.Combine(ConfigDirectory, "templates");
    public static string LibraryDirectory => Path.Combine(InstallDirectory, "Library");
    public static string ExamplesDirectory => Path.Combine(InstallDirectory, "Examples");

    public static string UserDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string SettingsFile => Path.Combine(UserDataDirectory, "settings.json");

    public static string DocumentsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppFolderName);

    public static string DefaultProjectsDirectory => Path.Combine(DocumentsDirectory, "Projects");
    public static string VirtualDriveDirectory => Path.Combine(DocumentsDirectory, "vdrive", "C");
}
