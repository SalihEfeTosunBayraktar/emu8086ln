using Emu8086.Core.Cpu;
using Emu8086.Core.Disassembler;

namespace Emu8086.Tests;

public class DisassemblerTests
{
    /// <summary>Assemble -> disassemble -> assemble again must give identical bytes.</summary>
    [Theory]
    [InlineData("mov ax, 1234h")]
    [InlineData("mov al, [bx+si]")]
    [InlineData("mov [bp+2], ax")]
    [InlineData("mov word ptr [1000h], 5")]
    [InlineData("mov cx, [bx+si-2]")]
    [InlineData("mov dx, [di+300h]")]
    [InlineData("add bx, 1234h")]
    [InlineData("cmp byte ptr [si], 10")]
    [InlineData("lea si, [bx+di+10h]")]
    [InlineData("shr bl, cl")]
    [InlineData("rol ax, 4")]
    [InlineData("out 4, ax")]
    [InlineData("xchg al, ah")]
    [InlineData("push word ptr [bx]")]
    [InlineData("idiv word ptr [si]")]
    [InlineData("inc byte ptr [si]")]
    [InlineData("int 21h")]
    [InlineData("rep movsb")]
    [InlineData("jmp bx")]
    [InlineData("mov es:[di], al")]
    [InlineData("mov ds, ax")]
    [InlineData("imul ax, bx, 10")]
    [InlineData("test al, 1")]
    [InlineData("in al, dx")]
    public void RoundTrips(string source)
    {
        byte[] original = TestHost.Bytes(source);
        var memory = new Memory();
        memory.Load(Memory.Physical(0x700, 0x100), original);
        var ins = new Disassembler8086(memory).Decode(0x700, 0x100);
        Assert.Equal(original.Length, ins.Bytes.Length);
        byte[] again = TestHost.Bytes(ins.Text);
        Assert.Equal(original, again);
    }
}
