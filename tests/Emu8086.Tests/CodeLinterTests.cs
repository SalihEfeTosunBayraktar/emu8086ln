using Emu8086.Core.Analysis;

namespace Emu8086.Tests;

public class CodeLinterTests
{
    private static List<string> Codes(string source) =>
        CodeLinter.Analyze(source, "test.asm").Select(d => $"{d.Code}@{d.Line}").ToList();

    [Fact]
    public void CodeAfterJumpWithoutLabelIsUnreachable()
    {
        Assert.Equal(["UnreachableCode@3"], Codes("org 100h\njmp done\nmov ax, 1\ndone: hlt"));
    }

    [Fact]
    public void LabelsDataAndMacroCallsAfterRetAreFine()
    {
        Assert.Empty(Codes("org 100h\nret\nmsg db 'x$'\nDEFINE_PRINT_STRING\nnext:\n  mov ax, 1\n  ret"));
    }

    [Fact]
    public void UnbalancedProcedureIsReported()
    {
        const string source = "f proc\n  push ax\n  push bx\n  pop bx\n  ret\nf endp";
        Assert.Equal(["PushPopMismatch@1"], Codes(source));
    }

    [Fact]
    public void ProceduresWithSeveralReturnsAreNotGuessed()
    {
        Assert.Empty(Codes("f proc\n  push ax\n  jz a\n  pop ax\n  ret\na: pop ax\n  ret\nf endp"));
    }

    [Fact]
    public void JumpToANumberIsReported()
    {
        Assert.Equal(["JumpToNumber@2"], Codes("org 100h\njz 105h\nloop again\nagain: jmp 0FFFFh:0000h"));
    }

    [Fact]
    public void MacroBodiesAreSkipped()
    {
        Assert.Empty(Codes("m macro\n  ret\n  mov ax, 1\nendm\nret"));
    }
}
