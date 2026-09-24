namespace Emu8086.Core.Cpu;

public sealed partial class Cpu8086
{
    private static readonly bool[] EvenParity = BuildParityTable();

    private static bool[] BuildParityTable()
    {
        var table = new bool[256];
        for (int i = 0; i < 256; i++)
            table[i] = (System.Numerics.BitOperations.PopCount((uint)i) & 1) == 0;
        return table;
    }

    private static int Mask(bool w) => w ? 0xFFFF : 0xFF;
    private static int SignBit(bool w) => w ? 0x8000 : 0x80;

    private void SetSzp(int result, bool w)
    {
        ZF = (result & Mask(w)) == 0;
        SF = (result & SignBit(w)) != 0;
        PF = EvenParity[result & 0xFF];
    }

    private int Add(int a, int b, int carry, bool w)
    {
        int r = a + b + carry;
        CF = r > Mask(w);
        AF = ((a ^ b ^ r) & 0x10) != 0;
        OF = ((r ^ a) & (r ^ b) & SignBit(w)) != 0;
        SetSzp(r, w);
        return r & Mask(w);
    }

    private int Sub(int a, int b, int borrow, bool w)
    {
        int r = a - b - borrow;
        CF = r < 0;
        AF = ((a ^ b ^ r) & 0x10) != 0;
        OF = ((a ^ b) & (a ^ r) & SignBit(w)) != 0;
        SetSzp(r, w);
        return r & Mask(w);
    }

    private int Logic(int r, bool w)
    {
        CF = false;
        OF = false;
        AF = false;
        SetSzp(r, w);
        return r & Mask(w);
    }

    /// <summary>ALU operations in opcode order: ADD OR ADC SBB AND SUB XOR CMP.</summary>
    private int Alu(int op, int a, int b, bool w) => op switch
    {
        0 => Add(a, b, 0, w),
        1 => Logic(a | b, w),
        2 => Add(a, b, CF ? 1 : 0, w),
        3 => Sub(a, b, CF ? 1 : 0, w),
        4 => Logic(a & b, w),
        5 => Sub(a, b, 0, w),
        6 => Logic(a ^ b, w),
        _ => Sub(a, b, 0, w),
    };

    private int Inc(int v, bool w)
    {
        bool carry = CF;
        int r = Add(v, 1, 0, w);
        CF = carry;
        return r;
    }

    private int Dec(int v, bool w)
    {
        bool carry = CF;
        int r = Sub(v, 1, 0, w);
        CF = carry;
        return r;
    }

    /// <summary>Shift/rotate group: ROL ROR RCL RCR SHL SHR SAL SAR.</summary>
    private int Shift(int op, int v, int count, bool w)
    {
        count &= 0x1F;
        if (count == 0) return v;
        int mask = Mask(w), sign = SignBit(w), bits = w ? 16 : 8;

        switch (op)
        {
            case 0: // ROL
                for (int i = 0; i < count; i++)
                {
                    bool c = (v & sign) != 0;
                    v = ((v << 1) | (c ? 1 : 0)) & mask;
                    CF = c;
                }
                OF = ((v & sign) != 0) ^ CF;
                return v;
            case 1: // ROR
                for (int i = 0; i < count; i++)
                {
                    bool c = (v & 1) != 0;
                    v = (v >> 1) | (c ? sign : 0);
                    CF = c;
                }
                OF = (((v << 1) ^ v) & sign) != 0;
                return v;
            case 2: // RCL
                for (int i = 0; i < count; i++)
                {
                    bool c = (v & sign) != 0;
                    v = ((v << 1) | (CF ? 1 : 0)) & mask;
                    CF = c;
                }
                OF = ((v & sign) != 0) ^ CF;
                return v;
            case 3: // RCR
                for (int i = 0; i < count; i++)
                {
                    bool c = (v & 1) != 0;
                    v = (v >> 1) | (CF ? sign : 0);
                    CF = c;
                }
                OF = (((v << 1) ^ v) & sign) != 0;
                return v;
            case 4:
            case 6: // SHL / SAL
            {
                long wide = (long)v << count;
                CF = count <= bits && (wide & ((long)sign << 1)) != 0;
                int r = (int)(wide & mask);
                OF = ((r & sign) != 0) ^ CF;
                AF = false;
                SetSzp(r, w);
                return r;
            }
            case 5: // SHR
            {
                CF = count <= bits && ((v >> (count - 1)) & 1) != 0;
                OF = (v & sign) != 0;
                int r = count >= bits ? 0 : v >> count;
                AF = false;
                SetSzp(r, w);
                return r;
            }
            default: // SAR
            {
                int sv = w ? (short)v : (sbyte)v;
                int n = Math.Min(count, bits);
                CF = ((sv >> (n - 1)) & 1) != 0;
                int r = (sv >> n) & mask;
                OF = false;
                AF = false;
                SetSzp(r, w);
                return r;
            }
        }
    }

