using Emu8086.Core.Cpu;

namespace Emu8086.Core.Disassembler;

public sealed record DisassembledInstruction(ushort Segment, ushort Offset, byte[] Bytes, string Mnemonic, string Operands)
{
    public string Text => Operands.Length == 0 ? Mnemonic : $"{Mnemonic} {Operands}";
}

/// <summary>Decodes the same instruction set that <see cref="Cpu8086"/> executes, in MASM syntax.</summary>
public sealed class Disassembler8086
{
    private static readonly string[] R8 = ["AL", "CL", "DL", "BL", "AH", "CH", "DH", "BH"];
    private static readonly string[] R16 = ["AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI"];
    private static readonly string[] Sreg = ["ES", "CS", "SS", "DS"];
    private static readonly string[] Alu = ["ADD", "OR", "ADC", "SBB", "AND", "SUB", "XOR", "CMP"];
    private static readonly string[] Shifts = ["ROL", "ROR", "RCL", "RCR", "SHL", "SHR", "SAL", "SAR"];
    private static readonly string[] Group3 = ["TEST", "TEST", "NOT", "NEG", "MUL", "IMUL", "DIV", "IDIV"];
    private static readonly string[] Jcc = ["JO", "JNO", "JB", "JAE", "JE", "JNE", "JBE", "JA", "JS", "JNS", "JP", "JNP", "JL", "JGE", "JLE", "JG"];
    private static readonly string[] Ea = ["BX+SI", "BX+DI", "BP+SI", "BP+DI", "SI", "DI", "BP", "BX"];

    private readonly Memory _mem;
    private ushort _seg, _off, _start;
    private string? _segPrefix;
    private int _mod, _reg, _rm;
    private string _rmText = "";

    public Disassembler8086(Memory memory)
    {
        _mem = memory;
    }

    private byte Next() => _mem.Read8(_seg, _off++);

    private ushort Next16()
    {
        ushort v = _mem.Read16(_seg, _off);
        _off += 2;
        return v;
    }

    private static string H8(int v) => Hex($"{v & 0xFF:X2}");
    private static string H16(int v) => Hex($"{v & 0xFFFF:X4}");

    /// <summary>MASM hex literal: a leading 0 keeps it from looking like an identifier.</summary>
    private static string Hex(string digits) => (char.IsLetter(digits[0]) ? "0" : "") + digits + "h";

    private void ModRm(bool w)
    {
        byte b = Next();
        _mod = b >> 6;
        _reg = (b >> 3) & 7;
        _rm = b & 7;
        if (_mod == 3)
        {
            _rmText = w ? R16[_rm] : R8[_rm];
            return;
        }
        string inner;
        if (_mod == 0 && _rm == 6) inner = H16(Next16());
        else
        {
            inner = Ea[_rm];
            if (_mod == 1)
            {
                sbyte d = (sbyte)Next();
                inner += d < 0 ? $"-{H8(-d)}" : $"+{H8(d)}";
            }
            else if (_mod == 2) inner += $"+{H16(Next16())}";
        }
        _rmText = $"{_segPrefix}[{inner}]";
    }

    private string Sized(bool w) => _mod == 3 ? _rmText : (w ? "WORD PTR " : "BYTE PTR ") + _rmText;

    public DisassembledInstruction Decode(ushort segment, ushort offset)
    {
        _seg = segment;
        _off = offset;
        _start = offset;
        _segPrefix = null;
        string prefix = "";
        (string m, string o) result;

        while (true)
        {
            byte op = Next();
            switch (op)
            {
                case 0x26: case 0x2E: case 0x36: case 0x3E:
                    _segPrefix = Sreg[(op >> 3) & 3] + ":";
                    if (_off - _start > 6) { result = ("DB", H8(op)); goto done; }
                    continue;
                case 0xF0: prefix += "LOCK "; continue;
                case 0xF2: prefix += "REPNE "; continue;
                case 0xF3: prefix += "REP "; continue;
            }
            result = DecodeOpcode(op);
            break;
        }
        done:
        int length = (ushort)(_off - _start);
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = _mem.Read8(segment, (ushort)(offset + i));
        return new DisassembledInstruction(segment, offset, bytes, prefix + result.m, result.o);
    }

    public List<DisassembledInstruction> DecodeMany(ushort segment, ushort offset, int count)
    {
        var list = new List<DisassembledInstruction>(count);
        for (int i = 0; i < count; i++)
        {
            var ins = Decode(segment, offset);
            list.Add(ins);
            offset = (ushort)(offset + ins.Bytes.Length);
        }
        return list;
    }

    private string Rel8()
    {
        sbyte r = (sbyte)Next();
        return H16(_off + r);
    }

    private string Rel16()
    {
        short r = (short)Next16();
        return H16(_off + r);
    }

