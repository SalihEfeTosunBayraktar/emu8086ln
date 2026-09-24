namespace Emu8086.Core.Cpu;

/// <summary>Receives the previous value of every byte written, used for step-back.</summary>
public interface IMemoryJournal
{
    void Record(int address, byte oldValue);
}

/// <summary>1 MB real-mode address space with 20-bit wrap-around.</summary>
public sealed class Memory
{
    public const int Size = 0x100000;
    private const int AddressMask = Size - 1;

    private readonly byte[] _data = new byte[Size];

    public IMemoryJournal? Journal { get; set; }

    /// <summary>Incremented on every write; lets views skip redraws when nothing changed.</summary>
    public long Version { get; private set; }

    public static int Physical(ushort segment, ushort offset) => ((segment << 4) + offset) & AddressMask;

    public byte Read8(int address) => _data[address & AddressMask];

    public ushort Read16(int address) =>
        (ushort)(_data[address & AddressMask] | (_data[(address + 1) & AddressMask] << 8));

    public byte Read8(ushort segment, ushort offset) => _data[Physical(segment, offset)];

    /// <summary>Word access wraps inside the segment, as on a real 8086.</summary>
    public ushort Read16(ushort segment, ushort offset) =>
        (ushort)(Read8(segment, offset) | (Read8(segment, (ushort)(offset + 1)) << 8));

    public void Write8(int address, byte value)
    {
        address &= AddressMask;
        Journal?.Record(address, _data[address]);
        _data[address] = value;
        Version++;
    }

    public void Write16(int address, ushort value)
    {
        Write8(address, (byte)value);
        Write8(address + 1, (byte)(value >> 8));
    }

    public void Write8(ushort segment, ushort offset, byte value) => Write8(Physical(segment, offset), value);

    public void Write16(ushort segment, ushort offset, ushort value)
    {
        Write8(segment, offset, (byte)value);
        Write8(segment, (ushort)(offset + 1), (byte)(value >> 8));
    }

    /// <summary>Bulk load without journaling (program loading, reset).</summary>
    public void Load(int address, ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
            _data[(address + i) & AddressMask] = bytes[i];
        Version++;
    }

    public void Clear()
    {
        Array.Clear(_data);
        Version++;
    }

    public ReadOnlySpan<byte> Span(int address, int length) => _data.AsSpan(address & AddressMask, Math.Min(length, Size - (address & AddressMask)));

    /// <summary>Restores a byte without journaling (used by step-back).</summary>
    internal void Restore(int address, byte value)
    {
        _data[address & AddressMask] = value;
        Version++;
    }
}
