using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Emu8086.App.Infrastructure;
using Emu8086.App.Services;
using Emu8086.Core.Assembler;
using Emu8086.Core.Cpu;
using Emu8086.Core.Machine;

namespace Emu8086.App.ViewModels;

public sealed record ProjectFileItem(string Name, string Path, bool IsMain);

public sealed record ExampleItem(string Name, string Path);

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int DisassemblyLines = 40;
    private const int StackRows = 24;
    private static readonly TimeSpan SlowRefreshInterval = TimeSpan.FromMilliseconds(250);

    private readonly IDialogService _dialogs;
    private DocumentViewModel? _selectedDocument;
    private DocumentViewModel? _buildDocument;
    private string? _builtText;
    private Project? _project;
    private string _statusText = "";
    private string _stateText = "";
    private string _positionText = "";
    private int _stepDelay;
    private CpuState _baseline;
    private DateTime _lastSlowRefresh = DateTime.MinValue;
    private LanguageInfo? _selectedLanguage;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Session = new EmulatorSession();
        Session.StateChanged += s => Application.Current?.Dispatcher.BeginInvoke(() => OnSessionStateChanged(s));
        _stepDelay = SettingsService.Current.StepDelayMs;
        Session.StepDelayMs = _stepDelay;

        Registers = CreateRegisters();
        Flags = CreateFlags();
        Languages = Loc.Instance.Available();
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == Loc.Instance.CurrentCode);
        Loc.Instance.LanguageChanged += UpdateStateText;

        NewProjectCommand = new RelayCommand(NewProject);
        OpenProjectCommand = new RelayCommand(OpenProject);
        OpenFileCommand = new RelayCommand(OpenFile);
        NewFileCommand = new RelayCommand(NewFile, () => Project != null);
        SaveCommand = new RelayCommand(() => SaveDocument(SelectedDocument), () => SelectedDocument != null);
        SaveAllCommand = new RelayCommand(() => SaveAll());
        CloseDocumentCommand = new RelayCommand(p => CloseDocument(p as DocumentViewModel ?? SelectedDocument));
        OpenProjectFileCommand = new RelayCommand(p => { if (p is ProjectFileItem f) OpenDocument(f.Path); });
        SetMainFileCommand = new RelayCommand(p => SetMainFile(p as ProjectFileItem), p => Project != null && p is ProjectFileItem);
        OpenExampleCommand = new RelayCommand(p => { if (p is string path) OpenExample(path); });
        OpenRecentCommand = new RelayCommand(p => { if (p is string path) OpenProjectPath(path); });
        BuildCommand = new RelayCommand(() => Build(), () => !Session.IsBusy && CanBuild());
        RunCommand = new RelayCommand(Run, () => !Session.IsBusy && CanBuild());
        PauseCommand = new RelayCommand(Session.Pause, () => Session.IsBusy);
        StopCommand = new RelayCommand(ResetProgram, () => Session.State != SessionState.Empty);
        StepIntoCommand = new RelayCommand(() => Step(Session.StepInto), () => !Session.IsBusy && CanBuild());
        StepOverCommand = new RelayCommand(() => Step(Session.StepOver), () => !Session.IsBusy && CanBuild());
        StepBackCommand = new RelayCommand(StepBack, () => !Session.IsBusy && Session.Machine.History.Count > 0);
        RunToCursorCommand = new RelayCommand(RunToCursor, () => !Session.IsBusy && SelectedDocument != null);
        ToggleBreakpointCommand = new RelayCommand(ToggleBreakpointAtCaret, () => SelectedDocument != null);
        ClearBreakpointsCommand = new RelayCommand(ClearBreakpoints);
        ExportCommand = new RelayCommand(Export, () => CanBuild());
        ToggleThemeCommand = new RelayCommand(ThemeService.Toggle);
        ThemeService.ThemeChanged += () => OnPropertyChanged(nameof(IsDarkTheme));
        AboutCommand = new RelayCommand(_dialogs.ShowAbout);
        GoToDiagnosticCommand = new RelayCommand(p => { if (p is DiagnosticItem d) GoToLine(d.Line); });
        ClearOutputCommand = new RelayCommand(Output.Clear);

        UpdateStateText();
    }

    public EmulatorSession Session { get; }
    public ObservableCollection<DocumentViewModel> Documents { get; } = new();
    public ObservableCollection<ProjectFileItem> ProjectFiles { get; } = new();
    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = new();
    public ObservableCollection<OutputLine> Output { get; } = new();
    public ObservableCollection<StackRow> Stack { get; } = new();
    public ObservableCollection<DisassemblyRow> Disassembly { get; } = new();
    public ObservableCollection<VariableRow> Variables { get; } = new();
    public IReadOnlyList<RegisterItem> Registers { get; }
    public IReadOnlyList<FlagItem> Flags { get; }
    public IReadOnlyList<LanguageInfo> Languages { get; }
    public IReadOnlyList<string> RecentProjects => SettingsService.Current.RecentProjects.Where(File.Exists).ToList();

    public IReadOnlyList<ExampleItem> Examples =>
        Directory.Exists(AppPaths.ExamplesDirectory)
            ? Directory.GetFiles(AppPaths.ExamplesDirectory, "*.asm").Order()
                .Select(p => new ExampleItem(Path.GetFileNameWithoutExtension(p).Replace('_', ' '), p)).ToList()
            : [];

    public ICommand NewProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand NewFileCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAllCommand { get; }
    public ICommand CloseDocumentCommand { get; }
    public ICommand OpenProjectFileCommand { get; }
    public ICommand SetMainFileCommand { get; }
    public ICommand OpenExampleCommand { get; }
    public ICommand OpenRecentCommand { get; }
    public ICommand BuildCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand StepIntoCommand { get; }
    public ICommand StepOverCommand { get; }
    public ICommand StepBackCommand { get; }
    public ICommand RunToCursorCommand { get; }
    public ICommand ToggleBreakpointCommand { get; }
    public ICommand ClearBreakpointsCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand GoToDiagnosticCommand { get; }
    public ICommand ClearOutputCommand { get; }

    /// <summary>Raised when the program waits for keyboard input, so the screen can take focus.</summary>
    public event Action? InputRequested;
    /// <summary>Raised after a build with the devices requested via #start=...#.</summary>
    public event Action<IReadOnlyList<string>>? DevicesRequested;
    /// <summary>Raised when a new build has been loaded (views reset their caches).</summary>
    public event Action? ProgramLoaded;

    public DocumentViewModel? SelectedDocument
    {
        get => _selectedDocument;
        set => Set(ref _selectedDocument, value);
    }

    public Project? Project
    {
        get => _project;
        private set
        {
            if (!Set(ref _project, value)) return;
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(ProjectName));
        }
    }

    public string ProjectName => Project?.Name ?? Loc.Instance["project.none"];
    public string WindowTitle => Project == null ? "emu8086ln" : $"{Project.Name} - emu8086ln";

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string StateText
    {
        get => _stateText;
        private set => Set(ref _stateText, value);
    }

    public string PositionText
    {
        get => _positionText;
        private set => Set(ref _positionText, value);
    }

    public SessionState State => Session.State;

    /// <summary>Instruction delay in ms (0 = full speed), bound to the speed slider.</summary>
    public int StepDelay
    {
        get => _stepDelay;
        set
        {
            if (!Set(ref _stepDelay, value)) return;
            Session.StepDelayMs = value;
            SettingsService.Current.StepDelayMs = value;
            OnPropertyChanged(nameof(SpeedText));
        }
    }

    public string SpeedText => StepDelay == 0 ? Loc.Instance["speed.max"] : Loc.Instance.Format("speed.delay", StepDelay);

    public bool IsDarkTheme
    {
        get => ThemeService.Current == ThemeService.Dark;
        set
        {
            if (value == IsDarkTheme) return;
            ThemeService.Apply(value ? ThemeService.Dark : ThemeService.Light);
            SettingsService.Current.Theme = ThemeService.Current;
        }
    }

    public LanguageInfo? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!Set(ref _selectedLanguage, value) || value == null) return;
            Loc.Instance.Load(value.Code);
            SettingsService.Current.Language = value.Code;
            RefreshDiagnosticsText();
            OnPropertyChanged(nameof(SpeedText));
            OnPropertyChanged(nameof(ProjectName));
        }
    }

    public void Dispose() => Session.Dispose();
}
