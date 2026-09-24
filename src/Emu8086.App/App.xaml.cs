using System.Windows;
using Emu8086.App.Services;
using Emu8086.App.Views;

namespace Emu8086.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            MessageBox.Show(args.Exception.Message, "emu8086ln", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        SettingsService.Load();
        var settings = SettingsService.Current;
        if (string.IsNullOrEmpty(settings.Language))
            settings.Language = AppSettings.DefaultLanguage;
        Loc.Instance.Load(settings.Language);
        settings.Language = Loc.Instance.CurrentCode;
        ThemeService.Apply(settings.Theme);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>Appends unexpected exceptions to error.log in the user data folder.</summary>
    private static void LogCrash(Exception? exception)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppPaths.UserDataDirectory);
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppPaths.UserDataDirectory, "error.log"),
                $"[{DateTime.Now:O}] {exception}{Environment.NewLine}");
        }
        catch (System.IO.IOException)
        {
            // Nothing more we can do.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsService.Save();
        base.OnExit(e);
    }
}
