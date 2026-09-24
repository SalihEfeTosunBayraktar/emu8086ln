using System.Text;
using Emu8086.Core.Assembler;
using Emu8086.Core.Cpu;
using Emu8086.Core.Machine;

namespace Emu8086.Core.Disassembler;

/// <summary>
/// Turns a loaded executable into assembly text plus a listing that maps every line to its
/// address, so a program without source can be stepped and given breakpoints like source code.
/// </summary>
public static class BinaryDisassembly
{
    private const int MaxInstructions = 20000;
    private const int CommentColumn = 32;

    public static (string Text, List<ListingEntry> Listing) Create(ProgramImage image, string name)
    {
        // Code of COM/BIN starts at the origin; EXE code starts at the entry segment.
        int start = image.Format == OutputFormat.Exe ? image.EntryParagraph * 16 : 0;
        int length = image.Bytes.Length - start;
        ushort offset = (ushort)(image.Format == OutputFormat.Exe ? 0 : image.Origin);

        var memory = new Memory();
        memory.Load(Memory.Physical(0, offset), image.Bytes.AsSpan(start, Math.Max(0, length)));
        var disassembler = new Disassembler8086(memory);

        var sb = new StringBuilder();
        var listing = new List<ListingEntry>();
        int line = 0;
        void Emit(string text)
        {
            sb.AppendLine(text);
            line++;
        }

        Emit($"; {name} ({image.Format.ToString().ToUpperInvariant()}), disassembled by emu8086ln");
        Emit($"#make_{image.Format.ToString().ToUpperInvariant()}#");
        if (image.Format != OutputFormat.Exe) Emit($"org {image.Origin:X}h");
        Emit("");

        int end = offset + length;
        for (int i = 0; i < MaxInstructions && offset < end; i++)
        {
            var ins = disassembler.Decode(0, offset);
            string code = "    " + ins.Text;
            string bytes = string.Join(" ", ins.Bytes.Select(b => b.ToString("X2")));
            Emit($"{code.PadRight(CommentColumn)} ; {offset:X4}: {bytes}");
            listing.Add(new ListingEntry(line, true, 0, offset, ins.Bytes.Length, true, ins.Text));
            offset = (ushort)(offset + ins.Bytes.Length);
            if (offset == 0) break; // wrapped past the end of the segment
        }
        return (sb.ToString(), listing);
    }
}
