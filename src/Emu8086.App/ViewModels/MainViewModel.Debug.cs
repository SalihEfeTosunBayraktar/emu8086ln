using System.IO;
using Emu8086.App.Services;
using Emu8086.Core.Analysis;
using Emu8086.Core.Assembler;
using Emu8086.Core.Cpu;
using Emu8086.Core.Machine;

namespace Emu8086.App.ViewModels;

public sealed partial class MainViewModel
{
    private List<AsmDiagnostic> _lastDiagnostics = new();
    private StepRecord? _lastAnalyzedRecord;

    /// <summary>Analysis of the most recently executed instruction (for the CPU visualizer).</summary>
    public StepAnalysis? LastStep { get; private set; }

    public event Action<StepAnalysis?>? StepCompleted;

    private void UpdateLastStep()
    {
        StepAnalysis? analysis = null;
        lock (Session.Sync)
        {
            var record = Session.Machine.History.Last;
            if (ReferenceEquals(record, _lastAnalyzedRecord)) return;
            _lastAnalyzedRecord = record;
            if (record != null) analysis = StepAnalyzer.Analyze(record, Session.Machine.Memory);
        }
        LastStep = analysis;
        StepCompleted?.Invoke(analysis);
    }

    #region Registers and flags

    private IReadOnlyList<RegisterItem> CreateRegisters()
    {
        var cpu = Session.Machine.Cpu;
        RegisterItem R(string name, bool split, Action<ushort> write) =>
            new(name, split, v => { lock (Session.Sync) write(v); });
        return
        [
            R("AX", true, v => cpu.AX = v), R("BX", true, v => cpu.BX = v),
            R("CX", true, v => cpu.CX = v), R("DX", true, v => cpu.DX = v),
            R("SI", false, v => cpu.SI = v), R("DI", false, v => cpu.DI = v),
            R("BP", false, v => cpu.BP = v), R("SP", false, v => cpu.SP = v),
            R("CS", false, v => cpu.CS = v), R("DS", false, v => cpu.DS = v),
            R("ES", false, v => cpu.ES = v), R("SS", false, v => cpu.SS = v),
            R("IP", false, v => cpu.IP = v),
        ];
    }

    private IReadOnlyList<FlagItem> CreateFlags()
    {
        var cpu = Session.Machine.Cpu;
        FlagItem F(string name, CpuFlags flag) =>
            new(name, "flag." + name, v => { lock (Session.Sync) cpu.SetFlag(flag, v); });
        return
        [
            F("CF", CpuFlags.CF), F("ZF", CpuFlags.ZF), F("SF", CpuFlags.SF), F("OF", CpuFlags.OF),
            F("PF", CpuFlags.PF), F("AF", CpuFlags.AF), F("IF", CpuFlags.IF), F("DF", CpuFlags.DF), F("TF", CpuFlags.TF),
        ];
    }

    private static ushort[] Values(CpuState s) =>
        [s.AX, s.BX, s.CX, s.DX, s.SI, s.DI, s.BP, s.SP, s.CS, s.DS, s.ES, s.SS, s.IP];

    #endregion

    #region Build

    /// <summary>A build is possible when a project main file exists or a document is open.</summary>
    private bool CanBuild() => Project != null && File.Exists(Project.MainFilePath) || SelectedDocument != null;

    /// <summary>The document that is assembled: the project's main file, else the active one.</summary>
    private DocumentViewModel? EnsureBuildTargetOpen()
    {
        if (Project != null && File.Exists(Project.MainFilePath)) return OpenDocumentQuiet(Project.MainFilePath);
        return SelectedDocument;
    }

    private DocumentViewModel? OpenDocumentQuiet(string path)
    {
        var selected = SelectedDocument;
        var doc = OpenDocument(path);
        if (selected != null) SelectedDocument = selected;
        return doc;
    }

