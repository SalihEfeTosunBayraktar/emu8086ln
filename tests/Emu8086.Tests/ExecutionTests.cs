using Emu8086.Core.Cpu;
using Emu8086.Core.Machine;

namespace Emu8086.Tests;

public class ExecutionTests
{
    [Fact]
    public void HelloWorldCom()
    {
        var m = TestHost.Run("""
            org 100h
            mov dx, offset msg
            mov ah, 9
            int 21h
            ret
            msg db 'Hello, World!$'
            """);
        Assert.Equal(StopReason.Terminated, m.StopReason);
        Assert.Equal("Hello, World!", m.Video.ReadText());
    }

    [Fact]
    public void Emu8086ExeTemplate()
    {
        var m = TestHost.Run("""
            data segment
                pkey db "press any key...$"
            ends
            stack segment
                dw   128  dup(0)
            ends
            code segment
            start:
                mov ax, data
                mov ds, ax
                mov es, ax
                lea dx, pkey
                mov ah, 9
                int 21h
                mov ah, 1
                int 21h
                mov ax, 4c00h
                int 21h
            ends
            end start
            """, keys: "x");
        Assert.Equal(StopReason.Terminated, m.StopReason);
        Assert.Equal("press any key...x", m.Video.ReadText());
    }

    [Fact]
    public void SimplifiedSegments()
    {
        var m = TestHost.Run("""
            .model small
            .stack 100h
            .data
            msg db 'ok$'
            .code
            main proc
                mov ax, @data
                mov ds, ax
                mov dx, offset msg
                mov ah, 9
                int 21h
                mov ax, 4C05h
                int 21h
            main endp
            end main
            """);
        Assert.Equal("ok", m.Video.ReadText());
        Assert.Equal(5, m.ExitCode);
    }

    [Fact]
    public void FactorialLoop()
    {
        var m = TestHost.Run("""
            org 100h
            mov cx, 5
            mov ax, 1
            again:
            mul cx
            loop again
            hlt
            """);
        Assert.Equal(120, m.Cpu.AX);
        Assert.Equal(StopReason.HaltInstruction, m.StopReason);
    }

    [Fact]
    public void BufferedInputReadsLine()
    {
        var m = TestHost.Run("""
            org 100h
            mov dx, offset buf
            mov ah, 0Ah
            int 21h
            mov bl, buf[1]
            mov bh, 0
            mov al, buf[2]
            hlt
            buf db 10, ?, 10 dup(0)
            """, keys: "abc\n");
        Assert.Equal(3, m.Cpu.BL);
        Assert.Equal((byte)'a', m.Cpu.AL);
        Assert.Equal("abc", m.Video.ReadText());
    }

    [Fact]
    public void WaitsForKeyAndResumes()
    {
        var m = TestHost.Run("org 100h\nmov ah, 0\nint 16h\nhlt");
        Assert.False(m.IsStopped);
        m.Keyboard.Push((byte)'k', 0x25);
        for (int i = 0; i < 10 && !m.IsStopped; i++) m.Step();
        Assert.Equal(0x256B, m.Cpu.AX);
    }

    [Fact]
    public void ProceduresAndStack()
    {
        var m = TestHost.Run("""
            org 100h
            mov ax, 3
            push ax
            call square
            add sp, 2
            hlt
            square proc
                push bp
                mov bp, sp
                mov ax, [bp+4]
                imul ax
                pop bp
                ret
            square endp
            """);
        Assert.Equal(9, m.Cpu.AX);
        Assert.Equal(0xFFFE, m.Cpu.SP);
    }

    [Fact]
    public void StringInstructions()
    {
        var m = TestHost.Run("""
            org 100h
            cld
            mov si, offset src
            mov di, offset dst
            mov cx, 5
            rep movsb
            mov si, offset src
            mov di, offset dst
            mov cx, 5
            repe cmpsb
            hlt
            src db 'HELLO'
            dst db 5 dup(0)
            """);
        Assert.Equal(0, m.Cpu.CX);
        Assert.True(m.Cpu.GetFlag(CpuFlags.ZF));
    }

