using Emu8086.Core.Analysis;
using Emu8086.Core.Machine;

namespace Emu8086.Tests;

public class StepAnalyzerTests
{
    /// <summary>Runs all instructions before the last one, then analyzes the last one.</summary>
    private static StepAnalysis AnalyzeLast(string setup, string instruction)
    {
        var result = TestHost.AssembleOk($"org 100h\n{setup}\n{instruction}\nhlt\ndata dw 1234h, 5678h");
        var m = new Machine(Path.GetTempPath());
        m.Load(ProgramImage.Link(result));
        int setupLines = setup.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        for (int i = 0; i <= setupLines; i++) m.Step();
        return StepAnalyzer.Analyze(m.History.Last!, m.Memory);
    }

    [Fact]
    public void MemoryToRegisterMove()
    {
        var a = AnalyzeLast("mov bx, offset data\nmov si, 2", "mov ax, [bx+si]");
        Assert.Equal("MOV", a.Mnemonic);
        Assert.Contains("BX", a.RegistersRead);
        Assert.Contains("SI", a.RegistersRead);
        Assert.DoesNotContain("AX", a.RegistersRead);
        var ax = Assert.Single(a.RegistersWritten);
        Assert.Equal(("AX", (ushort)0x5678), (ax.Name, ax.After));
        Assert.NotNull(a.Address);
        Assert.Equal("DS", a.Address!.Segment);
        Assert.Equal(a.Address.PhysicalAddress, a.MemoryReads[0].Address);
        Assert.Equal(0x78, a.MemoryReads[0].Value);
        Assert.Null(a.Alu);
    }

    [Fact]
    public void AluAddition()
    {
        var a = AnalyzeLast("mov ax, 5\nmov bx, 3", "add ax, bx");
        Assert.NotNull(a.Alu);
        Assert.Equal(5, a.Alu!.OperandA);
        Assert.Equal(3, a.Alu.OperandB);
        Assert.Equal(8, a.Alu.Result);
        Assert.Contains("AX", a.RegistersRead);
    }

    [Fact]
    public void CompareChangesOnlyFlags()
    {
        var a = AnalyzeLast("mov al, 7", "cmp al, 7");
        Assert.Empty(a.RegistersWritten.Where(r => r.Name != "IP"));
        Assert.Contains(a.FlagsChanged, f => f.Name == "ZF" && f.After);
    }

    [Fact]
    public void StoreAndJumpAndPort()
    {
        var store = AnalyzeLast("mov ax, 0ABCDh", "mov [data], ax");
        Assert.Equal(2, store.MemoryWrites.Count);
        Assert.Equal(0xCD, store.MemoryWrites[0].Value);
        Assert.Equal(0x34, store.MemoryWrites[0].OldValue);

        var jump = AnalyzeLast("", "jmp $");
        Assert.True(jump.Jumped);

        var port = AnalyzeLast("mov al, 5", "out 4, al");
        Assert.True(Assert.Single(port.Ports).IsWrite);
    }

    [Fact]
    public void StackPush()
    {
        var a = AnalyzeLast("mov ax, 1234h", "push ax");
        Assert.Contains(a.RegistersWritten, r => r.Name == "SP" && r.After == 0xFFFC);
        Assert.Equal(2, a.MemoryWrites.Count);
    }
}