    /// <summary>Returns false on divide overflow (caller raises INT 0).</summary>
    private bool Divide(int divisor, bool w, bool signed)
    {
        if (divisor == 0) return false;
        if (!w)
        {
            if (signed)
            {
                int dividend = (short)AX;
                int d = (sbyte)divisor;
                int q = dividend / d;
                if (q is > 127 or < -128) return false;
                AL = (byte)q;
                AH = (byte)(dividend % d);
            }
            else
            {
                int q = AX / divisor;
                if (q > 0xFF) return false;
                AH = (byte)(AX % divisor);
                AL = (byte)q;
            }
            return true;
        }

        if (signed)
        {
            int dividend = (DX << 16) | AX;
            int d = (short)divisor;
            long q = (long)dividend / d;
            if (q is > 32767 or < -32768) return false;
            AX = (ushort)q;
            DX = (ushort)(dividend % d);
        }
        else
        {
            uint dividend = ((uint)DX << 16) | AX;
            uint q = dividend / (uint)divisor;
            if (q > 0xFFFF) return false;
            DX = (ushort)(dividend % (uint)divisor);
            AX = (ushort)q;
        }
        return true;
    }

    private void Multiply(int v, bool w, bool signed)
    {
        if (!w)
        {
            if (signed)
            {
                AX = (ushort)((sbyte)AL * (sbyte)v);
                CF = OF = (short)AX != (sbyte)AL;
            }
            else
            {
                AX = (ushort)(AL * v);
                CF = OF = AH != 0;
            }
            SetSzp(AL, false);
            return;
        }

        if (signed)
        {
            int r = (short)AX * (short)v;
            AX = (ushort)r;
            DX = (ushort)(r >> 16);
            CF = OF = r != (short)r;
        }
        else
        {
            uint r = (uint)AX * (uint)v;
            AX = (ushort)r;
            DX = (ushort)(r >> 16);
            CF = OF = DX != 0;
        }
        SetSzp(AX, true);
    }

    private void Daa()
    {
        int oldAl = AL;
        bool oldCf = CF;
        CF = false;
        if ((AL & 0x0F) > 9 || AF)
        {
            AL = (byte)(AL + 6);
            CF = oldCf || oldAl + 6 > 0xFF;
            AF = true;
        }
        else AF = false;

        if (oldAl > 0x99 || oldCf)
        {
            AL = (byte)(AL + 0x60);
            CF = true;
        }
        else CF = false;
        SetSzp(AL, false);
    }

    private void Das()
    {
        int oldAl = AL;
        bool oldCf = CF;
        CF = false;
        if ((AL & 0x0F) > 9 || AF)
        {
            AL = (byte)(AL - 6);
            CF = oldCf || oldAl < 6;
            AF = true;
        }
        else AF = false;

        if (oldAl > 0x99 || oldCf)
        {
            AL = (byte)(AL - 0x60);
            CF = true;
        }
        SetSzp(AL, false);
    }

    private void Aaa()
    {
        if ((AL & 0x0F) > 9 || AF)
        {
            AX = (ushort)(AX + 0x106);
            AF = CF = true;
        }
        else AF = CF = false;
        AL &= 0x0F;
    }

    private void Aas()
    {
        if ((AL & 0x0F) > 9 || AF)
        {
            AL = (byte)(AL - 6);
            AH = (byte)(AH - 1);
            AF = CF = true;
        }
        else AF = CF = false;
        AL &= 0x0F;
    }

    private bool Condition(int cc) => cc switch
    {
        0x0 => OF,
        0x1 => !OF,
        0x2 => CF,
        0x3 => !CF,
        0x4 => ZF,
        0x5 => !ZF,
        0x6 => CF || ZF,
        0x7 => !CF && !ZF,
        0x8 => SF,
        0x9 => !SF,
        0xA => PF,
        0xB => !PF,
        0xC => SF != OF,
        0xD => SF == OF,
        0xE => ZF || SF != OF,
        _ => !ZF && SF == OF,
    };
}