    [Fact]
    public void DivideByZeroStops()
    {
        var m = TestHost.Run("org 100h\nmov bl, 0\ndiv bl\nhlt");
        Assert.Equal(StopReason.DivideError, m.StopReason);
    }

    [Fact]
    public void UserInterruptHandler()
    {
        var m = TestHost.Run("""
            org 100h
            mov ax, 0
            mov es, ax
            mov word ptr es:[60h*4], offset handler
            mov es:[60h*4+2], cs
            int 60h
            hlt
            handler:
            mov bx, 1234h
            iret
            """);
        Assert.Equal(0x1234, m.Cpu.BX);
        Assert.Equal(StopReason.HaltInstruction, m.StopReason);
    }

    [Fact]
    public void StepBackRestoresRegistersAndMemory()
    {
        var result = TestHost.AssembleOk("org 100h\nmov ax, 5\nmov v, ax\nhlt\nv dw 1");
        var m = new Machine(Path.GetTempPath());
        m.Load(ProgramImage.Link(result));
        m.Step();
        m.Step();
        int address = Memory.Physical(m.Cpu.DS, 0x107);
        Assert.Equal(5, m.Memory.Read16(address));
        Assert.True(m.StepBack());
        Assert.Equal(1, m.Memory.Read16(address));
        Assert.Equal(0x103, m.Cpu.IP);
        Assert.True(m.StepBack());
        Assert.Equal(0, m.Cpu.AX);
    }

    [Fact]
    public void MacrosWithLocalLabels()
    {
        var m = TestHost.Run("""
            org 100h
            addn macro reg, n
                local lp
                mov cx, n
            lp: inc reg
                loop lp
            endm
            xor ax, ax
            addn ax, 3
            addn ax, 4
            hlt
            """);
        Assert.Equal(7, m.Cpu.AX);
    }

    [Fact]
    public void VideoTeletypeAndCursor()
    {
        var m = TestHost.Run("""
            org 100h
            mov ah, 2
            mov dh, 2
            mov dl, 5
            mov bh, 0
            int 10h
            mov ah, 0Eh
            mov al, 'Z'
            int 10h
            hlt
            """);
        Assert.Equal((byte)'Z', m.Video.ReadCell(5, 2).Char);
        Assert.Equal(6, m.Video.CursorX);
    }

    [Fact]
    public void PortsLatchValues()
    {
        var m = TestHost.Run("org 100h\nmov al, 42\nout 30, al\nmov al, 0\nin al, 30\nhlt");
        Assert.Equal(42, m.Cpu.AL);
    }
}

public class BinaryLoadingTests
{
    private static Machine RunImage(ProgramImage image)
    {
        var m = new Machine(Path.Combine(Path.GetTempPath(), "emu8086ln-tests"));
        m.Load(image);
        for (int i = 0; i < 100000 && !m.IsStopped; i++) m.Step();
        return m;
    }

    [Fact]
    public void ComFileRoundTrip()
    {
        var image = ProgramImage.Link(TestHost.AssembleOk("org 100h\nmov dx, offset m\nmov ah, 9\nint 21h\nret\nm db 'COM!$'"));
        var loaded = ProgramImage.FromFile(image.ToFileBytes(), ".com");
        Assert.Equal("COM!", RunImage(loaded).Video.ReadText());
    }

    [Fact]
    public void ExeFileRoundTripAppliesRelocations()
    {
        var image = ProgramImage.Link(TestHost.AssembleOk("""
            data segment
              m db 'EXE!$'
            ends
            stack segment
              dw 64 dup(0)
            ends
            code segment
            start:
              mov ax, data
              mov ds, ax
              mov dx, offset m
              mov ah, 9
              int 21h
              mov ax, 4C00h
              int 21h
            ends
            end start
            """));
        var loaded = ProgramImage.FromFile(image.ToFileBytes(), ".exe");
        Assert.Equal(image.Relocations, loaded.Relocations);
        Assert.Equal("EXE!", RunImage(loaded).Video.ReadText());
    }
}
