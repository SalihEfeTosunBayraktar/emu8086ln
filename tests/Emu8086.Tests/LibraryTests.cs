using Emu8086.Core.Machine;

namespace Emu8086.Tests;

public class LibraryTests
{
    private static readonly string LibraryPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Emu8086.App", "Library", "emu8086.inc");

    private static string? Include(string name) =>
        name.Equals("emu8086.inc", StringComparison.OrdinalIgnoreCase) ? File.ReadAllText(LibraryPath) : null;

    [Fact]
    public void PrintScanAndPrintNumbers()
    {
        var m = TestHost.Run("""
            include 'emu8086.inc'
            org 100h
            PRINT 'Enter: '
            CALL SCAN_NUM
            MOV AX, CX
            PRINTN ''
            CALL PRINT_NUM
            PUTC ' '
            MOV AX, 65535
            CALL PRINT_NUM_UNS
            GOTOXY 10, 5
            CALL PTHIS
            DB 'done', 0
            RET
            DEFINE_SCAN_NUM
            DEFINE_PRINT_NUM
            DEFINE_PRINT_NUM_UNS
            DEFINE_PTHIS
            END
            """, keys: "-1234\n", includes: Include);
        Assert.Equal(StopReason.Terminated, m.StopReason);
        var lines = m.Video.ReadText().Split('\n');
        Assert.Equal("Enter: -1234", lines[0]);
        Assert.Equal("-1234 65535", lines[1]);
        Assert.Equal("          done", lines[5]);
    }

    [Fact]
    public void GetStringAndPrintString()
    {
        var m = TestHost.Run("""
            include emu8086.inc
            org 100h
            LEA DI, buffer
            MOV DX, 10
            CALL GET_STRING
            CALL CLEAR_SCREEN
            LEA SI, buffer
            CALL PRINT_STRING
            RET
            buffer DB 10 DUP(?)
            DEFINE_GET_STRING
            DEFINE_PRINT_STRING
            DEFINE_CLEAR_SCREEN
            """, keys: "abx\bc\n", includes: Include);
        Assert.Equal("abc", m.Video.ReadText());
    }
}
