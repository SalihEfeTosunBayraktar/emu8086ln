using System.Globalization;
using System.Text.RegularExpressions;
using Emu8086.Core.Cpu;
using Emu8086.Core.Disassembler;
using Emu8086.Core.Machine;

namespace Emu8086.Core.Analysis;

public sealed record RegisterChange(string Name, ushort Before, ushort After);

public sealed record MemoryAccess(int Address, byte Value, bool IsWrite, byte OldValue = 0);

public sealed record PortAccess(int Port, int Value, bool IsWrite, bool Word);

/// <summary>How a memory operand's address was formed: segment * 16 + (base + index + displacement).</summary>
public sealed record AddressCalculation(
    string Segment, ushort SegmentValue,
    string? Base, ushort BaseValue,
    string? Index, ushort IndexValue,
    int Displacement, ushort EffectiveAddress, int PhysicalAddress);

/// <summary>An arithmetic/logic operation performed by the ALU.</summary>
public sealed record AluOperation(string Operation, int? OperandA, int? OperandB, int Result, bool Word);

public sealed record FlagChange(string Name, bool Before, bool After);

/// <summary>Everything one executed instruction did, in terms of the 8086 building blocks.</summary>
public sealed class StepAnalysis
{
    public required DisassembledInstruction Instruction { get; init; }
    public required CpuState Before { get; init; }
    public required CpuState After { get; init; }
    public int FetchAddress { get; init; }
    public List<string> RegistersRead { get; } = new();
    public List<RegisterChange> RegistersWritten { get; } = new();
    public List<FlagChange> FlagsChanged { get; } = new();
    public List<MemoryAccess> MemoryReads { get; } = new();
    public List<MemoryAccess> MemoryWrites { get; } = new();
    public List<PortAccess> Ports { get; } = new();
    public AddressCalculation? Address { get; set; }
    public AluOperation? Alu { get; set; }
    /// <summary>True when execution did not continue with the next sequential instruction.</summary>
    public bool Jumped { get; set; }
    public int? ImmediateValue { get; set; }
    /// <summary>Software interrupt number handled by the BIOS/DOS, if any.</summary>
    public int? Interrupt { get; set; }

    public string Mnemonic => Instruction.Mnemonic.Split(' ')[^1];
}

/// <summary>
/// Turns a <see cref="StepRecord"/> into a <see cref="StepAnalysis"/>: which registers were
/// read and written, the address calculation, the ALU operation, memory and port traffic.
/// </summary>
public static partial class StepAnalyzer
{
    private static readonly string[] Reg16 = ["AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI"];
    private static readonly string[] Reg8 = ["AL", "CL", "DL", "BL", "AH", "CH", "DH", "BH"];
    private static readonly string[] SegRegs = ["ES", "CS", "SS", "DS"];

    private static readonly HashSet<string> AluMnemonics =
    [
        "ADD", "ADC", "SUB", "SBB", "CMP", "AND", "OR", "XOR", "TEST", "INC", "DEC", "NEG", "NOT",
        "MUL", "IMUL", "DIV", "IDIV", "SHL", "SAL", "SHR", "SAR", "ROL", "ROR", "RCL", "RCR",
        "DAA", "DAS", "AAA", "AAS", "AAM", "AAD", "CBW", "CWD",
    ];

    /// <summary>Instructions whose first operand is only written, not read.</summary>
    private static readonly HashSet<string> WriteOnlyDestination = ["MOV", "LEA", "LDS", "LES", "POP", "IN"];

    private static readonly (string Name, CpuFlags Flag)[] FlagBits =
    [
        ("CF", CpuFlags.CF), ("PF", CpuFlags.PF), ("AF", CpuFlags.AF), ("ZF", CpuFlags.ZF),
        ("SF", CpuFlags.SF), ("TF", CpuFlags.TF), ("IF", CpuFlags.IF), ("DF", CpuFlags.DF), ("OF", CpuFlags.OF),
    ];

