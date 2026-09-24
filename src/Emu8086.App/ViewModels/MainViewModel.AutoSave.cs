using System.IO;
using System.Windows.Threading;
using Emu8086.App.Services;
using Emu8086.Core.Editing;

namespace Emu8086.App.ViewModels;

/// <summary>Automatic saving of open documents according to <see cref="AutoSavePolicy"/>.</summary>
public sealed partial class MainViewModel
{
    private static readonly TimeSpan AutoSaveCheckInterval = TimeSpan.FromSeconds(1);
    private DispatcherTimer? _autoSaveTimer;
    private DocumentViewModel? _previousDocument;

    private void InitializeAutoSave()
    {
        _autoSaveTimer = new DispatcherTimer { Interval = AutoSaveCheckInterval };
        _autoSaveTimer.Tick += (_, _) => AutoSave(AutoSaveTrigger.Tick);
        _autoSaveTimer.Start();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SelectedDocument)) return;
            // Leaving a document counts as losing focus for the smart policy.
            if (_previousDocument != null) AutoSave(AutoSaveTrigger.FocusLost, _previousDocument);
            _previousDocument = SelectedDocument;
        };
    }

    private static AutoSavePolicy CurrentPolicy => new()
    {
        Mode = SettingsService.Current.AutoSaveMode,
        Interval = TimeSpan.FromSeconds(SettingsService.Current.AutoSaveIntervalSeconds),
    };

    /// <summary>Called when the main window is deactivated.</summary>
    public void OnApplicationDeactivated() => AutoSave(AutoSaveTrigger.FocusLost);

    private void AutoSave(AutoSaveTrigger trigger, DocumentViewModel? only = null)
    {
        var policy = CurrentPolicy;
        if (policy.Mode == AutoSaveMode.Off) return;
        var now = DateTime.Now;
        int saved = 0;
        foreach (var doc in Documents.ToList())
        {
            if (only != null && doc != only) continue;
            if (!doc.IsDirty || !policy.ShouldSave(doc.Stats, now, trigger)) continue;
            try
            {
                doc.Save();
                saved++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                StatusText = Loc.Instance.Format("autosave.failed", Path.GetFileName(doc.FilePath), e.Message);
            }
        }
        if (saved > 0) StatusText = Loc.Instance.Format("autosave.done", now.ToString("HH:mm:ss"));
    }
}
