namespace Emu8086.Core.Cpu;

public sealed partial class Cpu8086
{
    private int _repPrefix;

    private StepResult DivideError()
    {
        Interrupt(0);
        return StepResult.Ok;
    }

    private StepResult ExecuteInstruction()
    {
        while (true)
        {
            byte op = Fetch8();
            switch (op)
            {
                // Prefixes
                case 0x26: _segOverride = SegES; continue;
                case 0x2E: _segOverride = SegCS; continue;
                case 0x36: _segOverride = SegSS; continue;
                case 0x3E: _segOverride = SegDS; continue;
                case 0xF0: case 0xF1: continue; // LOCK
                case 0xF2: case 0xF3: _repPrefix = op; continue;
            }
            return Execute(op);
        }
    }

    private StepResult Execute(byte op)
    {
        // ALU r/m, reg / reg, r/m / acc, imm  (00-3F excluding prefixes and BCD/segment ops)
        if (op < 0x40 && (op & 7) < 6)
        {
            int aluOp = op >> 3;
            bool w = (op & 1) != 0;
            switch (op & 7)
            {
                case 0:
                case 1:
                {
                    DecodeModRm();
                    int r = Alu(aluOp, ReadRm(w), ReadReg(w), w);
                    if (aluOp != 7) WriteRm(w, r);
                    return StepResult.Ok;
                }
                case 2:
                case 3:
                {
                    DecodeModRm();
                    int r = Alu(aluOp, ReadReg(w), ReadRm(w), w);
                    if (aluOp != 7) WriteReg(w, r);
                    return StepResult.Ok;
                }
                case 4:
                {
                    int r = Alu(aluOp, AL, Fetch8(), false);
                    if (aluOp != 7) AL = (byte)r;
                    return StepResult.Ok;
                }
                default:
                {
                    int r = Alu(aluOp, AX, Fetch16(), true);
                    if (aluOp != 7) AX = (ushort)r;
                    return StepResult.Ok;
                }
            }
        }

        switch (op)
        {
            case 0x06: Push(ES); break;
            case 0x07: ES = Pop(); break;
            case 0x0E: Push(CS); break;
            case 0x0F: Interrupt(6); break; // invalid opcode (80186+)
            case 0x16: Push(SS); break;
            case 0x17: SS = Pop(); break;
            case 0x1E: Push(DS); break;
            case 0x1F: DS = Pop(); break;
            case 0x27: Daa(); break;
            case 0x2F: Das(); break;
            case 0x37: Aaa(); break;
            case 0x3F: Aas(); break;

            case >= 0x40 and <= 0x47: _r[op & 7] = (ushort)Inc(_r[op & 7], true); break;
            case >= 0x48 and <= 0x4F: _r[op & 7] = (ushort)Dec(_r[op & 7], true); break;
            case >= 0x50 and <= 0x57:
            {
                ushort v = _r[op & 7];
                Push((op & 7) == RegSP ? (ushort)(v - 2) : v);
                break;
            }
            case >= 0x58 and <= 0x5F: _r[op & 7] = Pop(); break;

            case 0x60: // PUSHA
            {
                ushort sp = SP;
                Push(AX); Push(CX); Push(DX); Push(BX);
                Push(sp); Push(BP); Push(SI); Push(DI);
                break;
            }
            case 0x61: // POPA
                DI = Pop(); SI = Pop(); BP = Pop(); Pop();
                BX = Pop(); DX = Pop(); CX = Pop(); AX = Pop();
                break;
            case 0x62: // BOUND
            {
                DecodeModRm();
                short idx = (short)ReadReg(true);
                short lo = (short)_mem.Read16(_eaSeg, _eaOff);
                short hi = (short)_mem.Read16(_eaSeg, (ushort)(_eaOff + 2));
                if (idx < lo || idx > hi)
                {
                    IP = InstructionIP;
                    Interrupt(5);
                }
                break;
            }
            case 0x68: Push(Fetch16()); break;
            case 0x6A: Push((ushort)(sbyte)Fetch8()); break;
            case 0x69:
            case 0x6B:
            {
                DecodeModRm();
                int a = (short)ReadRm(true);
                int b = op == 0x69 ? (short)Fetch16() : (sbyte)Fetch8();
                int r = a * b;
                WriteReg(true, r);
                CF = OF = r != (short)r;
                break;
            }
            case 0x6C: case 0x6D: case 0x6E: case 0x6F: StringOp(op); break;

            case >= 0x70 and <= 0x7F:
            {
                sbyte rel = (sbyte)Fetch8();
                if (Condition(op & 0x0F)) IP = (ushort)(IP + rel);
                break;
            }

            case 0x80: case 0x81: case 0x82: case 0x83:
            {
                bool w = (op & 1) != 0;
                DecodeModRm();
                int a = ReadRm(w);
                int b = op switch
                {
                    0x81 => Fetch16(),
                    0x83 => (ushort)(sbyte)Fetch8(),
                    _ => Fetch8(),
                };
                int r = Alu(_reg, a, b, w);
                if (_reg != 7) WriteRm(w, r);
                break;
            }
            case 0x84: case 0x85:
            {
                bool w = (op & 1) != 0;
                DecodeModRm();
                Logic(ReadRm(w) & ReadReg(w), w);
                break;
            }
            case 0x86: case 0x87:
            {
                bool w = (op & 1) != 0;
                DecodeModRm();
                int a = ReadRm(w);
                WriteRm(w, ReadReg(w));
                WriteReg(w, a);
                break;
            }
            case 0x88: case 0x89:
            {
                bool w = (op & 1) != 0;
                DecodeModRm();
                WriteRm(w, ReadReg(w));
                break;
            }
            case 0x8A: case 0x8B:
            {
                bool w = (op & 1) != 0;
                DecodeModRm();
                WriteReg(w, ReadRm(w));
                break;
            }
            case 0x8C: DecodeModRm(); WriteRm(true, _s[_reg & 3]); break;
            case 0x8D: DecodeModRm(); _r[_reg] = _eaOff; break;
            case 0x8E: DecodeModRm(); _s[_reg & 3] = (ushort)ReadRm(true); break;
            case 0x8F: DecodeModRm(); WriteRm(true, Pop()); break;

            case 0x90: break; // NOP
            case >= 0x91 and <= 0x97:
                (_r[op & 7], AX) = (AX, _r[op & 7]);
                break;
            case 0x98: AX = (ushort)(sbyte)AL; break; // CBW
            case 0x99: DX = (AX & 0x8000) != 0 ? (ushort)0xFFFF : (ushort)0; break; // CWD
            case 0x9A: // CALL far
            {
                ushort off = Fetch16();
                ushort seg = Fetch16();
                Push(CS);
                Push(IP);
                CS = seg;
                IP = off;
                break;
            }
            case 0x9B: break; // WAIT
            case 0x9C: Push(Flags); break;
            case 0x9D: Flags = (ushort)((Pop() & 0x0FD5) | 0x0002); break;
            case 0x9E: Flags = (ushort)((Flags & 0xFF00) | (AH & 0xD5) | 0x02); break; // SAHF
            case 0x9F: AH = (byte)Flags; break; // LAHF

            case 0xA0: AL = _mem.Read8(DataSeg(SegDS), Fetch16()); break;
            case 0xA1: AX = _mem.Read16(DataSeg(SegDS), Fetch16()); break;
            case 0xA2: _mem.Write8(DataSeg(SegDS), Fetch16(), AL); break;
            case 0xA3: _mem.Write16(DataSeg(SegDS), Fetch16(), AX); break;
            case >= 0xA4 and <= 0xA7: StringOp(op); break;
            case 0xA8: Logic(AL & Fetch8(), false); break;
            case 0xA9: Logic(AX & Fetch16(), true); break;
            case >= 0xAA and <= 0xAF: StringOp(op); break;

            case >= 0xB0 and <= 0xB7: SetReg8(op & 7, Fetch8()); break;
            case >= 0xB8 and <= 0xBF: _r[op & 7] = Fetch16(); break;

            case 0xC0: case 0xC1:
            {
                bool w = (op & 1) != 0;
                DecodeModRm();
                int v = ReadRm(w);
                WriteRm(w, Shift(_reg, v, Fetch8(), w));
                break;
            }
            case 0xC2: { ushort n = Fetch16(); IP = Pop(); SP += n; break; }
            case 0xC3: IP = Pop(); break;
            case 0xC4: case 0xC5:
            {
                DecodeModRm();
                _r[_reg] = _mem.Read16(_eaSeg, _eaOff);
                ushort seg = _mem.Read16(_eaSeg, (ushort)(_eaOff + 2));
                if (op == 0xC4) ES = seg; else DS = seg;
                break;
            }
            case 0xC6: case 0xC7:
            {
                bool w = (op & 1) != 0;
                DecodeModRm();
                WriteRm(w, w ? Fetch16() : Fetch8());
                break;
            }
            case 0xC8: // ENTER
            {
                ushort size = Fetch16();
                int level = Fetch8() & 0x1F;
                Push(BP);
                ushort frame = SP;
                for (int i = 1; i < level; i++)
                {
                    BP -= 2;
                    Push(_mem.Read16(SS, BP));
                }
                if (level > 0) Push(frame);
                BP = frame;
                SP -= size;
                break;
            }
            case 0xC9: SP = BP; BP = Pop(); break; // LEAVE
            case 0xCA: { ushort n = Fetch16(); IP = Pop(); CS = Pop(); SP += n; break; }
            case 0xCB: IP = Pop(); CS = Pop(); break;
            case 0xCC: return SoftwareInterrupt(3);
            case 0xCD: return SoftwareInterrupt(Fetch8());
            case 0xCE: if (OF) return SoftwareInterrupt(4); break;
            case 0xCF: IP = Pop(); CS = Pop(); Flags = (ushort)((Pop() & 0x0FD5) | 0x0002); break;

            case 0xD0: case 0xD1: case 0xD2: case 0xD3:
            {
                bool w = (op & 1) != 0;
                DecodeModRm();
                int count = op < 0xD2 ? 1 : CL;
                WriteRm(w, Shift(_reg, ReadRm(w), count, w));
                break;
            }
            case 0xD4: // AAM
            {
                int b = Fetch8();
                if (b == 0) return DivideError();
                AH = (byte)(AL / b);
                AL = (byte)(AL % b);
                SetSzp(AL, false);
                break;
            }
            case 0xD5: // AAD
            {
                int b = Fetch8();
                AL = (byte)(AL + AH * b);
                AH = 0;
                SetSzp(AL, false);
                break;
            }
            case 0xD6: AL = CF ? (byte)0xFF : (byte)0; break; // SALC
            case 0xD7: AL = _mem.Read8(DataSeg(SegDS), (ushort)(BX + AL)); break; // XLAT
            case >= 0xD8 and <= 0xDF: DecodeModRm(); break; // ESC (no FPU)

            case 0xE0: case 0xE1: case 0xE2:
            {
                sbyte rel = (sbyte)Fetch8();
                CX--;
                bool take = CX != 0 && (op == 0xE2 || (op == 0xE1 ? ZF : !ZF));
                if (take) IP = (ushort)(IP + rel);
                break;
            }
            case 0xE3: { sbyte rel = (sbyte)Fetch8(); if (CX == 0) IP = (ushort)(IP + rel); break; }
            case 0xE4: AL = (byte)PortIn(Fetch8(), false); break;
            case 0xE5: AX = (ushort)PortIn(Fetch8(), true); break;
            case 0xE6: PortOut(Fetch8(), AL, false); break;
            case 0xE7: PortOut(Fetch8(), AX, true); break;
            case 0xE8: { short rel = (short)Fetch16(); Push(IP); IP = (ushort)(IP + rel); break; }
            case 0xE9: { short rel = (short)Fetch16(); IP = (ushort)(IP + rel); break; }
            case 0xEA: { ushort off = Fetch16(); CS = Fetch16(); IP = off; break; }
            case 0xEB: { sbyte rel = (sbyte)Fetch8(); IP = (ushort)(IP + rel); break; }
            case 0xEC: AL = (byte)PortIn(DX, false); break;
            case 0xED: AX = (ushort)PortIn(DX, true); break;
            case 0xEE: PortOut(DX, AL, false); break;
            case 0xEF: PortOut(DX, AX, true); break;

            case 0xF4: Halted = true; return StepResult.Halted;
            case 0xF5: CF = !CF; break;
            case 0xF6: case 0xF7: return Group3((op & 1) != 0);
            case 0xF8: CF = false; break;
            case 0xF9: CF = true; break;
            case 0xFA: SetFlag(CpuFlags.IF, false); break;
            case 0xFB: SetFlag(CpuFlags.IF, true); break;
            case 0xFC: SetFlag(CpuFlags.DF, false); break;
            case 0xFD: SetFlag(CpuFlags.DF, true); break;
            case 0xFE:
            {
                DecodeModRm();
                int v = ReadRm(false);
                if (_reg == 0) WriteRm(false, Inc(v, false));
                else if (_reg == 1) WriteRm(false, Dec(v, false));
                else Interrupt(6);
                break;
            }
            case 0xFF: Group5(); break;

            default: // 0x63-0x67 and other undefined opcodes
                Interrupt(6);
                break;
        }
        return StepResult.Ok;
    }

