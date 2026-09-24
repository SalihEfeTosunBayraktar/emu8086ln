namespace Emu8086.Core.Assembler;

public sealed partial class Assembler8086
{
    private static readonly Dictionary<string, int> SimpleOpcodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOP"] = 0x90, ["HLT"] = 0xF4, ["CLC"] = 0xF8, ["STC"] = 0xF9, ["CMC"] = 0xF5,
        ["CLD"] = 0xFC, ["STD"] = 0xFD, ["CLI"] = 0xFA, ["STI"] = 0xFB,
        ["PUSHF"] = 0x9C, ["POPF"] = 0x9D, ["SAHF"] = 0x9E, ["LAHF"] = 0x9F,
        ["CBW"] = 0x98, ["CWD"] = 0x99, ["PUSHA"] = 0x60, ["POPA"] = 0x61, ["LEAVE"] = 0xC9,
        ["IRET"] = 0xCF, ["INTO"] = 0xCE, ["WAIT"] = 0x9B, ["FWAIT"] = 0x9B, ["XLATB"] = 0xD7,
        ["DAA"] = 0x27, ["DAS"] = 0x2F, ["AAA"] = 0x37, ["AAS"] = 0x3F, ["SALC"] = 0xD6,
        ["MOVSB"] = 0xA4, ["MOVSW"] = 0xA5, ["CMPSB"] = 0xA6, ["CMPSW"] = 0xA7,
        ["STOSB"] = 0xAA, ["STOSW"] = 0xAB, ["LODSB"] = 0xAC, ["LODSW"] = 0xAD,
        ["SCASB"] = 0xAE, ["SCASW"] = 0xAF, ["INSB"] = 0x6C, ["INSW"] = 0x6D,
        ["OUTSB"] = 0x6E, ["OUTSW"] = 0x6F,
    };

    private static readonly Dictionary<string, int> Prefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REP"] = 0xF3, ["REPE"] = 0xF3, ["REPZ"] = 0xF3, ["REPNE"] = 0xF2, ["REPNZ"] = 0xF2, ["LOCK"] = 0xF0,
    };

    private static readonly Dictionary<string, int> AluOps = new(StringComparer.OrdinalIgnoreCase)
        { ["ADD"] = 0, ["OR"] = 1, ["ADC"] = 2, ["SBB"] = 3, ["AND"] = 4, ["SUB"] = 5, ["XOR"] = 6, ["CMP"] = 7 };

    private static readonly Dictionary<string, int> ShiftOps = new(StringComparer.OrdinalIgnoreCase)
        { ["ROL"] = 0, ["ROR"] = 1, ["RCL"] = 2, ["RCR"] = 3, ["SHL"] = 4, ["SAL"] = 4, ["SHR"] = 5, ["SAR"] = 7 };

    private static readonly Dictionary<string, int> Group3Ops = new(StringComparer.OrdinalIgnoreCase)
        { ["NOT"] = 2, ["NEG"] = 3, ["MUL"] = 4, ["IMUL"] = 5, ["DIV"] = 6, ["IDIV"] = 7 };

    private static readonly Dictionary<string, int> ConditionalJumps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JO"] = 0x0, ["JNO"] = 0x1, ["JB"] = 0x2, ["JC"] = 0x2, ["JNAE"] = 0x2,
        ["JNB"] = 0x3, ["JAE"] = 0x3, ["JNC"] = 0x3, ["JE"] = 0x4, ["JZ"] = 0x4,
        ["JNE"] = 0x5, ["JNZ"] = 0x5, ["JBE"] = 0x6, ["JNA"] = 0x6, ["JA"] = 0x7, ["JNBE"] = 0x7,
        ["JS"] = 0x8, ["JNS"] = 0x9, ["JP"] = 0xA, ["JPE"] = 0xA, ["JNP"] = 0xB, ["JPO"] = 0xB,
        ["JL"] = 0xC, ["JNGE"] = 0xC, ["JGE"] = 0xD, ["JNL"] = 0xD, ["JLE"] = 0xE, ["JNG"] = 0xE,
        ["JG"] = 0xF, ["JNLE"] = 0xF,
    };

    private static readonly Dictionary<string, int> LoopOps = new(StringComparer.OrdinalIgnoreCase)
        { ["LOOPNE"] = 0xE0, ["LOOPNZ"] = 0xE0, ["LOOPE"] = 0xE1, ["LOOPZ"] = 0xE1, ["LOOP"] = 0xE2, ["JCXZ"] = 0xE3 };

    private static readonly Dictionary<string, (int Byte, int Word)> StringOps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MOVS"] = (0xA4, 0xA5), ["CMPS"] = (0xA6, 0xA7), ["STOS"] = (0xAA, 0xAB),
        ["LODS"] = (0xAC, 0xAD), ["SCAS"] = (0xAE, 0xAF), ["INS"] = (0x6C, 0x6D), ["OUTS"] = (0x6E, 0x6F),
    };

    private static readonly HashSet<string> OtherMnemonics = new(StringComparer.OrdinalIgnoreCase)
    {
        "MOV", "XCHG", "PUSH", "POP", "LEA", "LDS", "LES", "IN", "OUT", "INC", "DEC", "TEST", "INT",
        "RET", "RETN", "RETF", "JMP", "CALL", "AAM", "AAD", "ENTER", "BOUND", "XLAT",
    };

    /// <summary>All instruction mnemonics the assembler accepts (used by the editor for highlighting).</summary>
    public static IEnumerable<string> Mnemonics =>
        SimpleOpcodes.Keys.Concat(Prefixes.Keys).Concat(AluOps.Keys).Concat(ShiftOps.Keys).Concat(Group3Ops.Keys)
            .Concat(ConditionalJumps.Keys).Concat(LoopOps.Keys).Concat(StringOps.Keys).Concat(OtherMnemonics);

    private static bool IsKnownMnemonic(string name) =>
        SimpleOpcodes.ContainsKey(name) || Prefixes.ContainsKey(name) || AluOps.ContainsKey(name)
        || ShiftOps.ContainsKey(name) || Group3Ops.ContainsKey(name) || ConditionalJumps.ContainsKey(name)
        || LoopOps.ContainsKey(name) || StringOps.ContainsKey(name) || OtherMnemonics.Contains(name);

    #region Emission helpers

    private void B(int value) => Seg.Emit((byte)value);

    private void W(int value)
    {
        B(value);
        B(value >> 8);
    }

    private void AddFixup(int targetSegment)
    {
        Seg.Fixups.Add((Seg.Location, targetSegment));
        W(0);
    }

    private void EmitWord(ExprValue v)
    {
        if (v.SegmentValue >= 0)
        {
            AddFixup(v.SegmentValue);
            return;
        }
        CheckRange(v, 2);
        W((int)v.Num);
    }

    private void CheckRange(ExprValue v, int size)
    {
        if (v.Undefined) return;
        long min = size == 1 ? -128 : -32768;
        long max = size == 1 ? 0xFF : 0xFFFF;
        if (v.Num < min || v.Num > max) throw new AsmException(AsmErrorCode.ValueOutOfRange, v.Num.ToString());
    }

    private void EmitImmediate(Operand imm, int size)
    {
        if (imm.Value.SegmentValue >= 0 && size == 1) throw new AsmException(AsmErrorCode.ValueOutOfRange, "SEG");
        if (size == 1)
        {
            CheckRange(imm.Value, 1);
            B((int)imm.Value.Num);
        }
        else EmitWord(imm.Value);
    }

    private static int DefaultSegment(ExprValue v) => v.Base == 5 ? 2 : 3; // BP-based addressing uses SS

    private void SegmentPrefix(Operand op)
    {
        if (op.Kind != OperandKind.Mem || op.Value.SegOverride < 0) return;
        if (op.Value.SegOverride != DefaultSegment(op.Value) || op.Value.Base < 0 && op.Value.Index < 0 && op.Value.SegOverride != 3)
            B(0x26 | (op.Value.SegOverride << 3));
    }

    /// <summary>Emits the ModR/M byte and displacement for register field <paramref name="regField"/>.</summary>
    private void ModRm(int regField, Operand rm)
    {
        if (rm.IsReg)
        {
            B(0xC0 | (regField << 3) | rm.Reg);
            return;
        }
        if (rm.Kind != OperandKind.Mem) throw new AsmException(AsmErrorCode.InvalidOperands);

        var v = rm.Value;
        long disp = v.Num;
        if (v.Base < 0 && v.Index < 0)
        {
            B((regField << 3) | 6);
            if (!v.Undefined && (disp < -32768 || disp > 0xFFFF))
                throw new AsmException(AsmErrorCode.ValueOutOfRange, disp.ToString());
            W((int)disp);
            return;
        }

        int rmBits = (v.Base, v.Index) switch
        {
            (3, 6) => 0,
            (3, 7) => 1,
            (5, 6) => 2,
            (5, 7) => 3,
            (-1, 6) => 4,
            (-1, 7) => 5,
            (5, -1) => 6,
            (3, -1) => 7,
            _ => throw new AsmException(AsmErrorCode.InvalidAddressing, ""),
        };

        bool dispIsLabel = v.SegmentOf >= 0 || v.Undefined;
        if (!dispIsLabel && disp == 0 && rmBits != 6)
        {
            B((regField << 3) | rmBits);
        }
        else if (!dispIsLabel && disp is >= -128 and <= 127)
        {
            B(0x40 | (regField << 3) | rmBits);
            B((int)disp);
        }
        else
        {
            B(0x80 | (regField << 3) | rmBits);
            W((int)disp);
        }
    }

    private static int ResolveSize(Operand a, Operand? b)
    {
        int sa = a.Size;
        int sb = b?.Size ?? 0;
        if (sa != 0 && sb != 0 && sa != sb) throw new AsmException(AsmErrorCode.SizeMismatch);
        int size = sa != 0 ? sa : sb;
        if (size == 0) throw new AsmException(AsmErrorCode.SizeUnknown);
        if (size is not (1 or 2)) throw new AsmException(AsmErrorCode.SizeMismatch);
        return size;
    }

    #endregion

    private void EncodeInstruction(List<Token> tokens, int p)
    {
        var mnemonicToken = tokens[p];
        if (mnemonicToken.Kind != TokenKind.Identifier)
            throw new AsmException(AsmErrorCode.SyntaxError, mnemonicToken.Text);
        string m = mnemonicToken.Upper;

        if (Prefixes.TryGetValue(m, out int prefix))
        {
            B(prefix);
            if (p + 1 < tokens.Count) EncodeInstruction(tokens, p + 1);
            return;
        }

        if (!IsKnownMnemonic(m)) throw new AsmException(AsmErrorCode.UnknownInstruction, mnemonicToken.Text);

        var ops = Operand.Split(tokens, p + 1).Select(t => Operand.Parse(t, this)).ToList();
        Encode(m, ops);
    }

    private static void RequireCount(List<Operand> ops, int min, int max = -1)
    {
        if (max < 0) max = min;
        if (ops.Count < min || ops.Count > max) throw new AsmException(AsmErrorCode.OperandCount);
    }

    private void Encode(string m, List<Operand> ops)
    {
        if (SimpleOpcodes.TryGetValue(m, out int simple))
        {
            RequireCount(ops, 0);
            B(simple);
            return;
        }
        if (AluOps.TryGetValue(m, out int alu)) { EncodeAlu(alu, ops); return; }
        if (ShiftOps.TryGetValue(m, out int shift)) { EncodeShift(shift, ops); return; }
        if (Group3Ops.TryGetValue(m, out int g3)) { EncodeGroup3(m, g3, ops); return; }
        if (ConditionalJumps.TryGetValue(m, out int cc)) { EncodeJcc(cc, ops); return; }
        if (LoopOps.TryGetValue(m, out int loop)) { EncodeLoop(loop, ops); return; }
        if (StringOps.TryGetValue(m, out var str)) { EncodeString(m, str, ops); return; }

        switch (m)
        {
            case "MOV": EncodeMov(ops); break;
            case "XCHG": EncodeXchg(ops); break;
            case "PUSH": EncodePush(ops); break;
            case "POP": EncodePop(ops); break;
            case "LEA": EncodeLoadAddress(0x8D, ops); break;
            case "LDS": EncodeLoadAddress(0xC5, ops); break;
            case "LES": EncodeLoadAddress(0xC4, ops); break;
            case "BOUND": EncodeLoadAddress(0x62, ops); break;
            case "IN": EncodeIn(ops); break;
            case "OUT": EncodeOut(ops); break;
            case "INC": EncodeIncDec(0, ops); break;
            case "DEC": EncodeIncDec(1, ops); break;
            case "TEST": EncodeTest(ops); break;
            case "INT": EncodeInt(ops); break;
            case "RET": EncodeRet(ops, _procStack.Count > 0 && _procStack.Peek().Far); break;
            case "RETN": EncodeRet(ops, false); break;
            case "RETF": EncodeRet(ops, true); break;
            case "JMP": EncodeJmpCall(ops, isCall: false); break;
            case "CALL": EncodeJmpCall(ops, isCall: true); break;
            case "AAM":
            case "AAD":
                RequireCount(ops, 0, 1);
                B(m == "AAM" ? 0xD4 : 0xD5);
                B(ops.Count == 1 ? (int)ops[0].Value.Num : 10);
                break;
            case "ENTER":
                RequireCount(ops, 2);
                B(0xC8);
                W((int)ops[0].Value.Num);
                B((int)ops[1].Value.Num);
                break;
            case "XLAT":
                RequireCount(ops, 0, 1);
                if (ops.Count == 1) SegmentPrefix(ops[0]);
                B(0xD7);
                break;
            default:
                throw new AsmException(AsmErrorCode.UnknownInstruction, m);
        }
    }

    private void EncodeMov(List<Operand> ops)
    {
        RequireCount(ops, 2);
        var dst = ops[0];
        var src = ops[1];

        if (dst.Kind == OperandKind.Imm) throw new AsmException(AsmErrorCode.ImmediateDestination);

        if (dst.Kind == OperandKind.Seg || src.Kind == OperandKind.Seg)
        {
            if (dst.Kind == OperandKind.Seg && src.Kind == OperandKind.Seg || src.Kind == OperandKind.Imm)
                throw new AsmException(AsmErrorCode.SegmentImmediate);
            if (dst.Kind == OperandKind.Seg)
            {
                if (src.Size is not (0 or 2)) throw new AsmException(AsmErrorCode.SizeMismatch);
                SegmentPrefix(src);
                B(0x8E);
                ModRm(dst.Reg, src);
            }
            else
            {
                if (dst.Size is not (0 or 2)) throw new AsmException(AsmErrorCode.SizeMismatch);
                SegmentPrefix(dst);
                B(0x8C);
                ModRm(src.Reg, dst);
            }
            return;
        }

        if (dst.Kind == OperandKind.Mem && src.Kind == OperandKind.Mem) throw new AsmException(AsmErrorCode.MemoryToMemory);

        if (src.Kind == OperandKind.Imm)
        {
            int size = ResolveSize(dst, null);
            if (dst.IsReg)
            {
                B((size == 1 ? 0xB0 : 0xB8) + dst.Reg);
            }
            else
            {
                SegmentPrefix(dst);
                B(size == 1 ? 0xC6 : 0xC7);
                ModRm(0, dst);
            }
            EmitImmediate(src, size);
            return;
        }

        int w = ResolveSize(dst, src) == 2 ? 1 : 0;
        // Accumulator <-> direct address has a short form.
        if (dst.IsAcc && src.Kind == OperandKind.Mem && !src.Value.HasRegisters)
        {
            SegmentPrefix(src);
            B(0xA0 | w);
            W((int)src.Value.Num);
            return;
        }
        if (src.IsAcc && dst.Kind == OperandKind.Mem && !dst.Value.HasRegisters)
        {
            SegmentPrefix(dst);
            B(0xA2 | w);
            W((int)dst.Value.Num);
            return;
        }
        if (src.IsReg)
        {
            SegmentPrefix(dst);
            B(0x88 | w);
            ModRm(src.Reg, dst);
        }
        else
        {
            SegmentPrefix(src);
            B(0x8A | w);
            ModRm(dst.Reg, src);
        }
    }

    private void EncodeAlu(int alu, List<Operand> ops)
    {
        RequireCount(ops, 2);
        var dst = ops[0];
        var src = ops[1];
        if (dst.Kind == OperandKind.Imm) throw new AsmException(AsmErrorCode.ImmediateDestination);
        if (dst.Kind == OperandKind.Seg || src.Kind == OperandKind.Seg) throw new AsmException(AsmErrorCode.InvalidOperands);
        if (dst.Kind == OperandKind.Mem && src.Kind == OperandKind.Mem) throw new AsmException(AsmErrorCode.MemoryToMemory);

        if (src.Kind == OperandKind.Imm)
        {
            int size = ResolveSize(dst, null);
            if (dst.IsAcc)
            {
                B((alu << 3) | (size == 1 ? 4 : 5));
                EmitImmediate(src, size);
                return;
            }
            bool shortImm = size == 2 && !src.Value.Undefined && src.Value.SegmentValue < 0
                            && src.Value.SegmentOf < 0 && src.Value.Num is >= -128 and <= 127;
            SegmentPrefix(dst);
            B(size == 1 ? 0x80 : shortImm ? 0x83 : 0x81);
            ModRm(alu, dst);
            if (shortImm) B((int)src.Value.Num);
            else EmitImmediate(src, size);
            return;
        }

        int w = ResolveSize(dst, src) == 2 ? 1 : 0;
        if (src.IsReg)
        {
            SegmentPrefix(dst);
            B((alu << 3) | w);
            ModRm(src.Reg, dst);
        }
        else
        {
            SegmentPrefix(src);
            B((alu << 3) | 2 | w);
            ModRm(dst.Reg, src);
        }
    }

    private void EncodeTest(List<Operand> ops)
    {
        RequireCount(ops, 2);
        var a = ops[0];
        var b = ops[1];
        if (a.Kind == OperandKind.Imm) (a, b) = (b, a);
        if (!a.IsRm) throw new AsmException(AsmErrorCode.InvalidOperands);
        if (b.Kind == OperandKind.Imm)
        {
            int size = ResolveSize(a, null);
            if (a.IsAcc) B(size == 1 ? 0xA8 : 0xA9);
            else
            {
                SegmentPrefix(a);
                B(size == 1 ? 0xF6 : 0xF7);
                ModRm(0, a);
            }
            EmitImmediate(b, size);
            return;
        }
        if (a.Kind == OperandKind.Mem && b.Kind == OperandKind.Mem) throw new AsmException(AsmErrorCode.MemoryToMemory);
        int w = ResolveSize(a, b) == 2 ? 1 : 0;
        var (reg, rm) = b.IsReg ? (b, a) : (a, b);
        SegmentPrefix(rm);
        B(0x84 | w);
        ModRm(reg.Reg, rm);
    }

    private void EncodeXchg(List<Operand> ops)
    {
        RequireCount(ops, 2);
        var a = ops[0];
        var b = ops[1];
        if (!a.IsRm || !b.IsRm) throw new AsmException(AsmErrorCode.InvalidOperands);
        if (a.Kind == OperandKind.Mem && b.Kind == OperandKind.Mem) throw new AsmException(AsmErrorCode.MemoryToMemory);
        int w = ResolveSize(a, b) == 2 ? 1 : 0;
        if (w == 1 && a.Kind == OperandKind.Reg16 && b.Kind == OperandKind.Reg16 && (a.Reg == 0 || b.Reg == 0))
        {
            B(0x90 + (a.Reg == 0 ? b.Reg : a.Reg));
            return;
        }
        var (reg, rm) = b.IsReg ? (b, a) : (a, b);
        SegmentPrefix(rm);
        B(0x86 | w);
        ModRm(reg.Reg, rm);
    }

    private void EncodePush(List<Operand> ops)
    {
        RequireCount(ops, 1);
        var op = ops[0];
        switch (op.Kind)
        {
            case OperandKind.Reg16: B(0x50 + op.Reg); break;
            case OperandKind.Seg: B(0x06 | (op.Reg << 3)); break;
            case OperandKind.Imm:
                if (!op.Value.Undefined && op.Value.SegmentValue < 0 && op.Value.Num is >= -128 and <= 127)
                {
                    B(0x6A);
                    B((int)op.Value.Num);
                }
                else
                {
                    B(0x68);
                    EmitWord(op.Value);
                }
                break;
            case OperandKind.Mem:
                if (op.Size is not (0 or 2)) throw new AsmException(AsmErrorCode.SizeMismatch);
                SegmentPrefix(op);
                B(0xFF);
                ModRm(6, op);
                break;
            default: throw new AsmException(AsmErrorCode.InvalidOperands);
        }
    }

    private void EncodePop(List<Operand> ops)
    {
        RequireCount(ops, 1);
        var op = ops[0];
        switch (op.Kind)
        {
            case OperandKind.Reg16: B(0x58 + op.Reg); break;
            case OperandKind.Seg:
                if (op.Reg == 1) throw new AsmException(AsmErrorCode.PopCs);
                B(0x07 | (op.Reg << 3));
                break;
            case OperandKind.Mem:
                if (op.Size is not (0 or 2)) throw new AsmException(AsmErrorCode.SizeMismatch);
                SegmentPrefix(op);
                B(0x8F);
                ModRm(0, op);
                break;
            default: throw new AsmException(AsmErrorCode.InvalidOperands);
        }
    }

    private void EncodeLoadAddress(int opcode, List<Operand> ops)
    {
        RequireCount(ops, 2);
        if (ops[0].Kind != OperandKind.Reg16 || ops[1].Kind != OperandKind.Mem)
            throw new AsmException(AsmErrorCode.InvalidOperands);
        if (opcode != 0x8D) SegmentPrefix(ops[1]);
        B(opcode);
        ModRm(ops[0].Reg, ops[1]);
    }

    private void EncodeIn(List<Operand> ops)
    {
        RequireCount(ops, 2);
        if (!ops[0].IsAcc) throw new AsmException(AsmErrorCode.InvalidOperands);
        int w = ops[0].Kind == OperandKind.Reg16 ? 1 : 0;
        if (ops[1].Kind == OperandKind.Reg16 && ops[1].Reg == 2) B(0xEC | w);
        else if (ops[1].Kind == OperandKind.Imm)
        {
            B(0xE4 | w);
            CheckRange(ops[1].Value, 1);
            B((int)ops[1].Value.Num);
        }
        else throw new AsmException(AsmErrorCode.InvalidOperands);
    }

    private void EncodeOut(List<Operand> ops)
    {
        RequireCount(ops, 2);
        if (!ops[1].IsAcc) throw new AsmException(AsmErrorCode.InvalidOperands);
        int w = ops[1].Kind == OperandKind.Reg16 ? 1 : 0;
        if (ops[0].Kind == OperandKind.Reg16 && ops[0].Reg == 2) B(0xEE | w);
        else if (ops[0].Kind == OperandKind.Imm)
        {
            B(0xE6 | w);
            CheckRange(ops[0].Value, 1);
            B((int)ops[0].Value.Num);
        }
        else throw new AsmException(AsmErrorCode.InvalidOperands);
    }

    private void EncodeIncDec(int sub, List<Operand> ops)
    {
        RequireCount(ops, 1);
        var op = ops[0];
        if (op.Kind == OperandKind.Reg16)
        {
            B((sub == 0 ? 0x40 : 0x48) + op.Reg);
            return;
        }
        if (!op.IsRm) throw new AsmException(AsmErrorCode.InvalidOperands);
        int size = ResolveSize(op, null);
        SegmentPrefix(op);
        B(size == 1 ? 0xFE : 0xFF);
        ModRm(sub, op);
    }

    private void EncodeGroup3(string m, int sub, List<Operand> ops)
    {
        // 80186 IMUL reg, r/m, imm  and  IMUL reg, imm
        if (m == "IMUL" && ops.Count >= 2)
        {
            RequireCount(ops, 2, 3);
            var dst = ops[0];
            var src = ops.Count == 3 ? ops[1] : ops[0];
            var imm = ops[^1];
            if (dst.Kind != OperandKind.Reg16 || imm.Kind != OperandKind.Imm || !src.IsRm)
                throw new AsmException(AsmErrorCode.InvalidOperands);
            bool shortImm = !imm.Value.Undefined && imm.Value.Num is >= -128 and <= 127;
            SegmentPrefix(src);
            B(shortImm ? 0x6B : 0x69);
            ModRm(dst.Reg, src);
            if (shortImm) B((int)imm.Value.Num); else EmitWord(imm.Value);
            return;
        }

        RequireCount(ops, 1);
        var op = ops[0];
        if (!op.IsRm) throw new AsmException(AsmErrorCode.InvalidOperands);
        int size = ResolveSize(op, null);
        SegmentPrefix(op);
        B(size == 1 ? 0xF6 : 0xF7);
        ModRm(sub, op);
    }

    private void EncodeShift(int sub, List<Operand> ops)
    {
        RequireCount(ops, 1, 2);
        var dst = ops[0];
        if (!dst.IsRm) throw new AsmException(AsmErrorCode.InvalidOperands);
        int w = ResolveSize(dst, null) == 2 ? 1 : 0;

        if (ops.Count == 1 || ops[1].Kind == OperandKind.Imm && !ops[1].Value.Undefined && ops[1].Value.Num == 1)
        {
            SegmentPrefix(dst);
            B(0xD0 | w);
            ModRm(sub, dst);
        }
        else if (ops[1].Kind == OperandKind.Reg8 && ops[1].Reg == 1)
        {
            SegmentPrefix(dst);
            B(0xD2 | w);
            ModRm(sub, dst);
        }
        else if (ops[1].Kind == OperandKind.Imm)
        {
            SegmentPrefix(dst);
            B(0xC0 | w);
            ModRm(sub, dst);
            CheckRange(ops[1].Value, 1);
            B((int)ops[1].Value.Num);
        }
        else throw new AsmException(AsmErrorCode.InvalidOperands);
    }

    private void EncodeInt(List<Operand> ops)
    {
        RequireCount(ops, 1);
        if (ops[0].Kind != OperandKind.Imm) throw new AsmException(AsmErrorCode.InvalidOperands);
        CheckRange(ops[0].Value, 1);
        int n = (int)ops[0].Value.Num & 0xFF;
        if (n == 3) B(0xCC);
        else
        {
            B(0xCD);
            B(n);
        }
    }

    private void EncodeRet(List<Operand> ops, bool far)
    {
        RequireCount(ops, 0, 1);
        if (ops.Count == 0)
        {
            B(far ? 0xCB : 0xC3);
            return;
        }
        B(far ? 0xCA : 0xC2);
        W((int)ops[0].Value.Num);
    }

    private void EncodeString(string m, (int Byte, int Word) opcodes, List<Operand> ops)
    {
        RequireCount(ops, 0, 2);
        int size = ops.Count == 0 ? 0 : ops.Select(o => o.Size).FirstOrDefault(s => s != 0);
        if (ops.Count == 0 || size == 0) throw new AsmException(AsmErrorCode.SizeUnknown);

        // The source operand (DS:SI) may carry a segment override.
        var source = m switch
        {
            "MOVS" or "CMPS" => ops.Count == 2 ? (m == "MOVS" ? ops[1] : ops[0]) : null,
            "LODS" or "OUTS" => ops[^1],
            _ => null,
        };
        if (source != null && source.Value.SegOverride >= 0 && source.Value.SegOverride != 3)
            B(0x26 | (source.Value.SegOverride << 3));
        B(size == 1 ? opcodes.Byte : opcodes.Word);
    }

    #region Jumps

    private bool IsDirectTarget(Operand op) =>
        op.Kind == OperandKind.Imm
        || op.Kind == OperandKind.Mem && !op.Value.Bracketed && !op.Value.HasRegisters
           && (op.Value.Undefined || op.Value.Symbol?.Kind is SymbolKind.Label or SymbolKind.Procedure);

    private bool IsFarTarget(ExprValue v) =>
        v.Distance == JumpDistance.Far
        || v.SegmentOf >= 0 && v.SegmentOf != CurrentSegment;

    private long RelativeTo(ExprValue target, int instructionLength) =>
        target.Num - (CurrentLocation + instructionLength);

    private static bool FitsShort(long rel) => rel is >= -128 and <= 127;

    private void EncodeJmpCall(List<Operand> ops, bool isCall)
    {
        RequireCount(ops, 1);
        var op = ops[0];

        if (!IsDirectTarget(op))
        {
            if (!op.IsRm) throw new AsmException(AsmErrorCode.InvalidOperands);
            bool far = op.Kind == OperandKind.Mem && (op.Value.Size == 4 || op.Value.Distance == JumpDistance.Far);
            if (!far && op.Size is not (0 or 2)) throw new AsmException(AsmErrorCode.SizeMismatch);
            SegmentPrefix(op);
            B(0xFF);
            ModRm(isCall ? (far ? 3 : 2) : (far ? 5 : 4), op);
            return;
        }

        var v = op.Value;
        if (IsFarTarget(v))
        {
            B(isCall ? 0x9A : 0xEA);
            W((int)v.Num);
            if (v.SegmentOf >= 0) AddFixup(v.SegmentOf);
            else W(0);
            return;
        }

        if (isCall)
        {
            long rel = RelativeTo(v, 3);
            B(0xE8);
            W((int)rel);
            return;
        }

        long shortRel = RelativeTo(v, 2);
        bool useShort = v.Distance == JumpDistance.Short
                        || v.Distance != JumpDistance.Near && !v.Undefined && FitsShort(shortRel);
        if (useShort)
        {
            if (!v.Undefined && !FitsShort(shortRel))
                throw new AsmException(AsmErrorCode.JumpOutOfRange, shortRel.ToString());
            B(0xEB);
            B((int)shortRel);
            return;
        }
        long nearRel = RelativeTo(v, 3);
        B(0xE9);
        W((int)nearRel);
    }

    private void EncodeJcc(int cc, List<Operand> ops)
    {
        RequireCount(ops, 1);
        if (!IsDirectTarget(ops[0])) throw new AsmException(AsmErrorCode.InvalidOperands);
        var v = ops[0].Value;
        long rel = RelativeTo(v, 2);
        if (v.Undefined || FitsShort(rel))
        {
            B(0x70 | cc);
            B((int)rel);
            return;
        }
        // Out of range: invert the condition and jump over a near JMP (as emu8086 does).
        long longRel = RelativeTo(v, 5);
        B(0x70 | (cc ^ 1));
        B(3);
        B(0xE9);
        W((int)longRel);
    }

    private void EncodeLoop(int opcode, List<Operand> ops)
    {
        RequireCount(ops, 1);
        if (!IsDirectTarget(ops[0])) throw new AsmException(AsmErrorCode.InvalidOperands);
        var v = ops[0].Value;
        long rel = RelativeTo(v, 2);
        if (!v.Undefined && !FitsShort(rel)) throw new AsmException(AsmErrorCode.JumpOutOfRange, rel.ToString());
        B(opcode);
        B((int)rel);
    }

    #endregion
}
