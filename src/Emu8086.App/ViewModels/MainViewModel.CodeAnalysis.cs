using System.IO;
using System.Windows.Threading;
using Emu8086.App.Services;
using Emu8086.Core.Analysis;

namespace Emu8086.App.ViewModels;

/// <summary>Runs <see cref="CodeLinter"/> on the active document shortly after the user stops typing.</summary>
public sealed partial class MainViewModel
{
    private static readonly TimeSpan AnalysisDelay = TimeSpan.FromMilliseconds(600);
    private readonly Dictionary<DocumentViewModel, int> _analyzedVersions = new();
    private DispatcherTimer? _analysisTimer;
    private DocumentViewModel? _analysisShownFor;

    private void InitializeCodeAnalysis()
    {
        _analysisTimer = new DispatcherTimer { Interval = AnalysisDelay };
        _analysisTimer.Tick += (_, _) => AnalyzeSelectedDocument();
        _analysisTimer.Start();
        SettingsService.Changed += () =>
        {
            _analyzedVersions.Clear();
            AnalyzeSelectedDocument();
        };
    }

    private void AnalyzeSelectedDocument()
    {
        var doc = SelectedDocument;
        // Wait until typing pauses so warnings do not flicker on half-written lines.
        bool typing = doc?.Stats.LastChange is { } last && DateTime.Now - last < AnalysisDelay;
        if (!typing) AnalyzeIfChanged(doc);
        if (doc == _analysisShownFor) return;
        _analysisShownFor = doc;
        RefreshDiagnosticsText();
    }

    private void AnalyzeIfChanged(DocumentViewModel? doc)
    {
        if (doc == null || !Path.GetExtension(doc.FilePath).Equals(".asm", StringComparison.OrdinalIgnoreCase)) return;
        if (_analyzedVersions.TryGetValue(doc, out int version) && version == doc.EditVersion) return;
        foreach (var closed in _analyzedVersions.Keys.Except(Documents).ToList()) _analyzedVersions.Remove(closed);
        _analyzedVersions[doc] = doc.EditVersion;
        doc.Warnings = SettingsService.Current.CodeAnalysis
            ? CodeLinter.Analyze(doc.Document.Text, Path.GetFileName(doc.FilePath))
            : [];
        if (doc == SelectedDocument) RefreshDiagnosticsText();
    }
}