    private StepResult Group3(bool w)
    {
        DecodeModRm();
        int v = ReadRm(w);
        switch (_reg)
        {
            case 0:
            case 1:
                Logic(v & (w ? Fetch16() : Fetch8()), w);
                break;
            case 2: WriteRm(w, ~v & Mask(w)); break;
            case 3:
            {
                int r = Sub(0, v, 0, w);
                CF = v != 0;
                WriteRm(w, r);
                break;
            }
            case 4: Multiply(v, w, false); break;
            case 5: Multiply(v, w, true); break;
            case 6: if (!Divide(v, w, false)) return DivideError(); break;
            default: if (!Divide(v, w, true)) return DivideError(); break;
        }
        return StepResult.Ok;
    }

    private void Group5()
    {
        DecodeModRm();
        switch (_reg)
        {
            case 0: WriteRm(true, Inc(ReadRm(true), true)); break;
            case 1: WriteRm(true, Dec(ReadRm(true), true)); break;
            case 2:
            {
                ushort target = (ushort)ReadRm(true);
                Push(IP);
                IP = target;
                break;
            }
            case 3:
            {
                ushort off = _mem.Read16(_eaSeg, _eaOff);
                ushort seg = _mem.Read16(_eaSeg, (ushort)(_eaOff + 2));
                Push(CS);
                Push(IP);
                CS = seg;
                IP = off;
                break;
            }
            case 4: IP = (ushort)ReadRm(true); break;
            case 5:
                IP = _mem.Read16(_eaSeg, _eaOff);
                CS = _mem.Read16(_eaSeg, (ushort)(_eaOff + 2));
                break;
            case 6: Push((ushort)ReadRm(true)); break;
            default: Interrupt(6); break;
        }
    }

