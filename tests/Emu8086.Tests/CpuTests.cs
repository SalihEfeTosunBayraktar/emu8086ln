using Emu8086.Core.Cpu;

namespace Emu8086.Tests;

public class CpuTests
{
    private static Cpu8086 Exec(string code)
    {
        var m = TestHost.Run("org 100h\n" + code + "\nhlt");
        return m.Cpu;
    }

    [Fact]
    public void AddSetsOverflowAndSign()
    {
        var cpu = Exec("mov al, 7Fh\nadd al, 1");
        Assert.Equal(0x80, cpu.AL);
        Assert.True(cpu.GetFlag(CpuFlags.OF));
        Assert.True(cpu.GetFlag(CpuFlags.SF));
        Assert.False(cpu.GetFlag(CpuFlags.CF));
        Assert.True(cpu.GetFlag(CpuFlags.AF));
    }

    [Fact]
    public void SubBorrowSetsCarry()
    {
        var cpu = Exec("mov ax, 1\nsub ax, 2");
        Assert.Equal(0xFFFF, cpu.AX);
        Assert.True(cpu.GetFlag(CpuFlags.CF));
        Assert.False(cpu.GetFlag(CpuFlags.OF));
    }

    [Fact]
    public void ParityFlag()
    {
        Assert.True(Exec("mov al, 3\nor al, 0").GetFlag(CpuFlags.PF));
        Assert.False(Exec("mov al, 1\nor al, 0").GetFlag(CpuFlags.PF));
    }

    [Fact]
    public void IncPreservesCarry()
    {
        var cpu = Exec("stc\nmov al, 0FFh\ninc al");
        Assert.True(cpu.GetFlag(CpuFlags.CF));
        Assert.True(cpu.GetFlag(CpuFlags.ZF));
    }

    [Fact]
    public void Daa()
    {
        var cpu = Exec("mov al, 38h\nadd al, 45h\ndaa");
        Assert.Equal(0x83, cpu.AL);
    }

    [Fact]
    public void AaaAndAam()
    {
        Assert.Equal(0x0105, Exec("mov ax, 9\nadd al, 6\naaa").AX);
        Assert.Equal(0x0703, Exec("mov al, 73\naam").AX);
    }

    [Fact]
    public void SignedMultiplyAndDivide()
    {
        var cpu = Exec("mov ax, -7\nmov bx, 3\nimul bx\nmov cx, ax\nmov ax, -7\ncwd\nidiv bx");
        Assert.Equal(unchecked((ushort)-21), cpu.CX);
        Assert.Equal(unchecked((ushort)-2), cpu.AX);
        Assert.Equal(unchecked((ushort)-1), cpu.DX);
    }

    [Fact]
    public void UnsignedDivide()
    {
        var cpu = Exec("mov dx, 1\nmov ax, 0\nmov bx, 10h\ndiv bx");
        Assert.Equal(0x1000, cpu.AX);
        Assert.Equal(0, cpu.DX);
    }

    [Fact]
    public void ShiftsAndRotates()
    {
        Assert.Equal(0x0F00, Exec("mov ax, 0F0h\nmov cl, 4\nshl ax, cl").AX);
        Assert.Equal(0xFF, Exec("mov al, 0FEh\nsar al, 1\nmov ah, 0").AL);
        var cpu = Exec("mov al, 81h\nrol al, 1");
        Assert.Equal(0x03, cpu.AL);
        Assert.True(cpu.GetFlag(CpuFlags.CF));
        Assert.Equal(0x40, Exec("clc\nmov al, 81h\nrcr al, 1").AL);
    }

    [Fact]
    public void ConditionalJumpsSignedVsUnsigned()
    {
        // -1 < 1 signed, but 0FFFFh > 1 unsigned
        var cpu = Exec("""
            mov bx, 0
            mov ax, -1
            cmp ax, 1
            jl signed_less
            jmp check
            signed_less: or bx, 1
            check:
            cmp ax, 1
            ja unsigned_above
            jmp done
            unsigned_above: or bx, 2
            done:
            """);
        Assert.Equal(3, cpu.BX);
    }

    [Fact]
    public void XlatAndLea()
    {
        var cpu = Exec("jmp go\ntable db 10, 20, 30\ngo:\nlea bx, table\nmov al, 2\nxlat");
        Assert.Equal(30, cpu.AL);
    }

    [Fact]
    public void PushaPopa()
    {
        var cpu = Exec("mov ax, 1\nmov bx, 2\npusha\nmov ax, 0\nmov bx, 0\npopa");
        Assert.Equal(1, cpu.AX);
        Assert.Equal(2, cpu.BX);
    }
}
