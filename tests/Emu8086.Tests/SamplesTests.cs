using Emu8086.Core.Machine;

namespace Emu8086.Tests;

/// <summary>Every bundled example and project template must assemble; the deterministic ones must also produce the right output.</summary>
public class SamplesTests
{
    private static readonly string AppDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Emu8086.App"));

    private static string? Include(string name)
    {
        string path = Path.Combine(AppDirectory, "Library", name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static IEnumerable<object[]> SourceFiles() =>
        Directory.GetFiles(Path.Combine(AppDirectory, "Examples"), "*.asm")
            .Concat(Directory.GetFiles(Path.Combine(AppDirectory, "config", "templates"), "*.asm", SearchOption.AllDirectories))
            .Select(f => new object[] { Path.GetRelativePath(AppDirectory, f) });

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void Assembles(string relativePath)
    {
        TestHost.AssembleOk(File.ReadAllText(Path.Combine(AppDirectory, relativePath)), Include);
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void HasNoLintWarnings(string relativePath)
    {
        var warnings = Emu8086.Core.Analysis.CodeLinter.Analyze(File.ReadAllText(Path.Combine(AppDirectory, relativePath)), relativePath);
        Assert.True(warnings.Count == 0, string.Join("\n", warnings.Select(w => $"{w.Line}: {w.Code} [{w.SourceText}]")));
    }

    private static Machine RunExample(string name, string keys = "") =>
        TestHost.Run(File.ReadAllText(Path.Combine(AppDirectory, "Examples", name)), keys, includes: Include);

    [Fact]
    public void Arithmetic()
    {
        var m = RunExample("02_arithmetic.asm");
        Assert.Equal("25 + 17 = 42\n10 - 35 = -25\n123 * 45 = 5535\n1000 / 7 = 142 remainder 6", m.Video.ReadText());
    }

    [Fact]
    public void Loops() => Assert.Equal("0123456789\n9876543210", RunExample("03_loops.asm").Video.ReadText());

    [Fact]
    public void StringReverse() => Assert.Equal("nuf si nl6808ume", RunExample("04_string_reverse.asm").Video.ReadText());

    [Fact]
    public void Factorial() => Assert.Equal("7! = 5040", RunExample("05_procedures_stack.asm").Video.ReadText());

    [Fact]
    public void KeyboardEcho() =>
        Assert.Equal("Type something (ESC to quit):\nHELLO", RunExample("06_keyboard_echo.asm", "hello\u001b").Video.ReadText());

    [Fact]
    public void BubbleSort() => Assert.Equal("1 3 7 21 42 58 77 99", RunExample("16_bubble_sort.asm").Video.ReadText());

    [Fact]
    public void ExeSegments() => Assert.Equal("Data lives in its own segment.", RunExample("17_exe_segments.asm").Video.ReadText());

    [Fact]
    public void InputNumbers() =>
        Assert.Equal("First number: 12\nSecond number: -30\nSum: -18", RunExample("18_input_numbers.asm", "12\n-30\n").Video.ReadText());

    [Fact]
    public void Printer() => Assert.Equal("Printed by emu8086ln\r\nLine 2\r\n", RunExample("14_printer.asm").PrinterText);

    [Fact]
    public void FileRoundTrip() =>
        Assert.Equal("This text went to a file and came back.", RunExample("15_file_io.asm").Video.ReadText());

    [Fact]
    public void LedDisplayShowsLastValue()
    {
        var m = RunExample("11_led_display.asm");
        Assert.Equal(unchecked((ushort)-1234), (ushort)m.Ports.In(199, true));
    }

    [Fact]
    public void HelloTemplateIsTheFirstRunProject()
    {
        var m = TestHost.Run(File.ReadAllText(Path.Combine(AppDirectory, "config", "templates", "tr", "hello_com.asm")), "k");
        Assert.Equal(StopReason.Terminated, m.StopReason);
        Assert.StartsWith("Merhaba Dunya!", m.Video.ReadText());
    }

    [Fact]
    public void Bootloader() => Assert.Equal("Hello from the boot sector!", RunExample("19_bootloader.asm").Video.ReadText());

    [Fact]
    public void CustomInterrupt() => Assert.Equal("AABB", RunExample("20_custom_interrupt.asm").Video.ReadText());

    [Fact]
    public void StackParameters() => Assert.Equal("345", RunExample("21_stack_parameters.asm").Video.ReadText());

    [Fact]
    public void StringSearch() => Assert.Equal("found at position 4", RunExample("22_string_search.asm").Video.ReadText());

    [Fact]
    public void Bcd() => Assert.Equal("83", RunExample("23_bcd_arithmetic.asm").Video.ReadText());

    [Fact]
    public void NumberFormats() =>
        Assert.Equal("bin: 0000011111101010\nhex: 07EA\ndec: 2026", RunExample("24_number_formats.asm").Video.ReadText());

    [Fact]
    public void Fibonacci() =>
        Assert.Equal("0 1 1 2 3 5 8 13 21 34 55 89 144 233 377", RunExample("25_fibonacci.asm").Video.ReadText());

    [Fact]
    public void FarCall() =>
        Assert.Equal("In the main segment\nIn the library segment", RunExample("28_far_call.asm").Video.ReadText());
}
