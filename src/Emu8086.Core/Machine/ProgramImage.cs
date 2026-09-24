using Emu8086.Core.Assembler;

namespace Emu8086.Core.Machine;

/// <summary>Linked, relocatable program ready to be loaded or saved (.com / .exe / .bin).</summary>
public sealed class ProgramImage
{
    public required OutputFormat Format { get; init; }
    public required byte[] Bytes { get; init; }
    /// <summary>Image offsets of words that receive the load segment (EXE only).</summary>
    public List<int> Relocations { get; } = new();
    /// <summary>Paragraph of each assembler segment relative to the image start.</summary>
    public required int[] SegmentParagraphs { get; init; }
    /// <summary>Offset of image byte 0 inside its segment (0x100 for COM, ORG for BIN/BOOT).</summary>
    public int Origin { get; init; }
    public int EntryParagraph { get; init; }
    public int EntryOffset { get; init; }
    public int StackParagraph { get; init; }
    public int StackPointer { get; init; }

    public static ProgramImage Link(AssemblyResult result)
    {
        var segments = result.Segments;
        if (result.Format != OutputFormat.Exe)
            return LinkFlat(result);

        var paragraphs = new int[segments.Count];
        int para = 0;
        foreach (var (seg, i) in segments.Select((s, i) => (s, i)))
        {
            paragraphs[i] = para;
            para += (seg.Bytes.Count + 15) / 16;
        }
        var bytes = new byte[para * 16];
        var image = new ProgramImage
        {
            Format = OutputFormat.Exe,
            Bytes = bytes,
            SegmentParagraphs = paragraphs,
            EntryParagraph = EntrySegment(result) is var e and >= 0 ? paragraphs[e] : 0,
            EntryOffset = result.Entry?.Offset ?? 0,
            StackParagraph = StackSegment(segments) is var s and >= 0 ? paragraphs[s] : para,
            StackPointer = StackSegment(segments) is var s2 and >= 0 ? StackSize(segments[s2]) : 0x400,
        };

        for (int i = 0; i < segments.Count; i++)
        {
            int baseOffset = paragraphs[i] * 16;
            segments[i].Bytes.CopyTo(bytes, baseOffset);
            foreach (var (offset, target) in segments[i].Fixups)
            {
                int at = baseOffset + offset;
                int value = paragraphs[target];
                bytes[at] = (byte)value;
                bytes[at + 1] = (byte)(value >> 8);
                image.Relocations.Add(at);
            }
        }
        return image;
    }

    private static int StackSize(AsmSegment seg) => seg.Bytes.Count == 0 ? 0x400 : Math.Min(seg.Bytes.Count, 0xFFFE);

    private static int EntrySegment(AssemblyResult r)
    {
        if (r.Entry is { } entry) return entry.Segment;
        int code = r.Segments.FindIndex(s => s.Kind == SegmentKind.Code);
        return code >= 0 ? code : 0;
    }

    private static int StackSegment(List<AsmSegment> segments) => segments.FindIndex(s => s.Kind == SegmentKind.Stack);

    /// <summary>COM, BIN and BOOT: one flat segment starting at its origin.</summary>
    private static ProgramImage LinkFlat(AssemblyResult result)
    {
        var seg = result.Segments.FirstOrDefault(s => s.Bytes.Count > 0) ?? result.Segments.FirstOrDefault();
        int origin = result.Format switch
        {
            OutputFormat.Com => 0x100,
            _ => seg is { Origin: >= 0 } ? seg.Origin : 0,
        };
        var all = seg?.Bytes ?? new List<byte>();
        byte[] bytes = all.Count > origin ? all.Skip(origin).ToArray() : [];
        return new ProgramImage
        {
            Format = result.Format,
            Bytes = bytes,
            SegmentParagraphs = new int[Math.Max(1, result.Segments.Count)],
            Origin = origin,
            EntryOffset = result.Entry?.Offset ?? origin,
        };
    }

    /// <summary>Serialises the image in its DOS file format.</summary>
    public byte[] ToFileBytes()
    {
        if (Format != OutputFormat.Exe) return Bytes;

        int headerParagraphs = (28 + Relocations.Count * 4 + 15) / 16;
        int headerSize = headerParagraphs * 16;
        int total = headerSize + Bytes.Length;
        var file = new byte[total];
        void Put(int at, int value)
        {
            file[at] = (byte)value;
            file[at + 1] = (byte)(value >> 8);
        }

        Put(0, 0x5A4D);                       // "MZ"
        Put(2, total % 512);                  // bytes in last page
        Put(4, (total + 511) / 512);          // pages
        Put(6, Relocations.Count);
        Put(8, headerParagraphs);
        Put(10, StackPointer > 0 && StackParagraph * 16 >= Bytes.Length ? (StackPointer + 15) / 16 : 0); // min alloc
        Put(12, 0xFFFF);                      // max alloc
        Put(14, StackParagraph);
        Put(16, StackPointer);
        Put(18, 0);                           // checksum
        Put(20, EntryOffset);
        Put(22, EntryParagraph);
        Put(24, 28);                          // relocation table offset
        Put(26, 0);                           // overlay
        for (int i = 0; i < Relocations.Count; i++)
        {
            int r = Relocations[i];
            Put(28 + i * 4, r & 0x0F);
            Put(30 + i * 4, r >> 4);
        }
        Bytes.CopyTo(file, headerSize);
        return file;
    }

    /// <summary>Default file extension for the format.</summary>
    public string Extension => Format switch
    {
        OutputFormat.Exe => ".exe",
        OutputFormat.Com => ".com",
        _ => ".bin",
    };
}
