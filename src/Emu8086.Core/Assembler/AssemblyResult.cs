namespace Emu8086.Core.Assembler;

public enum OutputFormat { Com, Exe, Bin, Boot }

/// <summary>Bytes produced by one source line; used to map CS:IP back to the editor.</summary>
public sealed record ListingEntry(int Line, bool FromMainFile, int Segment, int Offset, int Length, bool IsCode, string Text = "");

/// <summary>Registers that emu8086-style #REG=value# directives may preset.</summary>
public static class RegisterPreset
{
    public static readonly HashSet<string> Names =
        ["AX", "BX", "CX", "DX", "SI", "DI", "BP", "SP", "CS", "DS", "ES", "SS", "IP"];
}

public sealed class AssemblyResult
{
    public List<AsmDiagnostic> Diagnostics { get; } = new();
    public OutputFormat Format { get; set; }
    public List<AsmSegment> Segments { get; } = new();
    public Dictionary<string, Symbol> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ListingEntry> Listing { get; } = new();
    /// <summary>Entry point (segment index, offset); null means start of the first code segment.</summary>
    public (int Segment, int Offset)? Entry { get; set; }
    /// <summary>Virtual devices requested with #start=name#.</summary>
    public List<string> Devices { get; } = new();
    /// <summary>Initial register values requested with #AX=1234h# style directives.</summary>
    public Dictionary<string, ushort> RegisterPresets { get; } = new();
    public int Passes { get; set; }

    public bool Success => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}
