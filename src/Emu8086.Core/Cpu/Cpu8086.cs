namespace Emu8086.Core.Cpu;

/// <summary>
/// Intel 8086 CPU (with the 80186 additions emu8086 accepts: PUSHA/POPA, PUSH imm,
/// IMUL imm, shifts by immediate, ENTER/LEAVE, BOUND, INS/OUTS).
/// </summary>
public sealed partial class Cpu8086
{
    /// <summary>Segment holding the IRET stubs that native BIOS/DOS services hook.</summary>
    public const ushort BiosSegment = 0xF000;
    public const int NativeVectorCount = 0x100;

    private const int RegAX = 0, RegCX = 1, RegDX = 2, RegBX = 3, RegSP = 4, RegBP = 5, RegSI = 6, RegDI = 7;
    private const int SegES = 0, SegCS = 1, SegSS = 2, SegDS = 3;

    private readonly Memory _mem;
    private readonly ushort[] _r = new ushort[8];
    private readonly ushort[] _s = new ushort[4];

    public Cpu8086(Memory memory)
    {
        _mem = memory;
        Reset();
    }

    public Memory Memory => _mem;
    public IPortBus? Ports { get; set; }
    public IInterruptHandler? Interrupts { get; set; }

    public ushort IP { get; set; }
    public ushort Flags { get; set; }
    public bool Halted { get; set; }
    public long InstructionCount { get; private set; }

    /// <summary>Address of the instruction currently executing (valid inside interrupt handlers).</summary>
    public ushort InstructionCS { get; private set; }
    public ushort InstructionIP { get; private set; }

    #region Registers

    public ushort AX { get => _r[RegAX]; set => _r[RegAX] = value; }
    public ushort CX { get => _r[RegCX]; set => _r[RegCX] = value; }
    public ushort DX { get => _r[RegDX]; set => _r[RegDX] = value; }
    public ushort BX { get => _r[RegBX]; set => _r[RegBX] = value; }
    public ushort SP { get => _r[RegSP]; set => _r[RegSP] = value; }
    public ushort BP { get => _r[RegBP]; set => _r[RegBP] = value; }
    public ushort SI { get => _r[RegSI]; set => _r[RegSI] = value; }
    public ushort DI { get => _r[RegDI]; set => _r[RegDI] = value; }

    public ushort ES { get => _s[SegES]; set => _s[SegES] = value; }
    public ushort CS { get => _s[SegCS]; set => _s[SegCS] = value; }
    public ushort SS { get => _s[SegSS]; set => _s[SegSS] = value; }
    public ushort DS { get => _s[SegDS]; set => _s[SegDS] = value; }

    public byte AL { get => GetReg8(0); set => SetReg8(0, value); }
    public byte CL { get => GetReg8(1); set => SetReg8(1, value); }
    public byte DL { get => GetReg8(2); set => SetReg8(2, value); }
    public byte BL { get => GetReg8(3); set => SetReg8(3, value); }
    public byte AH { get => GetReg8(4); set => SetReg8(4, value); }
    public byte CH { get => GetReg8(5); set => SetReg8(5, value); }
    public byte DH { get => GetReg8(6); set => SetReg8(6, value); }
    public byte BH { get => GetReg8(7); set => SetReg8(7, value); }

    public byte GetReg8(int index) =>
        index < 4 ? (byte)_r[index] : (byte)(_r[index - 4] >> 8);

    public void SetReg8(int index, int value)
    {
        if (index < 4) _r[index] = (ushort)((_r[index] & 0xFF00) | (value & 0xFF));
        else _r[index - 4] = (ushort)((_r[index - 4] & 0x00FF) | ((value & 0xFF) << 8));
    }

    public ushort GetReg16(int index) => _r[index];
    public void SetReg16(int index, int value) => _r[index] = (ushort)value;
    public ushort GetSeg(int index) => _s[index];
    public void SetSeg(int index, int value) => _s[index] = (ushort)value;

