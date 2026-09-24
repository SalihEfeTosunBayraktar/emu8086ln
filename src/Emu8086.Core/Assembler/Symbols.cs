namespace Emu8086.Core.Assembler;

public enum SymbolKind { Label, Variable, Constant, Procedure, Segment }

public sealed class Symbol
{
    public required string Name { get; init; }
    public SymbolKind Kind { get; set; }
    /// <summary>Index into the segment list, or -1 for absolute constants.</summary>
    public int Segment { get; set; } = -1;
    public long Value { get; set; }
    /// <summary>Element size in bytes for variables (1, 2, 4, 8, 10); 0 for code labels.</summary>
    public int ElementSize { get; set; }
    /// <summary>Number of elements defined (LENGTH).</summary>
    public int Length { get; set; } = 1;
    public bool Far { get; set; }
    public int DefinedInPass { get; set; }
    public int Line { get; set; }
}

public enum SegmentKind { Code, Data, Stack, Other }

public sealed class AsmSegment
{
    public required string Name { get; init; }
    public SegmentKind Kind { get; set; }
    public List<byte> Bytes { get; } = new();
    /// <summary>Location counter (offset inside the segment).</summary>
    public int Location { get; set; }
    public int Origin { get; set; } = -1;
    /// <summary>Offsets (inside this segment) of words that must receive a segment base at load time.</summary>
    public List<(int Offset, int TargetSegment)> Fixups { get; } = new();
    public bool Uninitialized { get; set; }

    public void Emit(byte b)
    {
        while (Bytes.Count < Location) Bytes.Add(0);
        if (Location < Bytes.Count) Bytes[Location] = b;
        else Bytes.Add(b);
        Location++;
    }
}