    private int PortIn(int port, bool w) => Ports?.In(port, w) ?? (w ? 0xFFFF : 0xFF);

    private void PortOut(int port, int value, bool w) => Ports?.Out(port, value, w);

    private void StringOp(byte op)
    {
        bool compares = op is 0xA6 or 0xA7 or 0xAE or 0xAF;
        if (_repPrefix == 0)
        {
            StringOnce(op);
            return;
        }
        while (CX != 0)
        {
            StringOnce(op);
            CX--;
            if (!compares) continue;
            if (_repPrefix == 0xF3 && !ZF) break;
            if (_repPrefix == 0xF2 && ZF) break;
        }
    }

    private void StringOnce(byte op)
    {
        bool w = (op & 1) != 0;
        int step = (DF ? -1 : 1) * (w ? 2 : 1);
        ushort src = DataSeg(SegDS);
        switch (op)
        {
            case 0x6C: case 0x6D: // INS
                WriteString(ES, DI, PortIn(DX, w), w);
                DI = (ushort)(DI + step);
                break;
            case 0x6E: case 0x6F: // OUTS
                PortOut(DX, ReadString(src, SI, w), w);
                SI = (ushort)(SI + step);
                break;
            case 0xA4: case 0xA5: // MOVS
                WriteString(ES, DI, ReadString(src, SI, w), w);
                SI = (ushort)(SI + step);
                DI = (ushort)(DI + step);
                break;
            case 0xA6: case 0xA7: // CMPS
                Sub(ReadString(src, SI, w), ReadString(ES, DI, w), 0, w);
                SI = (ushort)(SI + step);
                DI = (ushort)(DI + step);
                break;
            case 0xAA: case 0xAB: // STOS
                WriteString(ES, DI, w ? AX : AL, w);
                DI = (ushort)(DI + step);
                break;
            case 0xAC: case 0xAD: // LODS
                if (w) AX = (ushort)ReadString(src, SI, true); else AL = (byte)ReadString(src, SI, false);
                SI = (ushort)(SI + step);
                break;
            default: // SCAS
                Sub(w ? AX : AL, ReadString(ES, DI, w), 0, w);
                DI = (ushort)(DI + step);
                break;
        }
    }

    private int ReadString(ushort seg, ushort off, bool w) => w ? _mem.Read16(seg, off) : _mem.Read8(seg, off);

    private void WriteString(ushort seg, ushort off, int value, bool w)
    {
        if (w) _mem.Write16(seg, off, (ushort)value);
        else _mem.Write8(seg, off, (byte)value);
    }
}
