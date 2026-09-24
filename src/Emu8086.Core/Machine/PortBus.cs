using Emu8086.Core.Cpu;

namespace Emu8086.Core.Machine;

/// <summary>A virtual peripheral attached to I/O ports.</summary>
public interface IIoDevice
{
    /// <summary>Stable identifier (matches #start=name#).</summary>
    string Id { get; }
    IReadOnlyList<int> Ports { get; }
    int Read(int port, bool word);
    void Write(int port, int value, bool word);
    void Reset();
}

/// <summary>Routes IN/OUT to devices; unmapped ports keep the last value written (emu8086 behaviour).</summary>
public sealed class PortBus : IPortBus
{
    private readonly Dictionary<int, IIoDevice> _map = new();
    private readonly Dictionary<int, int> _latched = new();

    public List<IIoDevice> Devices { get; } = new();

    /// <summary>Raised for every port access: (port, value, isWrite, word).</summary>
    public event Action<int, int, bool, bool>? Accessed;

    public void Attach(IIoDevice device)
    {
        Devices.Add(device);
        foreach (int p in device.Ports) _map[p] = device;
    }

    public T? Find<T>() where T : class, IIoDevice => Devices.OfType<T>().FirstOrDefault();

    public int In(int port, bool word)
    {
        port &= 0xFFFF;
        int value = _map.TryGetValue(port, out var d)
            ? d.Read(port, word)
            : _latched.TryGetValue(port, out int v) ? v : word ? 0xFFFF : 0xFF;
        value &= word ? 0xFFFF : 0xFF;
        Accessed?.Invoke(port, value, false, word);
        return value;
    }

    public void Out(int port, int value, bool word)
    {
        port &= 0xFFFF;
        value &= word ? 0xFFFF : 0xFF;
        if (_map.TryGetValue(port, out var d)) d.Write(port, value, word);
        else _latched[port] = value;
        Accessed?.Invoke(port, value, true, word);
    }

    public void Reset()
    {
        _latched.Clear();
        foreach (var d in Devices) d.Reset();
    }
}
