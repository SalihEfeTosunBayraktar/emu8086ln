using Emu8086.App.Services;
using Emu8086.Core.Analysis;
using Emu8086.Core.Cpu;
using Emu8086.Core.Disassembler;

namespace Emu8086.App.ViewModels;

/// <summary>Optional tools (Settings > Tools): cycle counter, line explanations and the source used for comparisons.</summary>
public sealed partial class MainViewModel
{
    private string _cycleText = "";

    /// <summary>Estimated clock cycles for the status bar; empty when the cycle counter is off.</summary>
    public string CycleText
    {
        get => _cycleText;
        private set => Set(ref _cycleText, value);
    }

    /// <summary>SP right after loading; everything between SP and this is what the program pushed.</summary>
    public ushort StackTop { get; private set; }

    private void InitializeTools()
    {
        ApplyToolSettings();
        SettingsService.Changed += ApplyToolSettings;
    }

    private void ApplyToolSettings()
    {
        lock (Session.Sync) Session.Machine.CountCycles = SettingsService.Current.CycleCounter;
        UpdateCycleText();
    }

    private void UpdateCycleText()
    {
        if (!SettingsService.Current.CycleCounter || Session.State == SessionState.Empty)
        {
            CycleText = "";
            return;
        }
        long cycles;
        lock (Session.Sync) cycles = Session.Machine.Cycles;
        CycleText = Loc.Instance.Format("cycles.status", cycles, CycleTable.Microseconds(cycles), CycleTable.ClockMHz);
    }

    private void RememberStackTop()
    {
        lock (Session.Sync) StackTop = Session.Machine.Cpu.SP;
    }

    /// <summary>What the instruction on <paramref name="line"/> would do with the current registers, or null.</summary>
    public string? ExplainLine(DocumentViewModel doc, int line)
    {
        if (!SettingsService.Current.LineExplain || doc != _buildDocument || doc.Document.Text != _builtText
            || Session.State == SessionState.Empty || Session.AddressOfLine(line) is not { } address)
            return null;

        DisassembledInstruction instruction;
        CpuState state;
        lock (Session.Sync)
        {
            state = Session.Machine.Cpu.GetState();
            int offset = address - state.CS * 16;
            (ushort segment, ushort off) = offset is >= 0 and <= 0xFFFF
                ? (state.CS, (ushort)offset)
                : ((ushort)(address >> 4), (ushort)(address & 0xF));
            instruction = new Disassembler8086(Session.Machine.Memory).Decode(segment, off);
        }
        var explanation = InstructionExplainer.Explain(instruction, state);
        return instruction.Text + "\n" + Loc.Instance.Format("explain." + explanation.Key, explanation.Args.Cast<object?>().ToArray());
    }

    /// <summary>The file a comparison uses as program A: the same one Run would assemble.</summary>
    public (string Path, string Text)? ComparisonSource()
    {
        var doc = EnsureBuildTargetOpen();
        return doc == null ? null : (doc.FilePath, doc.Document.Text);
    }
}
