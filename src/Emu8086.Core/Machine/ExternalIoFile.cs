namespace Emu8086.Core.Machine;

/// <summary>
/// emu8086-style shared I/O file: one byte per port (64 KB). Ports that no built-in device
/// handles are written to and read from this file, so external programs can act as devices.
/// Word accesses use two consecutive bytes, low byte first.
/// </summary>
public sealed class ExternalIoFile : IDisposable
{
    public const string FileName = "emu8086.io";
    public const int Size = 0x10000;

    private readonly FileStream _stream;
    private readonly object _lock = new();

    public ExternalIoFile(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        _stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (_stream.Length < Size) _stream.SetLength(Size);
    }

    public string Path { get; }

    public int Read(int port, bool word)
    {
        lock (_lock)
        {
            var buffer = new byte[2];
            _stream.Position = port & 0xFFFF;
            _stream.ReadExactly(buffer, 0, word && port < Size - 1 ? 2 : 1);
            return word ? buffer[0] | (buffer[1] << 8) : buffer[0];
        }
    }

    public void Write(int port, int value, bool word)
    {
        lock (_lock)
        {
            _stream.Position = port & 0xFFFF;
            _stream.WriteByte((byte)value);
            if (word && port < Size - 1) _stream.WriteByte((byte)(value >> 8));
            _stream.Flush();
        }
    }

    public void Dispose() => _stream.Dispose();
}
