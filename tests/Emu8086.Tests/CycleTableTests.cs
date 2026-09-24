using Emu8086.Core.Analysis;
using Emu8086.Core.Disassembler;
using Emu8086.Core.Machine;

namespace Emu8086.Tests;

public class CycleTableTests
{
    private static int Cycles(string mnemonic, string operands, bool jumped = false, int repeats = 0) =>
        CycleTable.Estimate(new DisassembledInstruction(0, 0, [], mnemonic, operands), jumped, repeats);

    [Theory]
    [InlineData("MOV", "AX, BX", 2)]
    [InlineData("MOV", "AX, 0005h", 4)]
    [InlineData("MOV", "AX, [BX]", 13)]
    [InlineData("ADD", "WORD PTR [BX+SI+04h], AX", 28)]
    [InlineData("INC", "CX", 2)]
    [InlineData("INC", "CL", 3)]
    [InlineData("PUSH", "AX", 11)]
    [InlineData("MUL", "BL", 74)]
    [InlineData("NOP", "", 3)]
    public void KnownForms(string mnemonic, string operands, int expected) =>
        Assert.Equal(expected, Cycles(mnemonic, operands));

    [Fact]
    public void ConditionalJumpsDependOnTheOutcome()
    {
        Assert.Equal(16, Cycles("JNZ", "0105h", jumped: true));
        Assert.Equal(4, Cycles("JNZ", "0105h"));
        Assert.Equal(17, Cycles("LOOP", "0105h", jumped: true));
    }

    [Fact]
    public void RepeatedStringsScaleWithTheCount() => Assert.Equal(9 + 5 * 17, Cycles("REP MOVSB", "", repeats: 5));

    [Fact]
    public void MachineAccumulatesAndStepBackSubtracts()
    {
        var m = new Machine(Path.Combine(Path.GetTempPath(), "emu8086ln-tests")) { CountCycles = true };
        m.Load(ProgramImage.Link(TestHost.AssembleOk("org 100h\nmov ax, 1\nmov bx, ax\nhlt")));
        m.Step();
        m.Step();
        Assert.Equal(4 + 2, m.Cycles);
        m.StepBack();
        Assert.Equal(4, m.Cycles);
    }
}

public class ProgramComparerTests
{
    private static ProgramImage Image(string source) => ProgramImage.Link(TestHost.AssembleOk(source));

    [Fact]
    public void FindsTheFirstRegisterDifference()
    {
        var a = Image("org 100h\nmov ax, 1\nmov bx, 2\nhlt");
        var b = Image("org 100h\nmov ax, 1\nmov bx, 3\nhlt");
        var result = ProgramComparer.Compare(a, b, Path.Combine(Path.GetTempPath(), "emu8086ln-tests"));
        Assert.Equal(2, result.FirstDifference);
        Assert.Equal(["BX"], result.Steps[1].Differences);
        Assert.Equal(3, result.A.Instructions);
    }

    [Fact]
    public void ComparesCostAndOutput()
    {
        var slow = Image("org 100h\nmov cx, 3\nagain: loop again\nmov dl, 'x'\nmov ah, 2\nint 21h\nret");
        var fast = Image("org 100h\nxor cx, cx\nmov dl, 'x'\nmov ah, 2\nint 21h\nret");
        var result = ProgramComparer.Compare(slow, fast, Path.Combine(Path.GetTempPath(), "emu8086ln-tests"));
        Assert.True(result.SameScreen);
        Assert.True(result.A.Cycles > result.B.Cycles);
    }
}