    [GeneratedRegex(@"\[(?<inner>[^\]]*)\]")]
    private static partial Regex MemoryOperand();

    [GeneratedRegex(@"^(?<seg>ES|CS|SS|DS):")]
    private static partial Regex SegmentPrefix();

    public static StepAnalysis Analyze(StepRecord record, Memory memory)
    {
        var before = record.Before;
        var after = record.After;
        var instruction = DecodeAt(memory, record, before.CS, before.IP);
        var analysis = new StepAnalysis
        {
            Instruction = instruction,
            Before = before,
            After = after,
            FetchAddress = Memory.Physical(before.CS, before.IP),
        };

        string mnemonic = analysis.Mnemonic;
        var operands = SplitOperands(instruction.Operands);

        CollectRegisterChanges(analysis);
        CollectFlagChanges(analysis);
        CollectMemory(analysis, record, instruction.Bytes.Length);
        foreach (var (port, value, isWrite, word) in record.PortAccesses)
            analysis.Ports.Add(new PortAccess(port, value, isWrite, word));

        CollectRegisterReads(analysis, mnemonic, operands);
        analysis.Address = operands.Select(o => AddressOf(o, before)).FirstOrDefault(a => a != null);
        analysis.ImmediateValue = operands.Select(ParseImmediate).LastOrDefault(v => v != null);
        analysis.Alu = BuildAluOperation(analysis, mnemonic, operands);

        ushort nextIp = (ushort)(before.IP + instruction.Bytes.Length);
        analysis.Jumped = after.CS != before.CS || after.IP != nextIp;
        if (mnemonic == "INT" && operands.Count == 1 && ParseImmediate(operands[0]) is int n) analysis.Interrupt = n;
        return analysis;
    }

    /// <summary>Decodes from the pre-step memory image (self-modifying code safe).</summary>
    private static DisassembledInstruction DecodeAt(Memory memory, StepRecord record, ushort cs, ushort ip)
    {
        var shadow = new Memory();
        int start = Memory.Physical(cs, ip);
        var bytes = new byte[16];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = memory.Peek(start + i);
        foreach (var (address, oldValue, _) in record.MemoryWrites)
        {
            int offset = address - start;
            if (offset >= 0 && offset < bytes.Length) bytes[offset] = oldValue;
        }
        shadow.Load(start, bytes);
        return new Disassembler8086(shadow).Decode(cs, ip);
    }

    private static List<string> SplitOperands(string operands) =>
        string.IsNullOrWhiteSpace(operands)
            ? []
            : operands.Split(',').Select(o => o.Trim()).Where(o => o.Length > 0).ToList();

    private static ushort Value(CpuState s, string reg) => reg switch
    {
        "AX" => s.AX, "BX" => s.BX, "CX" => s.CX, "DX" => s.DX,
        "SI" => s.SI, "DI" => s.DI, "BP" => s.BP, "SP" => s.SP,
        "CS" => s.CS, "DS" => s.DS, "ES" => s.ES, "SS" => s.SS,
        "IP" => s.IP,
        "AL" => (ushort)(s.AX & 0xFF), "AH" => (ushort)(s.AX >> 8),
        "BL" => (ushort)(s.BX & 0xFF), "BH" => (ushort)(s.BX >> 8),
        "CL" => (ushort)(s.CX & 0xFF), "CH" => (ushort)(s.CX >> 8),
        "DL" => (ushort)(s.DX & 0xFF), "DH" => (ushort)(s.DX >> 8),
        _ => 0,
    };

    public static ushort RegisterValue(CpuState s, string reg) => Value(s, reg);

    private static void CollectRegisterChanges(StepAnalysis a)
    {
        foreach (var reg in Reg16.Concat(SegRegs))
        {
            ushort b = Value(a.Before, reg), c = Value(a.After, reg);
            if (b != c) a.RegistersWritten.Add(new RegisterChange(reg, b, c));
        }
    }

