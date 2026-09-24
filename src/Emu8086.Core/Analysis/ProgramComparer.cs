using Emu8086.Core.Cpu;
using Emu8086.Core.Disassembler;
using Emu8086.Core.Machine;

namespace Emu8086.Core.Analysis;

/// <summary>One lock-step: the instruction each program executed and the registers that differ afterwards.</summary>
public sealed record CompareStep(int Index, DisassembledInstruction? A, DisassembledInstruction? B, IReadOnlyList<string> Differences);

/// <summary>How one program's run ended.</summary>
public sealed record RunSummary(long Instructions, long Cycles, string Screen, StopReason Stop, int ExitCode, bool WaitingForInput);

public sealed record CompareResult(IReadOnlyList<CompareStep> Steps, int? FirstDifference, RunSummary A, RunSummary B)
{
    public bool SameScreen => A.Screen == B.Screen;
}

/// <summary>
/// Runs two programs side by side, one instruction each per step, and reports where their
/// general registers first differ plus how each run ended (instructions, cycles, screen output).
/// </summary>
public static class ProgramComparer
{
    public const int DefaultMaxSteps = 20000;

    private static readonly (string Name, Func<CpuState, ushort> Get)[] Compared =
    [
        ("AX", s => s.AX), ("BX", s => s.BX), ("CX", s => s.CX), ("DX", s => s.DX),
        ("SI", s => s.SI), ("DI", s => s.DI), ("BP", s => s.BP), ("SP", s => s.SP),
    ];

    public static CompareResult Compare(ProgramImage a, ProgramImage b, string diskRoot, int maxSteps = DefaultMaxSteps)
    {
        using var ma = Start(a, diskRoot);
        using var mb = Start(b, diskRoot);
        var da = new Disassembler8086(ma.Memory);
        var db = new Disassembler8086(mb.Memory);
        var steps = new List<CompareStep>();
        int? first = null;
        bool waitA = false, waitB = false;

        for (int i = 1; i <= maxSteps; i++)
        {
            var insA = Advance(ma, da, ref waitA);
            var insB = Advance(mb, db, ref waitB);
            if (insA == null && insB == null) break;
            var sa = ma.Cpu.GetState();
            var sb = mb.Cpu.GetState();
            var diff = Compared.Where(r => r.Get(sa) != r.Get(sb)).Select(r => r.Name).ToList();
            if (diff.Count > 0) first ??= i;
            steps.Add(new CompareStep(i, insA, insB, diff));
        }
        return new CompareResult(steps, first, Summary(ma, waitA), Summary(mb, waitB));
    }

    private static Machine.Machine Start(ProgramImage image, string diskRoot)
    {
        var machine = new Machine.Machine(diskRoot) { HistoryEnabled = false, CountCycles = true };
        machine.Load(image);
        return machine;
    }

    /// <summary>Executes one instruction; null once the program stopped or waits for input.</summary>
    private static DisassembledInstruction? Advance(Machine.Machine machine, Disassembler8086 decoder, ref bool waiting)
    {
        if (waiting || machine.IsStopped) return null;
        var cpu = machine.Cpu;
        var instruction = decoder.Decode(cpu.CS, cpu.IP);
        if (machine.Step() == StepResult.Waiting)
        {
            waiting = true;
            return null;
        }
        return instruction;
    }

    private static RunSummary Summary(Machine.Machine m, bool waiting) =>
        new(m.Cpu.InstructionCount, m.Cycles, m.Video.ReadText(), m.StopReason, m.ExitCode, waiting);
}