    public bool GetFlag(CpuFlags flag) => (Flags & (ushort)flag) != 0;

    public void SetFlag(CpuFlags flag, bool on) =>
        Flags = on ? (ushort)(Flags | (ushort)flag) : (ushort)(Flags & ~(ushort)flag);

    private bool CF { get => GetFlag(CpuFlags.CF); set => SetFlag(CpuFlags.CF, value); }
    private bool ZF { get => GetFlag(CpuFlags.ZF); set => SetFlag(CpuFlags.ZF, value); }
    private bool SF { get => GetFlag(CpuFlags.SF); set => SetFlag(CpuFlags.SF, value); }
    private bool OF { get => GetFlag(CpuFlags.OF); set => SetFlag(CpuFlags.OF, value); }
    private bool AF { get => GetFlag(CpuFlags.AF); set => SetFlag(CpuFlags.AF, value); }
    private bool PF { get => GetFlag(CpuFlags.PF); set => SetFlag(CpuFlags.PF, value); }
    private bool DF => GetFlag(CpuFlags.DF);

    #endregion

    public CpuState GetState() => new(AX, BX, CX, DX, SI, DI, BP, SP, CS, DS, ES, SS, IP, Flags, Halted);

    public void SetState(CpuState s)
    {
        AX = s.AX; BX = s.BX; CX = s.CX; DX = s.DX;
        SI = s.SI; DI = s.DI; BP = s.BP; SP = s.SP;
        CS = s.CS; DS = s.DS; ES = s.ES; SS = s.SS;
        IP = s.IP; Flags = s.Flags; Halted = s.Halted;
    }

    public void Reset()
    {
        Array.Clear(_r);
        Array.Clear(_s);
        IP = 0;
        Flags = 0x0002 | (ushort)CpuFlags.IF;
        Halted = false;
        InstructionCount = 0;
    }

    /// <summary>Points every interrupt vector at its IRET stub in the BIOS segment.</summary>
    public void InstallBiosStubs()
    {
        for (int n = 0; n < NativeVectorCount; n++)
        {
            _mem.Write16(n * 4, (ushort)n);
            _mem.Write16(n * 4 + 2, BiosSegment);
            _mem.Write8(BiosSegment, (ushort)n, 0xCF); // IRET
        }
    }

    #region Memory helpers

    private int _segOverride = -1;

    private byte Fetch8()
    {
        byte b = _mem.Read8(CS, IP);
        IP++;
        return b;
    }

    private ushort Fetch16()
    {
        ushort w = _mem.Read16(CS, IP);
        IP += 2;
        return w;
    }

    private ushort DataSeg(int defaultSeg) => _s[_segOverride >= 0 ? _segOverride : defaultSeg];

    public void Push(ushort value)
    {
        SP -= 2;
        _mem.Write16(SS, SP, value);
    }

    public ushort Pop()
    {
        ushort v = _mem.Read16(SS, SP);
        SP += 2;
        return v;
    }

    #endregion

    #region ModR/M

    private int _mod, _reg, _rm;
    private ushort _eaSeg, _eaOff;

    private void DecodeModRm()
    {
        byte b = Fetch8();
        _mod = b >> 6;
        _reg = (b >> 3) & 7;
        _rm = b & 7;
        if (_mod == 3) return;

        int defSeg = SegDS;
        int ea;
        switch (_rm)
        {
            case 0: ea = BX + SI; break;
            case 1: ea = BX + DI; break;
            case 2: ea = BP + SI; defSeg = SegSS; break;
            case 3: ea = BP + DI; defSeg = SegSS; break;
            case 4: ea = SI; break;
            case 5: ea = DI; break;
            case 6:
                if (_mod == 0) { ea = Fetch16(); }
                else { ea = BP; defSeg = SegSS; }
                break;
            default: ea = BX; break;
        }
        if (_mod == 1) ea += (sbyte)Fetch8();
        else if (_mod == 2) ea += Fetch16();
        _eaOff = (ushort)ea;
        _eaSeg = DataSeg(defSeg);
    }