    private static void CollectFlagChanges(StepAnalysis a)
    {
        foreach (var (name, flag) in FlagBits)
        {
            bool b = (a.Before.Flags & (ushort)flag) != 0, c = (a.After.Flags & (ushort)flag) != 0;
            if (b != c) a.FlagsChanged.Add(new FlagChange(name, b, c));
        }
    }

    private static void CollectMemory(StepAnalysis a, StepRecord record, int instructionLength)
    {
        var seen = new HashSet<int>();
        foreach (var (address, value) in record.MemoryReads)
        {
            int offset = (address - a.FetchAddress) & (Memory.Size - 1);
            if (offset < instructionLength) continue; // instruction fetch, shown separately
            if (seen.Add(address)) a.MemoryReads.Add(new MemoryAccess(address, value, false));
        }
        foreach (var (address, oldValue, newValue) in record.MemoryWrites)
            a.MemoryWrites.Add(new MemoryAccess(address, newValue, true, oldValue));
    }

    private static void CollectRegisterReads(StepAnalysis a, string mnemonic, List<string> operands)
    {
        var reads = new List<string>();
        for (int i = 0; i < operands.Count; i++)
        {
            string op = operands[i];
            var mem = MemoryOperand().Match(op);
            if (mem.Success)
            {
                foreach (var part in mem.Groups["inner"].Value.Split('+', '-'))
                    if (Reg16.Contains(part.Trim())) reads.Add(part.Trim());
                continue;
            }
            string reg = op.Trim().ToUpperInvariant();
            bool isRegister = Reg16.Contains(reg) || Reg8.Contains(reg) || SegRegs.Contains(reg);
            if (!isRegister) continue;
            bool destinationOnly = i == 0 && operands.Count > 1 && WriteOnlyDestination.Contains(mnemonic);
            if (!destinationOnly) reads.Add(reg);
        }

        // Implicit operands.
        switch (mnemonic)
        {
            case "MUL" or "IMUL" or "DIV" or "IDIV" when operands.Count == 1:
                reads.Add(IsByteOperand(operands[0]) ? "AL" : "AX");
                if (mnemonic.EndsWith("DIV")) reads.Add(IsByteOperand(operands[0]) ? "AH" : "DX");
                break;
            case "LOOP" or "LOOPE" or "LOOPNE" or "JCXZ":
                reads.Add("CX");
                break;
            case "PUSH" or "POP" or "CALL" or "RET" or "RETF" or "IRET" or "PUSHF" or "POPF" or "INT":
                reads.Add("SP");
                break;
            case "MOVSB" or "MOVSW" or "CMPSB" or "CMPSW" or "LODSB" or "LODSW":
                reads.Add("SI");
                if (mnemonic.StartsWith("MOVS") || mnemonic.StartsWith("CMPS")) reads.Add("DI");
                break;
            case "STOSB" or "STOSW" or "SCASB" or "SCASW":
                reads.Add("DI");
                reads.Add(mnemonic.EndsWith('B') ? "AL" : "AX");
                break;
            case "XLATB":
                reads.Add("BX");
                reads.Add("AL");
                break;
            case "CBW":
                reads.Add("AL");
                break;
            case "CWD":
                reads.Add("AX");
                break;
        }
        if (a.Instruction.Mnemonic.StartsWith("REP")) reads.Add("CX");
        a.RegistersRead.AddRange(reads.Distinct());
    }

    private static bool IsByteOperand(string operand)
    {
        string op = operand.Trim().ToUpperInvariant();
        return Reg8.Contains(op) || op.StartsWith("BYTE PTR");
    }

    public static int? ParseImmediate(string operand)
    {
        string op = operand.Trim();
        if (op.Length == 0 || op.Contains('[') || op.Contains(':')) return null;
        if (op.EndsWith('h') || op.EndsWith('H'))
            return int.TryParse(op[..^1], NumberStyles.HexNumber, null, out int h) ? h : null;
        return int.TryParse(op, out int d) ? d : null;
    }

