namespace Emu8086.Core.Machine;

/// <summary>1.44 MB floppy image (80 cylinders, 2 heads, 18 sectors of 512 bytes) stored in a file.</summary>
public sealed class VirtualFloppy
{
    public const int SectorSize = 512;
    public const int Cylinders = 80;
    public const int Heads = 2;
    public const int SectorsPerTrack = 18;
    public const int TotalSectors = Cylinders * Heads * SectorsPerTrack;
    public const int ImageSize = TotalSectors * SectorSize;

    public VirtualFloppy(string imagePath)
    {
        ImagePath = imagePath;
    }

    public string ImagePath { get; }

    /// <summary>Converts cylinder/head/sector (sector is 1-based) to a linear block address, or -1 if invalid.</summary>
    public static int ToLba(int cylinder, int head, int sector) =>
        cylinder is >= 0 and < Cylinders && head is >= 0 and < Heads && sector is >= 1 and <= SectorsPerTrack
            ? (cylinder * Heads + head) * SectorsPerTrack + sector - 1
            : -1;

    private FileStream Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ImagePath))!);
        var stream = new FileStream(ImagePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (stream.Length < ImageSize) stream.SetLength(ImageSize);
        return stream;
    }

    /// <summary>Reads <paramref name="count"/> sectors starting at <paramref name="lba"/>.</summary>
    public byte[] Read(int lba, int count)
    {
        if (lba < 0 || count < 0 || lba + count > TotalSectors) throw new ArgumentOutOfRangeException(nameof(lba));
        using var stream = Open();
        var data = new byte[count * SectorSize];
        stream.Position = (long)lba * SectorSize;
        stream.ReadExactly(data);
        return data;
    }

    /// <summary>Writes whole sectors starting at <paramref name="lba"/>; the last sector is zero-padded.</summary>
    public void Write(int lba, ReadOnlySpan<byte> data)
    {
        int sectors = (data.Length + SectorSize - 1) / SectorSize;
        if (lba < 0 || lba + sectors > TotalSectors) throw new ArgumentOutOfRangeException(nameof(lba));
        using var stream = Open();
        stream.Position = (long)lba * SectorSize;
        stream.Write(data);
        int padding = sectors * SectorSize - data.Length;
        if (padding > 0) stream.Write(new byte[padding]);
    }
}
