namespace Emu8086.Tests;

public class EncodingTests
{
    [Theory]
    [InlineData("mov ax, 1234h", "B8 34 12")]
    [InlineData("mov al, [bx+si]", "8A 00")]
    [InlineData("mov [bp+2], ax", "89 46 02")]
    [InlineData("mov word ptr [1000h], 5", "C7 06 00 10 05 00")]
    [InlineData("mov byte ptr [bx], 0FFh", "C6 07 FF")]
    [InlineData("mov es:[di], al", "26 88 05")]
    [InlineData("mov ds, ax", "8E D8")]
    [InlineData("mov ax, ds", "8C D8")]
    [InlineData("mov al, [1234h]", "A0 34 12")]
    [InlineData("mov [bp], al", "88 46 00")]
    [InlineData("mov cx, [bx+si-2]", "8B 48 FE")]
    [InlineData("mov dx, [di+300h]", "8B 95 00 03")]
    [InlineData("add ax, 5", "05 05 00")]
    [InlineData("add bx, 5", "83 C3 05")]
    [InlineData("add bx, 1234h", "81 C3 34 12")]
    [InlineData("sub al, bl", "28 D8")]
    [InlineData("cmp byte ptr [si], 10", "80 3C 0A")]
    [InlineData("xor ax, ax", "31 C0")]
    [InlineData("lea si, [bx+di+10h]", "8D 71 10")]
    [InlineData("shl ax, 1", "D1 E0")]
    [InlineData("shr bl, cl", "D2 EB")]
    [InlineData("rol ax, 4", "C1 C0 04")]
    [InlineData("in al, dx", "EC")]
    [InlineData("out 4, ax", "E7 04")]
    [InlineData("in al, 60h", "E4 60")]
    [InlineData("xchg ax, bx", "93")]
    [InlineData("xchg al, ah", "86 E0")]
    [InlineData("push cs", "0E")]
    [InlineData("pop ds", "1F")]
    [InlineData("push word ptr [bx]", "FF 37")]
    [InlineData("test al, 1", "A8 01")]
    [InlineData("test bx, cx", "85 CB")]
    [InlineData("mul bl", "F6 E3")]
    [InlineData("idiv word ptr [si]", "F7 3C")]
    [InlineData("inc byte ptr [si]", "FE 04")]
    [InlineData("inc cx", "41")]
    [InlineData("dec di", "4F")]
    [InlineData("neg ax", "F7 D8")]
    [InlineData("int 21h", "CD 21")]
    [InlineData("int 3", "CC")]
    [InlineData("rep movsb", "F3 A4")]
    [InlineData("repne scasb", "F2 AE")]
    [InlineData("jmp bx", "FF E3")]
    [InlineData("call word ptr [bx]", "FF 17")]
    [InlineData("ret", "C3")]
    [InlineData("ret 4", "C2 04 00")]
    [InlineData("retf", "CB")]
    [InlineData("aam", "D4 0A")]
    [InlineData("pusha", "60")]
    [InlineData("push 5", "6A 05")]
    [InlineData("imul ax, bx, 10", "6B C3 0A")]
    [InlineData("les di, [bx]", "C4 3F")]
    [InlineData("mov al, 'A'", "B0 41")]
    [InlineData("mov ax, -1", "B8 FF FF")]
    [InlineData("mov bx, 10b + 0Ah", "BB 0C 00")]
    public void EncodesInstruction(string source, string expectedHex)
    {
        Assert.Equal(expectedHex, Hex(TestHost.Bytes(source)));
    }

    private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));

    [Fact]
    public void ShortAndNearJumps()
    {
        Assert.Equal("EB FE", Hex(TestHost.Bytes("l: jmp l")));
        Assert.Equal("EB 00", Hex(TestHost.Bytes("jmp next\nnext:")));
        Assert.Equal("74 FE", Hex(TestHost.Bytes("l: je l")));
        Assert.Equal("E2 FE", Hex(TestHost.Bytes("l: loop l")));
        Assert.Equal("E8 00 00", Hex(TestHost.Bytes("call p\np:")));
    }

    [Fact]
    public void ConditionalJumpOutOfRangeIsExpanded()
    {
        var bytes = TestHost.Bytes("je far_away\ndb 200 dup(90h)\nfar_away: nop");
        // JNE +3 ; JMP near far_away
        Assert.Equal(0x75, bytes[0]);
        Assert.Equal(0x03, bytes[1]);
        Assert.Equal(0xE9, bytes[2]);
        Assert.Equal(200, bytes[3] | (bytes[4] << 8));
    }

    [Fact]
    public void DataDirectives()
    {
        var bytes = TestHost.Bytes("db 'Hi', 13, 10, '$'\ndw 1234h, 2 dup(0AAh)\ndd 12345678h\ndb 3 dup(?)");
        Assert.Equal("48 69 0D 0A 24 34 12 AA 00 AA 00 78 56 34 12 00 00 00", Hex(bytes));
    }

    [Fact]
    public void VariablesHaveTypesAndOffsets()
    {
        var bytes = TestHost.Bytes("mov al, v1\nmov v2, 5\nmov ax, offset v2\nret\nv1 db 7\nv2 dw ?");
        // mov al,[v1] ; mov word ptr [v2],5 ; mov ax, offset v2 ; ret
        Assert.Equal("A0 0D 01 C7 06 0E 01 05 00 B8 0E 01 C3 07 00 00", Hex(bytes));
    }

    [Fact]
    public void ReportsErrorsWithLineNumbers()
    {
        var r = TestHost.Assemble("org 100h\nmov ax, bl\nfoo bar\nmov [bx], 5\njmp nowhere");
        Assert.False(r.Success);
        var codes = r.Diagnostics.Select(d => (d.Line, d.Code)).ToList();
        Assert.Contains((2, Emu8086.Core.Assembler.AsmErrorCode.SizeMismatch), codes);
        Assert.Contains((3, Emu8086.Core.Assembler.AsmErrorCode.UnknownInstruction), codes);
        Assert.Contains((4, Emu8086.Core.Assembler.AsmErrorCode.SizeUnknown), codes);
        Assert.Contains((5, Emu8086.Core.Assembler.AsmErrorCode.UndefinedSymbol), codes);
    }

    [Fact]
    public void ExeWithSegmentsHasRelocation()
    {
        var r = TestHost.AssembleOk("""
            data segment
              msg db 'x$'
            ends
            stack segment
              dw 64 dup(0)
            ends
            code segment
            start:
              mov ax, data
              mov ds, ax
              mov ax, 4c00h
              int 21h
            ends
            end start
            """);
        Assert.Equal(Emu8086.Core.Assembler.OutputFormat.Exe, r.Format);
        var image = Emu8086.Core.Machine.ProgramImage.Link(r);
        Assert.Single(image.Relocations);
        var file = image.ToFileBytes();
        Assert.Equal((byte)'M', file[0]);
        Assert.Equal((byte)'Z', file[1]);
    }
}