    private static AddressCalculation? AddressOf(string operand, CpuState s)
    {
        var mem = MemoryOperand().Match(operand);
        if (!mem.Success) return null;
        string before = operand[..mem.Index].Replace("WORD PTR", "").Replace("BYTE PTR", "").Replace("DWORD PTR", "").Trim();
        var segMatch = SegmentPrefix().Match(before.Length > 0 ? before : "");
        string inner = mem.Groups["inner"].Value;

        string? baseReg = null, indexReg = null;
        int disp = 0;
        foreach (Match term in Regex.Matches(inner, @"([+-]?)([^+-]+)"))
        {
            string sign = term.Groups[1].Value;
            string t = term.Groups[2].Value.Trim();
            if (t is "BX" or "BP") baseReg = t;
            else if (t is "SI" or "DI") indexReg = t;
            else if (ParseImmediate(t) is int v) disp += sign == "-" ? -v : v;
        }

        string segment = segMatch.Success ? segMatch.Groups["seg"].Value : baseReg == "BP" ? "SS" : "DS";
        ushort segValue = Value(s, segment);
        ushort baseValue = baseReg != null ? Value(s, baseReg) : (ushort)0;
        ushort indexValue = indexReg != null ? Value(s, indexReg) : (ushort)0;
        ushort ea = (ushort)(baseValue + indexValue + disp);
        return new AddressCalculation(segment, segValue, baseReg, baseValue, indexReg, indexValue, disp, ea, Memory.Physical(segValue, ea));
    }

    private static AluOperation? BuildAluOperation(StepAnalysis a, string mnemonic, List<string> operands)
    {
        if (!AluMnemonics.Contains(mnemonic)) return null;
        bool word = operands.Count == 0 || !IsByteOperand(operands[0]);

        int? Operand(string op)
        {
            string reg = op.Trim().ToUpperInvariant();
            if (Reg16.Contains(reg) || Reg8.Contains(reg)) return Value(a.Before, reg);
            if (ParseImmediate(op) is int imm) return imm;
            if (MemoryOperand().IsMatch(op) && a.Address is { } addr)
            {
                var bytes = a.MemoryReads.Where(r => r.Address == addr.PhysicalAddress || r.Address == addr.PhysicalAddress + 1).ToList();
                if (bytes.Count == 0) return null;
                int lo = bytes.FirstOrDefault(b => b.Address == addr.PhysicalAddress)?.Value ?? 0;
                int hi = bytes.FirstOrDefault(b => b.Address == addr.PhysicalAddress + 1)?.Value ?? 0;
                return word ? lo | (hi << 8) : lo;
            }
            return null;
        }

        int? opA = operands.Count > 0 ? Operand(operands[0]) : null;
        int? opB = operands.Count > 1 ? Operand(operands[1]) : null;

        // Result: the destination's new value (for CMP/TEST the flags are the result).
        int result = 0;
        if (operands.Count > 0)
        {
            string dest = operands[0].Trim().ToUpperInvariant();
            if (Reg16.Contains(dest) || Reg8.Contains(dest)) result = Value(a.After, dest);
            else if (a.MemoryWrites.Count > 0)
                result = a.MemoryWrites[0].Value | (a.MemoryWrites.Count > 1 && word ? a.MemoryWrites[1].Value << 8 : 0);
        }
        if (mnemonic is "MUL" or "IMUL" or "DIV" or "IDIV" && operands.Count == 1)
        {
            opB = opA;
            opA = word ? a.Before.AX : a.Before.AX & 0xFF;
            result = a.After.AX;
        }
        if (mnemonic is "CBW" or "CWD" or "DAA" or "DAS" or "AAA" or "AAS" or "AAM" or "AAD")
        {
            opA = a.Before.AX;
            result = a.After.AX;
        }
        return new AluOperation(mnemonic, opA, opB, result, word);
    }
}
