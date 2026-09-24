using Emu8086.Core.Disassembler;

namespace Emu8086.Core.Analysis;

/// <summary>
/// Approximate 8086 clock counts from the Intel 8086 user's manual. Data-dependent instructions
/// (MUL, DIV, shifts by CL) use the middle of their range; memory operands add the effective
/// address time. Good for comparing two solutions, not for exact timing.
/// </summary>
public static class CycleTable
{
    public const double ClockMHz = 4.77;

    private enum Form { None, RegReg, RegImm, RegMem, MemReg, MemImm, Reg, Mem, Imm }

    private static readonly string[] Reg8 = ["AL", "BL", "CL", "DL", "AH", "BH", "CH", "DH"];
    private static readonly string[] SegRegs = ["CS", "DS", "ES", "SS"];

    /// <param name="instruction">The executed instruction.</param>
    /// <param name="jumped">A conditional jump or loop was taken.</param>
    /// <param name="repeats">Iterations performed by a REP-prefixed string instruction.</param>
    public static int Estimate(DisassembledInstruction instruction, bool jumped, int repeats = 0)
    {
        string[] parts = instruction.Mnemonic.Split(' ');
        string m = parts[^1];
        bool rep = parts.Length > 1 && parts[0].StartsWith("REP");
        var operands = OperandText.Split(instruction.Operands);
        var form = FormOf(operands);
        int ea = operands.Where(IsMemory).Select(EffectiveAddress).DefaultIfEmpty(0).Max();
        bool wide = !operands.Any(o => Reg8.Contains(o.ToUpperInvariant())) && !operands.Any(o => o.StartsWith("BYTE", StringComparison.OrdinalIgnoreCase));

        if (rep) return 9 + repeats * StringCost(m, repeated: true);

        return m switch
        {
            "MOV" => operands.Any(o => SegRegs.Contains(o.ToUpperInvariant()))
                ? form is Form.RegReg ? 2 : form is Form.RegMem ? 8 + ea : 9 + ea
                : Pick(form, ea, 2, 4, 8, 9, 10),
            "ADD" or "ADC" or "SUB" or "SBB" or "AND" or "OR" or "XOR" => Pick(form, ea, 3, 4, 9, 16, 17),
            "CMP" => Pick(form, ea, 3, 4, 9, 9, 10),
            "TEST" => Pick(form, ea, 3, 5, 9, 9, 11),
            "INC" or "DEC" => form == Form.Mem ? 15 + ea : wide ? 2 : 3,
            "NEG" or "NOT" => form == Form.Mem ? 16 + ea : 3,
            "LEA" => 2 + ea,
            "LDS" or "LES" => 16 + ea,
            "XCHG" => form is Form.RegReg ? 4 : 17 + ea,
            "PUSH" => form == Form.Mem ? 16 + ea : SegRegs.Contains(operands.FirstOrDefault()?.ToUpperInvariant()) ? 10 : 11,
            "POP" => form == Form.Mem ? 17 + ea : 8,
            "PUSHF" => 10,
            "POPF" => 8,
            "CALL" => form == Form.Mem ? 21 + ea : form == Form.Reg ? 16 : instruction.Operands.Contains(':') ? 28 : 19,
            "JMP" => form == Form.Mem ? 18 + ea : form == Form.Reg ? 11 : 15,
            "RET" or "RETN" => operands.Count > 0 ? 20 : 16,
            "RETF" => operands.Count > 0 ? 25 : 26,
            "IRET" => 24,
            "INT" => operands.FirstOrDefault() == "3" ? 52 : 51,
            "INTO" => jumped ? 53 : 4,
            "LOOP" => jumped ? 17 : 5,
            "LOOPE" or "LOOPZ" => jumped ? 18 : 6,
            "LOOPNE" or "LOOPNZ" => jumped ? 19 : 5,
            "JCXZ" => jumped ? 18 : 6,
            _ when m.StartsWith('J') => jumped ? 16 : 4,
            "MUL" => (wide ? 126 : 74) + (form == Form.Mem ? ea + 6 : 0),
            "IMUL" => (wide ? 141 : 89) + (form == Form.Mem ? ea + 6 : 0),
            "DIV" => (wide ? 153 : 85) + (form == Form.Mem ? ea + 6 : 0),
            "IDIV" => (wide ? 175 : 107) + (form == Form.Mem ? ea + 6 : 0),
            "SHL" or "SAL" or "SHR" or "SAR" or "ROL" or "ROR" or "RCL" or "RCR" =>
                operands.Count > 1 && operands[1].Equals("CL", StringComparison.OrdinalIgnoreCase)
                    ? (form == Form.MemReg ? 20 + ea : 8) + 4 * 2
                    : form is Form.Mem or Form.MemImm ? 15 + ea : 2,
            "IN" or "OUT" => operands.Any(o => o.Equals("DX", StringComparison.OrdinalIgnoreCase)) ? 8 : 10,
            "MOVSB" or "MOVSW" or "CMPSB" or "CMPSW" or "SCASB" or "SCASW" or "LODSB" or "LODSW" or "STOSB" or "STOSW" =>
                StringCost(m, repeated: false),
            "XLAT" or "XLATB" => 11,
            "LAHF" or "SAHF" => 4,
            "CBW" => 2,
            "CWD" => 5,
            "AAA" or "AAS" or "DAA" or "DAS" => 4,
            "AAM" => 83,
            "AAD" => 60,
            "NOP" => 3,
            _ => 2,
        };
    }

    public static double Microseconds(long cycles) => cycles / ClockMHz;

    private static int StringCost(string m, bool repeated) => m[..^1] switch
    {
        "MOVS" => 17,
        "CMPS" => 22,
        "SCAS" => 15,
        "LODS" => repeated ? 13 : 12,
        "STOS" => repeated ? 10 : 11,
        _ => 10,
    };

    private static int Pick(Form form, int ea, int regReg, int regImm, int regMem, int memReg, int memImm) => form switch
    {
        Form.RegReg => regReg,
        Form.RegImm => regImm,
        Form.RegMem => regMem + ea,
        Form.MemReg => memReg + ea,
        Form.MemImm => memImm + ea,
        _ => regReg,
    };

    private static bool IsMemory(string operand) => operand.Contains('[');

    private static bool IsImmediate(string operand) => operand.Length > 0 && char.IsDigit(operand[0]);

    private static Form FormOf(List<string> operands)
    {
        if (operands.Count == 0) return Form.None;
        if (operands.Count == 1)
            return IsMemory(operands[0]) ? Form.Mem : IsImmediate(operands[0]) ? Form.Imm : Form.Reg;
        bool destMem = IsMemory(operands[0]);
        if (IsImmediate(operands[1])) return destMem ? Form.MemImm : Form.RegImm;
        if (destMem) return Form.MemReg;
        return IsMemory(operands[1]) ? Form.RegMem : Form.RegReg;
    }

    /// <summary>Effective address time: 6 for a direct address, 5 for one register, 7-8 for two, +4 with a displacement.</summary>
    private static int EffectiveAddress(string operand)
    {
        string inner = operand[(operand.IndexOf('[') + 1)..operand.LastIndexOf(']')].ToUpperInvariant();
        var terms = inner.Split('+', '-').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        int registers = terms.Count(t => t is "BX" or "BP" or "SI" or "DI");
        bool displacement = terms.Count > registers;
        return registers switch
        {
            0 => 6,
            1 => displacement ? 9 : 5,
            _ => displacement ? 12 : 8,
        };
    }
}
