using Emu8086.Core.Analysis;

namespace Emu8086.App.Services;

public enum StepPhase { Fetch, Decode, Execute, WriteBack }

public sealed record NarrationLine(StepPhase Phase, string Text);

/// <summary>Describes a <see cref="StepAnalysis"/> in plain, localized sentences for learners.</summary>
public static class StepNarrator
{
    public static List<NarrationLine> Describe(StepAnalysis a)
    {
        var loc = Loc.Instance;
        var lines = new List<NarrationLine>();
        var ins = a.Instruction;
        string bytes = string.Join(" ", ins.Bytes.Select(b => b.ToString("X2")));

        lines.Add(new(StepPhase.Fetch, loc.Format("viz.n.fetch", a.Before.CS, a.Before.IP, a.FetchAddress, ins.Bytes.Length, bytes)));
        lines.Add(new(StepPhase.Decode, loc.Format("viz.n.decode", ins.Text)));

        if (a.Address is { } addr)
        {
            var parts = new List<string>();
            if (addr.Base != null) parts.Add($"{addr.Base}({addr.BaseValue:X4}h)");
            if (addr.Index != null) parts.Add($"{addr.Index}({addr.IndexValue:X4}h)");
            if (addr.Displacement != 0 || parts.Count == 0) parts.Add($"{addr.Displacement & 0xFFFF:X4}h");
            lines.Add(new(StepPhase.Execute, loc.Format("viz.n.ea", string.Join(" + ", parts), addr.EffectiveAddress,
                $"{addr.Segment}({addr.SegmentValue:X4}h)", addr.PhysicalAddress)));
        }
        foreach (var r in a.MemoryReads.Take(8))
            lines.Add(new(StepPhase.Execute, loc.Format("viz.n.memRead", r.Address, r.Value)));
        if (a.Alu is { } alu)
        {
            string format = alu.Word ? "X4" : "X2";
            string A(int? v) => v is int x ? x.ToString(format) + "h" : "?";
            lines.Add(new(StepPhase.Execute, loc.Format("viz.n.alu", alu.Operation, A(alu.OperandA), A(alu.OperandB), A(alu.Result))));
        }
        if (a.Interrupt is int n) lines.Add(new(StepPhase.Execute, loc.Format("viz.n.int", n)));

        foreach (var r in a.RegistersWritten)
            lines.Add(new(StepPhase.WriteBack, loc.Format("viz.n.reg", r.Name, r.Before, r.After)));
        foreach (var w in a.MemoryWrites.Take(8))
            lines.Add(new(StepPhase.WriteBack, loc.Format("viz.n.memWrite", w.Address, w.OldValue, w.Value)));
        foreach (var p in a.Ports)
            lines.Add(new(StepPhase.WriteBack, loc.Format("viz.n.port", p.IsWrite ? "OUT" : "IN", p.Port, p.Value.ToString(p.Word ? "X4" : "X2"))));
        foreach (var f in a.FlagsChanged)
            lines.Add(new(StepPhase.WriteBack, loc.Format("viz.n.flag", f.Name, f.Before ? 1 : 0, f.After ? 1 : 0)));

        lines.Add(new(StepPhase.WriteBack, a.Jumped
            ? loc.Format("viz.n.jump", a.After.CS, a.After.IP)
            : loc.Format("viz.n.next", a.After.IP)));
        return lines;
    }
}
