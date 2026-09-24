using Emu8086.Core.Cpu;
using Emu8086.Core.Disassembler;

namespace Emu8086.Core.Analysis;

/// <summary>A sentence template key ("explain.&lt;key&gt;") and its arguments.</summary>
public sealed record Explanation(string Key, string[] Args);

/// <summary>
/// Describes what an instruction does with the current register values, e.g.
/// "ADD AX, BX" with AX=5, BX=3 becomes key "ADD" with args ["AX (0005h)", "BX (0003h)"].
/// The sentences themselves live in the language files.
/// </summary>
public static class InstructionExplainer
{
    private static readonly Dictionary<string, Func<CpuState, ushort>> Registers16 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AX"] = s => s.AX, ["BX"] = s => s.BX, ["CX"] = s => s.CX, ["DX"] = s => s.DX,
        ["SI"] = s => s.SI, ["DI"] = s => s.DI, ["BP"] = s => s.BP, ["SP"] = s => s.SP,
        ["CS"] = s => s.CS, ["DS"] = s => s.DS, ["ES"] = s => s.ES, ["SS"] = s => s.SS,
    };

    private static readonly Dictionary<string, (string Reg, bool High)> Registers8 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = ("AX", false), ["AH"] = ("AX", true), ["BL"] = ("BX", false), ["BH"] = ("BX", true),
        ["CL"] = ("CX", false), ["CH"] = ("CX", true), ["DL"] = ("DX", false), ["DH"] = ("DX", true),
    };

    /// <summary>Instructions with their own sentence; others fall back to a group or "generic".</summary>
    private static readonly HashSet<string> Known =
    [
        "MOV", "ADD", "ADC", "SUB", "SBB", "CMP", "AND", "OR", "XOR", "TEST", "INC", "DEC", "NEG", "NOT",
        "MUL", "IMUL", "DIV", "IDIV", "PUSH", "POP", "PUSHF", "POPF", "CALL", "RET", "RETF", "IRET", "JMP",
        "LOOP", "LOOPE", "LOOPNE", "JCXZ", "INT", "LEA", "XCHG", "HLT", "NOP", "IN", "OUT", "CBW", "CWD",
        "CLC", "STC", "CMC", "CLD", "STD", "CLI", "STI", "LODSB", "LODSW", "STOSB", "STOSW", "MOVSB", "MOVSW",
        "CMPSB", "CMPSW", "SCASB", "SCASW", "XLAT",
    ];

    private static readonly HashSet<string> Shifts = ["SHL", "SAL", "SHR", "SAR", "ROL", "ROR", "RCL", "RCR"];

    public static Explanation Explain(DisassembledInstruction instruction, CpuState state)
    {
        string[] words = instruction.Mnemonic.Split(' ');
        string m = words[^1].ToUpperInvariant();
        m = m switch { "LOOPZ" => "LOOPE", "LOOPNZ" => "LOOPNE", "RETN" => "RET", "XLATB" => "XLAT", "SAL" => "SHL", _ => m };
        var operands = OperandText.Split(instruction.Operands).Select(o => Describe(o, state)).ToArray();

        if (words.Length > 1 && words[0].StartsWith("REP")) return new Explanation("rep", [m, $"{state.CX:X4}h"]);
        if (m == "INT" && operands.Length == 1)
            return new Explanation("INT", [operands[0], $"{state.AX >> 8:X2}h"]);
        if (m is "LOOP" or "LOOPE" or "LOOPNE" or "JCXZ")
            return new Explanation(m, [.. operands, $"{state.CX:X4}h"]);

        string key = Known.Contains(m) ? m
            : Shifts.Contains(m) ? "shift"
            : m.StartsWith('J') ? "jcc"
            : "generic";
        if (key == "shift") return new Explanation(key, [m, .. operands]);
        if (key == "jcc") return new Explanation(key, [.. operands, m]);
        if (key == "generic") return new Explanation(key, [instruction.Text]);
        return new Explanation(key, operands);
    }

    /// <summary>A register operand gets its current value appended; anything else is shown as written.</summary>
    private static string Describe(string operand, CpuState state)
    {
        if (Registers16.TryGetValue(operand, out var get)) return $"{operand} ({get(state):X4}h)";
        if (Registers8.TryGetValue(operand, out var r8))
        {
            ushort value = Registers16[r8.Reg](state);
            return $"{operand} ({(r8.High ? value >> 8 : value & 0xFF):X2}h)";
        }
        return operand;
    }
}