    public bool Build()
    {
        var doc = EnsureBuildTargetOpen();
        if (doc == null) return false;
        if (!SaveAll()) return false;

        Session.Stop();
        if (_buildDocument != null) _buildDocument.ExecutionLine = null;
        _buildDocument = doc;
        _builtText = doc.Document.Text;

        var build = BuildService.Build(_builtText, doc.FilePath);
        _lastDiagnostics = build.Result.Diagnostics;
        RefreshDiagnosticsText();
        doc.ErrorLines = build.Result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Line).ToHashSet();

        if (!build.Result.Success)
        {
            int errors = build.Result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            Log(Loc.Instance.Format("output.buildFailed", Path.GetFileName(doc.FilePath), errors), OutputKind.Error);
            StatusText = Loc.Instance["status.buildFailed"];
            Session.Load(build);
            return false;
        }

        Session.Load(build);
        SyncBreakpoints(doc);
        _baseline = Session.Machine.Cpu.GetState();
        Log(Loc.Instance.Format("output.buildOk", Path.GetFileName(doc.FilePath), build.Result.Format.ToString().ToUpperInvariant(),
            build.Image!.Bytes.Length, build.Result.Passes), OutputKind.Success);
        StatusText = Loc.Instance["status.buildOk"];
        ProgramLoaded?.Invoke();
        if (build.Result.Devices.Count > 0) DevicesRequested?.Invoke(build.Result.Devices);
        RefreshAll();
        UpdateLastStep();
        return true;
    }

    private void RefreshDiagnosticsText()
    {
        Diagnostics.Clear();
        foreach (var d in _lastDiagnostics)
            Diagnostics.Add(new DiagnosticItem(d.Severity, d.File, d.Line, BuildService.Describe(d)));
    }

    /// <summary>Builds if the source changed since the last build. Returns false if not runnable.</summary>
    private bool EnsureBuilt()
    {
        var target = EnsureBuildTargetOpen();
        if (target == null) return false;
        bool stale = target != _buildDocument || _builtText != target.Document.Text || Documents.Any(d => d.IsDirty)
                     || Session.State == SessionState.Empty;
        if (stale) return Build();
        if (Session.State == SessionState.Stopped)
        {
            Session.Reset();
            _baseline = Session.Machine.Cpu.GetState();
        }
        return Session.CanRun;
    }

    private void Export()
    {
        if (!EnsureBuilt() || Session.Build?.Image is not { } image) return;
        string name = Path.GetFileNameWithoutExtension(Session.Build.SourcePath) + image.Extension;
        string? path = _dialogs.PickSaveFile(name, image.Extension);
        if (path == null) return;
        try
        {
            File.WriteAllBytes(path, image.ToFileBytes());
            Log(Loc.Instance.Format("output.exported", path), OutputKind.Success);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage(e.Message);
        }
    }

    #endregion

    #region Execution

    private void Run()
    {
        if (!EnsureBuilt()) return;
        _baseline = Session.Machine.Cpu.GetState();
        Session.Run();
    }

    private void Step(Action step)
    {
        if (!EnsureBuilt()) return;
        lock (Session.Sync) _baseline = Session.Machine.Cpu.GetState();
        step();
    }

    private void StepBack()
    {
        lock (Session.Sync)
        {
            var last = Session.Machine.History.Last;
            _baseline = last?.After ?? Session.Machine.Cpu.GetState();
        }
        Session.StepBack();
        RefreshAll();
        UpdateLastStep();
    }

    private void RunToCursor()
    {
        var doc = SelectedDocument;
        if (doc == null || !EnsureBuilt() || doc != _buildDocument) return;
        int? address = Session.AddressOfLine(doc.CaretLine);
        if (address == null)
        {
            StatusText = Loc.Instance["status.noCodeOnLine"];
            return;
        }
        _baseline = Session.Machine.Cpu.GetState();
        Session.RunTo(address.Value);
    }

    private void ResetProgram()
    {
        Session.Reset();
        _baseline = Session.Machine.Cpu.GetState();
        StatusText = Loc.Instance["status.reset"];
        ProgramLoaded?.Invoke();
        RefreshAll();
        UpdateLastStep();
    }

    private void ToggleBreakpointAtCaret()
    {
        var doc = SelectedDocument;
        doc?.ToggleBreakpoint(doc.CaretLine);
    }

    private void ClearBreakpoints()
    {
        foreach (var d in Documents) d.ClearBreakpoints();
    }

    private void SyncBreakpoints(DocumentViewModel doc)
    {
        if (doc != _buildDocument) return;
        lock (Session.Sync)
        {
            Session.Breakpoints.Clear();
            foreach (int line in doc.Breakpoints)
                if (Session.AddressOfLine(line) is int a) Session.Breakpoints.Add(a);
        }
        RefreshDisassembly();
    }

    public void GoToLine(int line)
    {
        var doc = _buildDocument ?? SelectedDocument;
        if (doc == null) return;
        SelectedDocument = doc;
        doc.Navigate(line);
    }

    private void OnSessionStateChanged(SessionState state)
    {
        OnPropertyChanged(nameof(State));
        UpdateStateText();
        switch (state)
        {
            case SessionState.WaitingInput:
                InputRequested?.Invoke();
                break;
            case SessionState.Stopped:
                ReportStop();
                break;
        }
        RefreshAll();
        if (!Session.IsBusy) UpdateLastStep();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void ReportStop()
    {
        var m = Session.Machine;
        string key = "stop." + m.StopReason;
        var kind = m.StopReason is StopReason.Terminated or StopReason.HaltInstruction ? OutputKind.Info : OutputKind.Error;
        Log(Loc.Instance.Format(key, m.ExitCode, m.Cpu.InstructionCount), kind);
        StatusText = Loc.Instance.Format(key, m.ExitCode, m.Cpu.InstructionCount);
    }

    private void UpdateStateText() => StateText = Loc.Instance["state." + Session.State];

    public void Log(string text, OutputKind kind) =>
        Output.Add(new OutputLine(DateTime.Now.ToString("HH:mm:ss"), text, kind));

    #endregion

    #region Refresh

    /// <summary>Called by the UI timer. Registers refresh every tick; lists refresh at a slower rate while running.</summary>
    public void Tick()
    {
        if (Session.State == SessionState.Empty) return;
        bool running = Session.IsBusy;
        RefreshRegisters();
        if (!running || Session.StepDelayMs > 0 || DateTime.Now - _lastSlowRefresh > SlowRefreshInterval)
        {
            _lastSlowRefresh = DateTime.Now;
            RefreshLists();
            UpdateExecutionLine(running && Session.StepDelayMs == 0);
            if (!running || Session.StepDelayMs > 0) UpdateLastStep();
        }
    }

    public void RefreshAll()
    {
        RefreshRegisters();
        RefreshLists();
        UpdateExecutionLine(false);
    }

    private void RefreshRegisters()
    {
        CpuState s;
        long count;
        lock (Session.Sync)
        {
            s = Session.Machine.Cpu.GetState();
            count = Session.Machine.Cpu.InstructionCount;
        }
        var now = Values(s);
        var before = Values(_baseline);
        for (int i = 0; i < Registers.Count; i++)
        {
            Registers[i].Value = now[i];
            Registers[i].Changed = now[i] != before[i];
        }
        var flagBits = new[] { CpuFlags.CF, CpuFlags.ZF, CpuFlags.SF, CpuFlags.OF, CpuFlags.PF, CpuFlags.AF, CpuFlags.IF, CpuFlags.DF, CpuFlags.TF };
        for (int i = 0; i < Flags.Count; i++)
            Flags[i].Load((s.Flags & (ushort)flagBits[i]) != 0);
        PositionText = Loc.Instance.Format("status.position", s.CS, s.IP, Memory.Physical(s.CS, s.IP), count);
    }

    private void RefreshLists()
    {
        RefreshStack();
        RefreshDisassembly();
        RefreshVariables();
    }

    private void UpdateExecutionLine(bool hide)
    {
        if (_buildDocument == null) return;
        bool show = !hide && Session.State is not (SessionState.Empty or SessionState.Stopped);
        _buildDocument.ExecutionLine = show ? Session.CurrentLine : null;
    }

    private void RefreshStack()
    {
        var rows = new List<StackRow>();
        lock (Session.Sync)
        {
            var cpu = Session.Machine.Cpu;
            for (int i = 0; i < StackRows; i++)
            {
                ushort off = (ushort)(cpu.SP + i * 2);
                if (off < cpu.SP) break; // wrapped past the top of the segment
                ushort value = Session.Machine.Memory.Read16(cpu.SS, off);
                rows.Add(new StackRow($"{cpu.SS:X4}:{off:X4}", $"{value:X4}", i == 0));
            }
        }
        Replace(Stack, rows);
    }

    private void RefreshDisassembly()
    {
        var rows = new List<DisassemblyRow>();
        lock (Session.Sync)
        {
            var cpu = Session.Machine.Cpu;
            foreach (var ins in Session.Disassembler.DecodeMany(cpu.CS, cpu.IP, DisassemblyLines))
            {
                int address = Memory.Physical(ins.Segment, ins.Offset);
                rows.Add(new DisassemblyRow(address, $"{ins.Segment:X4}:{ins.Offset:X4}",
                    string.Join(" ", ins.Bytes.Select(b => b.ToString("X2"))), ins.Text,
                    ins.Segment == cpu.CS && ins.Offset == cpu.IP, Session.Breakpoints.Contains(address)));
            }
        }
        Replace(Disassembly, rows);
    }

    private void RefreshVariables()
    {
        var build = Session.Build;
        if (build == null)
        {
            Variables.Clear();
            return;
        }
        var rows = new List<VariableRow>();
        lock (Session.Sync)
        {
            var machine = Session.Machine;
            foreach (var sym in build.Result.Symbols.Values
                         .Where(s => s.Kind == SymbolKind.Variable && s.Segment >= 0 && !s.Name.StartsWith("??"))
                         .OrderBy(s => s.Segment).ThenBy(s => s.Value))
            {
                int address = machine.PhysicalAddress(sym.Segment, (int)sym.Value);
                if (address < 0) continue;
                ushort seg = machine.SegmentBases[sym.Segment];
                string value = FormatVariable(machine.Memory, address, sym.ElementSize, sym.Length);
                string type = sym.ElementSize switch { 1 => "BYTE", 2 => "WORD", 4 => "DWORD", _ => $"{sym.ElementSize}B" };
                if (sym.Length > 1) type += $"[{sym.Length}]";
                rows.Add(new VariableRow(sym.Name, $"{seg:X4}:{sym.Value:X4}", type, value));
            }
        }
        Replace(Variables, rows);
    }

    private static string FormatVariable(Memory memory, int address, int size, int length)
    {
        const int MaxElements = 16;
        int count = Math.Min(length, MaxElements);
        var parts = new List<string>();
        for (int i = 0; i < count; i++)
        {
            int a = address + i * size;
            parts.Add(size switch
            {
                1 => memory.Read8(a).ToString("X2"),
                2 => memory.Read16(a).ToString("X4"),
                _ => (memory.Read16(a) | (uint)memory.Read16(a + 2) << 16).ToString("X8"),
            });
        }
        string text = string.Join(" ", parts) + (length > MaxElements ? " ..." : "");
        if (size == 1 && length > 1)
        {
            var chars = Enumerable.Range(0, count).Select(i => memory.Read8(address + i))
                .Select(b => b is >= 32 and < 127 ? (char)b : '.');
            text += $"   \"{new string(chars.ToArray())}\"";
        }
        else if (length == 1)
        {
            int v = size == 1 ? memory.Read8(address) : memory.Read16(address);
            text += $"  ({v})";
        }
        return text;
    }

    /// <summary>Updates a collection in place so list views keep their scroll position.</summary>
    private static void Replace<T>(System.Collections.ObjectModel.ObservableCollection<T> target, List<T> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (i < target.Count)
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], items[i])) target[i] = items[i];
            }
            else target.Add(items[i]);
        }
        while (target.Count > items.Count) target.RemoveAt(target.Count - 1);
    }

    #endregion
}
