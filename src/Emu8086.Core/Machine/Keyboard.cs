namespace Emu8086.Core.Machine;

/// <summary>BIOS keyboard buffer. Keys are (scan code &lt;&lt; 8) | ASCII, as INT 16h returns them.</summary>
public sealed class Keyboard
{
    private const int Capacity = 64;
    private readonly Queue<ushort> _buffer = new();
    private readonly object _lock = new();

    public event Action? KeyAvailable;

    /// <summary>Shift/Ctrl/Alt state in INT 16h/02h format.</summary>
    public byte ShiftFlags { get; set; }

    public void Push(byte ascii, byte scanCode)
    {
        lock (_lock)
        {
            if (_buffer.Count >= Capacity) return;
            _buffer.Enqueue((ushort)((scanCode << 8) | ascii));
        }
        KeyAvailable?.Invoke();
    }

    public void PushText(string text)
    {
        foreach (char c in text) Push((byte)(c == '\n' ? '\r' : c), c == '\n' ? (byte)0x1C : (byte)0);
    }

    public bool TryPeek(out ushort key)
    {
        lock (_lock) return _buffer.TryPeek(out key);
    }

    public bool TryRead(out ushort key)
    {
        lock (_lock) return _buffer.TryDequeue(out key);
    }

    public bool HasKey
    {
        get { lock (_lock) return _buffer.Count > 0; }
    }

    public void Clear()
    {
        lock (_lock) _buffer.Clear();
    }
}
