using System.IO;
using System.Text;
using Emu8086.App.Infrastructure;
using ICSharpCode.AvalonEdit.Document;

namespace Emu8086.App.ViewModels;

/// <summary>An open source file in the editor.</summary>
public sealed class DocumentViewModel : ObservableObject
{
    private bool _isDirty;
    private int? _executionLine;
    private IReadOnlyCollection<int> _errorLines = [];
    private IReadOnlyList<Emu8086.Core.Assembler.AsmDiagnostic> _warnings = [];
    private string _filePath;

    public DocumentViewModel(string filePath, string text)
    {
        _filePath = filePath;
        Document = new TextDocument(text);
        Document.UndoStack.ClearAll();
        Document.TextChanged += (_, _) =>
        {
            IsDirty = true;
            EditVersion++;
        };
        Document.Changed += (_, e) => Stats.Record(e.InsertionLength, e.RemovalLength,
            CountLineBreaks(e.InsertedText.Text) + CountLineBreaks(e.RemovedText.Text), DateTime.Now);
    }

    public TextDocument Document { get; }

    /// <summary>Unsaved changes, used by the auto-save policy.</summary>
    public Emu8086.Core.Editing.EditStats Stats { get; } = new();

    private static int CountLineBreaks(string text) => text.Count(c => c == '\n');
    public HashSet<int> Breakpoints { get; } = new();

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (Set(ref _filePath, value)) OnPropertyChanged(nameof(Title));
        }
    }

    public string Title => Path.GetFileName(FilePath) + (IsDirty ? " •" : "");

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (Set(ref _isDirty, value)) OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>Line (1-based) of the instruction about to execute, when this file is running.</summary>
    public int? ExecutionLine
    {
        get => _executionLine;
        set => Set(ref _executionLine, value);
    }

    public IReadOnlyCollection<int> ErrorLines
    {
        get => _errorLines;
        set => Set(ref _errorLines, value);
    }

    /// <summary>Incremented on every text change; lets the code analysis skip unchanged documents.</summary>
    public int EditVersion { get; private set; }

    /// <summary>Code analysis warnings for this file.</summary>
    public IReadOnlyList<Emu8086.Core.Assembler.AsmDiagnostic> Warnings
    {
        get => _warnings;
        set
        {
            if (Set(ref _warnings, value)) OnPropertyChanged(nameof(WarningLines));
        }
    }

    public IReadOnlyCollection<int> WarningLines => Warnings.Select(w => w.Line).ToHashSet();

    /// <summary>Supplied by the main view model: explanation for a line of the running program, or null.</summary>
    public Func<int, string?>? Explain { get; set; }

    /// <summary>Caret line reported by the editor view.</summary>
    public int CaretLine { get; set; } = 1;

    public event Action? BreakpointsChanged;
    public event Action<int>? NavigateRequested;

    public void ToggleBreakpoint(int line)
    {
        if (!Breakpoints.Remove(line)) Breakpoints.Add(line);
        BreakpointsChanged?.Invoke();
    }

    public void ClearBreakpoints()
    {
        Breakpoints.Clear();
        BreakpointsChanged?.Invoke();
    }

    public void Navigate(int line) => NavigateRequested?.Invoke(line);

    public void Save()
    {
        File.WriteAllText(FilePath, Document.Text, new UTF8Encoding(false));
        IsDirty = false;
        Stats.Reset();
    }
}