    private (string, string) DecodeOpcode(byte op)
    {
        if (op < 0x40 && (op & 7) < 6)
        {
            string name = Alu[op >> 3];
            bool w = (op & 1) != 0;
            switch (op & 7)
            {
                case 0: case 1: ModRm(w); return (name, $"{_rmText}, {(w ? R16 : R8)[_reg]}");
                case 2: case 3: ModRm(w); return (name, $"{(w ? R16 : R8)[_reg]}, {_rmText}");
                case 4: return (name, $"AL, {H8(Next())}");
                default: return (name, $"AX, {H16(Next16())}");
            }
        }

        switch (op)
        {
            case 0x06: case 0x0E: case 0x16: case 0x1E: return ("PUSH", Sreg[op >> 3]);
            case 0x07: case 0x17: case 0x1F: return ("POP", Sreg[op >> 3]);
            case 0x27: return ("DAA", "");
            case 0x2F: return ("DAS", "");
            case 0x37: return ("AAA", "");
            case 0x3F: return ("AAS", "");
            case >= 0x40 and <= 0x47: return ("INC", R16[op & 7]);
            case >= 0x48 and <= 0x4F: return ("DEC", R16[op & 7]);
            case >= 0x50 and <= 0x57: return ("PUSH", R16[op & 7]);
            case >= 0x58 and <= 0x5F: return ("POP", R16[op & 7]);
            case 0x60: return ("PUSHA", "");
            case 0x61: return ("POPA", "");
            case 0x62: ModRm(true); return ("BOUND", $"{R16[_reg]}, {_rmText}");
            case 0x68: return ("PUSH", H16(Next16()));
            case 0x6A: return ("PUSH", H8(Next()));
            case 0x69: ModRm(true); return ("IMUL", $"{R16[_reg]}, {_rmText}, {H16(Next16())}");
            case 0x6B: ModRm(true); return ("IMUL", $"{R16[_reg]}, {_rmText}, {H8(Next())}");
            case 0x6C: return ("INSB", "");
            case 0x6D: return ("INSW", "");
            case 0x6E: return ("OUTSB", "");
            case 0x6F: return ("OUTSW", "");
            case >= 0x70 and <= 0x7F: return (Jcc[op & 0xF], Rel8());
            case 0x80: case 0x81: case 0x82: case 0x83:
            {
                bool w = (op & 1) != 0;
                ModRm(w);
                string imm = op switch
                {
                    0x81 => H16(Next16()),
                    0x83 => H16((sbyte)Next()),
                    _ => H8(Next()),
                };
                return (Alu[_reg], $"{Sized(w)}, {imm}");
            }
            case 0x84: case 0x85: { bool w = (op & 1) != 0; ModRm(w); return ("TEST", $"{_rmText}, {(w ? R16 : R8)[_reg]}"); }
            case 0x86: case 0x87: { bool w = (op & 1) != 0; ModRm(w); return ("XCHG", $"{_rmText}, {(w ? R16 : R8)[_reg]}"); }
            case 0x88: case 0x89: { bool w = (op & 1) != 0; ModRm(w); return ("MOV", $"{_rmText}, {(w ? R16 : R8)[_reg]}"); }
            case 0x8A: case 0x8B: { bool w = (op & 1) != 0; ModRm(w); return ("MOV", $"{(w ? R16 : R8)[_reg]}, {_rmText}"); }
            case 0x8C: ModRm(true); return ("MOV", $"{_rmText}, {Sreg[_reg & 3]}");
            case 0x8D: ModRm(true); return ("LEA", $"{R16[_reg]}, {_rmText}");
            case 0x8E: ModRm(true); return ("MOV", $"{Sreg[_reg & 3]}, {_rmText}");
            case 0x8F: ModRm(true); return ("POP", Sized(true));
            case 0x90: return ("NOP", "");
            case >= 0x91 and <= 0x97: return ("XCHG", $"AX, {R16[op & 7]}");
            case 0x98: return ("CBW", "");
            case 0x99: return ("CWD", "");
            case 0x9A: { ushort o = Next16(); ushort s = Next16(); return ("CALL", $"{s:X4}h:{o:X4}h"); }
            case 0x9B: return ("WAIT", "");
            case 0x9C: return ("PUSHF", "");
            case 0x9D: return ("POPF", "");
            case 0x9E: return ("SAHF", "");
            case 0x9F: return ("LAHF", "");
            case 0xA0: return ("MOV", $"AL, {_segPrefix}[{H16(Next16())}]");
            case 0xA1: return ("MOV", $"AX, {_segPrefix}[{H16(Next16())}]");
            case 0xA2: return ("MOV", $"{_segPrefix}[{H16(Next16())}], AL");
            case 0xA3: return ("MOV", $"{_segPrefix}[{H16(Next16())}], AX");
            case 0xA4: return ("MOVSB", "");
            case 0xA5: return ("MOVSW", "");
            case 0xA6: return ("CMPSB", "");
            case 0xA7: return ("CMPSW", "");
            case 0xA8: return ("TEST", $"AL, {H8(Next())}");
            case 0xA9: return ("TEST", $"AX, {H16(Next16())}");
            case 0xAA: return ("STOSB", "");
            case 0xAB: return ("STOSW", "");
            case 0xAC: return ("LODSB", "");
            case 0xAD: return ("LODSW", "");
            case 0xAE: return ("SCASB", "");
            case 0xAF: return ("SCASW", "");
            case >= 0xB0 and <= 0xB7: return ("MOV", $"{R8[op & 7]}, {H8(Next())}");
            case >= 0xB8 and <= 0xBF: return ("MOV", $"{R16[op & 7]}, {H16(Next16())}");
            case 0xC0: case 0xC1: { bool w = (op & 1) != 0; ModRm(w); return (Shifts[_reg], $"{Sized(w)}, {Next()}"); }
            case 0xC2: return ("RET", H16(Next16()));
            case 0xC3: return ("RET", "");
            case 0xC4: ModRm(true); return ("LES", $"{R16[_reg]}, {_rmText}");
            case 0xC5: ModRm(true); return ("LDS", $"{R16[_reg]}, {_rmText}");
            case 0xC6: ModRm(false); return ("MOV", $"{Sized(false)}, {H8(Next())}");
            case 0xC7: ModRm(true); return ("MOV", $"{Sized(true)}, {H16(Next16())}");
            case 0xC8: { ushort s = Next16(); return ("ENTER", $"{H16(s)}, {Next()}"); }
            case 0xC9: return ("LEAVE", "");
            case 0xCA: return ("RETF", H16(Next16()));
            case 0xCB: return ("RETF", "");
            case 0xCC: return ("INT", "3");
            case 0xCD: return ("INT", H8(Next()));
            case 0xCE: return ("INTO", "");
            case 0xCF: return ("IRET", "");
            case 0xD0: case 0xD1: { bool w = (op & 1) != 0; ModRm(w); return (Shifts[_reg], $"{Sized(w)}, 1"); }
            case 0xD2: case 0xD3: { bool w = (op & 1) != 0; ModRm(w); return (Shifts[_reg], $"{Sized(w)}, CL"); }
            case 0xD4: { byte b = Next(); return ("AAM", b == 10 ? "" : H8(b)); }
            case 0xD5: { byte b = Next(); return ("AAD", b == 10 ? "" : H8(b)); }
            case 0xD6: return ("SALC", "");
            case 0xD7: return ("XLATB", "");
            case >= 0xD8 and <= 0xDF: ModRm(true); return ("ESC", _rmText);
            case 0xE0: return ("LOOPNE", Rel8());
            case 0xE1: return ("LOOPE", Rel8());
            case 0xE2: return ("LOOP", Rel8());
            case 0xE3: return ("JCXZ", Rel8());
            case 0xE4: return ("IN", $"AL, {H8(Next())}");
            case 0xE5: return ("IN", $"AX, {H8(Next())}");
            case 0xE6: return ("OUT", $"{H8(Next())}, AL");
            case 0xE7: return ("OUT", $"{H8(Next())}, AX");
            case 0xE8: return ("CALL", Rel16());
            case 0xE9: return ("JMP", Rel16());
            case 0xEA: { ushort o = Next16(); ushort s = Next16(); return ("JMP", $"{s:X4}h:{o:X4}h"); }
            case 0xEB: return ("JMP", Rel8());
            case 0xEC: return ("IN", "AL, DX");
            case 0xED: return ("IN", "AX, DX");
            case 0xEE: return ("OUT", "DX, AL");
            case 0xEF: return ("OUT", "DX, AX");
            case 0xF4: return ("HLT", "");
            case 0xF5: return ("CMC", "");
            case 0xF6: case 0xF7:
            {
                bool w = (op & 1) != 0;
                ModRm(w);
                if (_reg < 2) return ("TEST", $"{Sized(w)}, {(w ? H16(Next16()) : H8(Next()))}");
                return (Group3[_reg], Sized(w));
            }
            case 0xF8: return ("CLC", "");
            case 0xF9: return ("STC", "");
            case 0xFA: return ("CLI", "");
            case 0xFB: return ("STI", "");
            case 0xFC: return ("CLD", "");
            case 0xFD: return ("STD", "");
            case 0xFE:
                ModRm(false);
                return _reg switch { 0 => ("INC", Sized(false)), 1 => ("DEC", Sized(false)), _ => ("DB", "0FEh") };
            case 0xFF:
                ModRm(true);
                return _reg switch
                {
                    0 => ("INC", Sized(true)),
                    1 => ("DEC", Sized(true)),
                    2 => ("CALL", _rmText),
                    3 => ("CALL", "DWORD PTR " + _rmText),
                    4 => ("JMP", _rmText),
                    5 => ("JMP", "DWORD PTR " + _rmText),
                    6 => ("PUSH", Sized(true)),
                    _ => ("DB", "0FFh"),
                };
            default:
                return ("DB", H8(op));
        }
    }
}
