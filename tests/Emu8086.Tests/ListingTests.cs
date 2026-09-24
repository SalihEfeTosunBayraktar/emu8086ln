using Emu8086.Core.Assembler;

namespace Emu8086.Tests;

public class ListingTests
{
    [Fact]
    public void ListingShowsAddressesBytesAndMacroExpansion()
    {
        const string source = "org 100h\nshow macro c\n  mov dl, c\n  mov ah, 2\n  int 21h\nendm\nshow 'A'\nret\nmsg db 'hi'";
        var result = TestHost.AssembleOk(source);
        string listing = ListingWriter.Listing(result, source, "test.asm");

        Assert.Contains("CODE:0100", listing);
        Assert.Contains("B2 41", listing);          // mov dl, 'A' from the macro expansion
        Assert.Contains("mov dl, 'A'", listing);   // expanded text is shown
        Assert.Contains("C3", listing);             // ret
        Assert.Contains("68 69", listing);          // 'hi'
        Assert.Contains("MSG", listing.ToUpperInvariant());
    }

    [Fact]
    public void SymbolTableListsVariablesAndLabels()
    {
        var result = TestHost.AssembleOk("org 100h\nstart: jmp start\ncount dw 5\nlimit equ 10");
        string symbols = ListingWriter.Symbols(result);
        Assert.Contains("start", symbols);
        Assert.Contains("VARIABLE", symbols);
        Assert.Contains("CONSTANT", symbols);
        Assert.Contains("CODE:0102", symbols);
    }
}