    private int ReadRm(bool w)
    {
        if (_mod == 3) return w ? _r[_rm] : GetReg8(_rm);
        return w ? _mem.Read16(_eaSeg, _eaOff) : _mem.Read8(_eaSeg, _eaOff);
    }

    private void WriteRm(bool w, int value)
    {
        if (_mod == 3)
        {
            if (w) _r[_rm] = (ushort)value; else SetReg8(_rm, value);
            return;
        }
        if (w) _mem.Write16(_eaSeg, _eaOff, (ushort)value);
        else _mem.Write8(_eaSeg, _eaOff, (byte)value);
    }

    private int ReadReg(bool w) => w ? _r[_reg] : GetReg8(_reg);

    private void WriteReg(bool w, int value)
    {
        if (w) _r[_reg] = (ushort)value; else SetReg8(_reg, value);
    }

    #endregion

    #region Interrupts

    /// <summary>Real-mode interrupt dispatch through the vector table.</summary>
    public void Interrupt(int vector)
    {
        Push(Flags);
        SetFlag(CpuFlags.IF, false);
        SetFlag(CpuFlags.TF, false);
        Push(CS);
        Push(IP);
        IP = _mem.Read16(vector * 4);
        CS = _mem.Read16(vector * 4 + 2);
    }

    private bool IsNativeVector(int vector) =>
        Interrupts != null
        && _mem.Read16(vector * 4 + 2) == BiosSegment
        && _mem.Read16(vector * 4) == vector;

    private StepResult SoftwareInterrupt(int vector)
    {
        if (IsNativeVector(vector))
        {
            switch (Interrupts!.Handle(vector, this))
            {
                case InterruptResult.Handled:
                    return StepResult.Ok;
                case InterruptResult.Wait:
                    CS = InstructionCS;
                    IP = InstructionIP;
                    return StepResult.Waiting;
                case InterruptResult.Halt:
                    Halted = true;
                    return StepResult.Halted;
            }
        }
        Interrupt(vector);
        return StepResult.Ok;
    }

    /// <summary>Execution reached a BIOS stub (e.g. via a chained far call); run the native service first.</summary>
    private StepResult RunNativeStub(int vector)
    {
        ushort flagsBefore = Flags;
        var result = Interrupts!.Handle(vector, this);
        if (result == InterruptResult.Wait) return StepResult.Waiting;
        if (result == InterruptResult.Halt)
        {
            Halted = true;
            return StepResult.Halted;
        }
        if (result == InterruptResult.Handled && Flags != flagsBefore)
        {
            // Propagate status flags to the caller's saved FLAGS so IRET keeps them.
            const ushort status = (ushort)(CpuFlags.CF | CpuFlags.PF | CpuFlags.AF | CpuFlags.ZF | CpuFlags.SF | CpuFlags.OF);
            ushort saved = _mem.Read16(SS, (ushort)(SP + 4));
            _mem.Write16(SS, (ushort)(SP + 4), (ushort)((saved & ~status) | (Flags & status)));
        }
        return StepResult.Ok;
    }

    #endregion

    /// <summary>Executes one instruction (a REP-prefixed string instruction runs to completion).</summary>
    public StepResult Step()
    {
        if (Halted) return StepResult.Halted;

        InstructionCS = CS;
        InstructionIP = IP;

        if (CS == BiosSegment && IP < NativeVectorCount && Interrupts != null)
        {
            var native = RunNativeStub(IP);
            if (native != StepResult.Ok) return native;
        }

        bool trap = GetFlag(CpuFlags.TF);
        _segOverride = -1;
        _repPrefix = 0;

        var result = ExecuteInstruction();
        if (result != StepResult.Waiting) InstructionCount++;

        if (trap && result == StepResult.Ok && !Halted)
            Interrupt(1);
        return result;
    }
}
