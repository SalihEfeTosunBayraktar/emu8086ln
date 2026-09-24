using Emu8086.Core.Machine;

namespace Emu8086.Core.Devices;

/// <summary>Base for devices whose state is read by the UI thread while the CPU thread writes it.</summary>
public abstract class DeviceBase : IIoDevice
{
    protected readonly object Sync = new();

    public abstract string Id { get; }
    public abstract IReadOnlyList<int> Ports { get; }
    public abstract int Read(int port, bool word);
    public abstract void Write(int port, int value, bool word);
    public abstract void Reset();

    /// <summary>Incremented on every state change so views redraw only when needed.</summary>
    public int Version { get; protected set; }
}

/// <summary>Four traffic lights on port 4 (word). Bits 0-2, 3-5, 6-8, 9-11: red, yellow, green of each light.</summary>
public sealed class TrafficLights : DeviceBase
{
    public const int Port = 4;
    private int _state;

    public override string Id => "traffic_lights";
    public override IReadOnlyList<int> Ports => [Port, Port + 1];

    public int State { get { lock (Sync) return _state; } }

    public bool IsOn(int light, int lamp) => (State >> (light * 3 + lamp) & 1) != 0;

    public override int Read(int port, bool word) => port == Port ? State : State >> 8;

    public override void Write(int port, int value, bool word)
    {
        lock (Sync)
        {
            if (port == Port) _state = word ? value : (_state & 0xFF00) | value;
            else _state = (_state & 0x00FF) | (value << 8);
            Version++;
        }
    }

    public override void Reset()
    {
        lock (Sync) { _state = 0; Version++; }
    }
}

/// <summary>
/// Stepper motor on port 7. The low 3 bits drive the coils; walking the half-step sequence
/// 001,011,010,110,100,101 turns the shaft clockwise. Bit 7 of the input is set when ready.
/// </summary>
public sealed class StepperMotor : DeviceBase
{
    public const int Port = 7;
    public const double DegreesPerHalfStep = 7.5;
    private static readonly int[] HalfSteps = [0b001, 0b011, 0b010, 0b110, 0b100, 0b101];

    private int _last;
    private int _position;

    public override string Id => "stepper_motor";
    public override IReadOnlyList<int> Ports => [Port];

    public double Angle { get { lock (Sync) return _position * DegreesPerHalfStep % 360; } }
    public int Coils { get { lock (Sync) return _last; } }

    public override int Read(int port, bool word) => 0x80 | Coils;

    public override void Write(int port, int value, bool word)
    {
        lock (Sync)
        {
            int next = value & 7;
            int from = Array.IndexOf(HalfSteps, _last);
            int to = Array.IndexOf(HalfSteps, next);
            if (from >= 0 && to >= 0)
            {
                int delta = (to - from + HalfSteps.Length) % HalfSteps.Length;
                if (delta > HalfSteps.Length / 2) delta -= HalfSteps.Length;
                _position += delta;
            }
            _last = next;
            Version++;
        }
    }

    public override void Reset()
    {
        lock (Sync) { _last = 0; _position = 0; Version++; }
    }
}

/// <summary>Four-digit LED display on port 199: shows the signed word written to it.</summary>
public sealed class LedDisplay : DeviceBase
{
    public const int Port = 199;
    private int _value;

    public override string Id => "led_display";
    public override IReadOnlyList<int> Ports => [Port];

    public short Value { get { lock (Sync) return (short)_value; } }

    public override int Read(int port, bool word) => Value;

    public override void Write(int port, int value, bool word)
    {
        lock (Sync)
        {
            _value = word ? value : (sbyte)value;
            Version++;
        }
    }

    public override void Reset()
    {
        lock (Sync) { _value = 0; Version++; }
    }
}

/// <summary>Thermometer and heater: port 125 reads the temperature (°C), port 127 bit 0 switches the heater.</summary>
public sealed class Thermometer : DeviceBase
{
    public const int TemperaturePort = 125;
    public const int HeaterPort = 127;
    public const double Ambient = 20;
    private const double HeatingRate = 3.0;  // °C per second with heater on
    private const double CoolingRate = 0.08; // fraction of the difference to ambient lost per second

    private double _temperature = Ambient;
    private bool _heater;

    public override string Id => "thermometer";
    public override IReadOnlyList<int> Ports => [TemperaturePort, HeaterPort];

    public double Temperature { get { lock (Sync) return _temperature; } }
    public bool HeaterOn { get { lock (Sync) return _heater; } }

    public override int Read(int port, bool word) =>
        port == TemperaturePort ? (int)Math.Round(Temperature) & 0xFF : HeaterOn ? 1 : 0;

    public override void Write(int port, int value, bool word)
    {
        if (port != HeaterPort) return;
        lock (Sync) { _heater = (value & 1) != 0; Version++; }
    }

    /// <summary>Advances the simulation by real time.</summary>
    public void Tick(double seconds)
    {
        lock (Sync)
        {
            if (_heater) _temperature += HeatingRate * seconds;
            _temperature -= (_temperature - Ambient) * CoolingRate * seconds;
            _temperature = Math.Clamp(_temperature, -50, 150);
            Version++;
        }
    }

    public override void Reset()
    {
        lock (Sync) { _temperature = Ambient; _heater = false; Version++; }
    }
}

/// <summary>
/// Robot on a grid. Port 9: command (1 forward, 2 turn left, 3 turn right, 4 examine,
/// 5 lamp on, 6 lamp off). Port 10: examine result (0 empty, 255 wall, 7 lamp on, 8 lamp off).
/// Port 11: status (bit 0 data ready, bit 1 busy, bit 2 error).
/// </summary>
public sealed class Robot : DeviceBase
{
    public const int CommandPort = 9;
    public const int DataPort = 10;
    public const int StatusPort = 11;
    public const int Width = 16;
    public const int Height = 10;

    public enum Cell : byte { Empty, Wall, LampOff, LampOn }

    private static readonly (int Dx, int Dy)[] Directions = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    private readonly Cell[,] _grid = new Cell[Width, Height];
    private int _x, _y, _dir;
    private int _data;
    private int _status;

    public Robot()
    {
        ResetMap();
    }

    public override string Id => "robot";
    public override IReadOnlyList<int> Ports => [CommandPort, DataPort, StatusPort];

    public (int X, int Y, int Direction) Position { get { lock (Sync) return (_x, _y, _dir); } }

    public Cell this[int x, int y] { get { lock (Sync) return _grid[x, y]; } }

    public void Toggle(int x, int y)
    {
        lock (Sync)
        {
            if (x == _x && y == _y) return;
            _grid[x, y] = (Cell)(((int)_grid[x, y] + 1) % 4);
            Version++;
        }
    }

    public override int Read(int port, bool word)
    {
        lock (Sync)
        {
            if (port == DataPort)
            {
                _status &= ~1;
                return _data;
            }
            return port == StatusPort ? _status : 0;
        }
    }

    public override void Write(int port, int value, bool word)
    {
        if (port != CommandPort) return;
        lock (Sync)
        {
            _status &= ~4;
            var (dx, dy) = Directions[_dir];
            int fx = _x + dx, fy = _y + dy;
            bool inside = fx >= 0 && fy >= 0 && fx < Width && fy < Height;
            Cell front = inside ? _grid[fx, fy] : Cell.Wall;
            switch (value & 0xFF)
            {
                case 1:
                    if (front == Cell.Empty) { _x = fx; _y = fy; }
                    else _status |= 4;
                    break;
                case 2: _dir = (_dir + 3) % 4; break;
                case 3: _dir = (_dir + 1) % 4; break;
                case 4:
                    _data = front switch { Cell.Wall => 255, Cell.LampOn => 7, Cell.LampOff => 8, _ => 0 };
                    _status |= 1;
                    break;
                case 5:
                case 6:
                    if (inside && front is Cell.LampOff or Cell.LampOn)
                        _grid[fx, fy] = (value & 0xFF) == 5 ? Cell.LampOn : Cell.LampOff;
                    else _status |= 4;
                    break;
            }
            Version++;
        }
    }

    /// <summary>Returns the robot to its start; the user-edited map (and lamp states) are kept.</summary>
    public override void Reset()
    {
        lock (Sync)
        {
            _x = 1;
            _y = 1;
            _dir = 0;
            _data = 0;
            _status = 0;
            Version++;
        }
    }

    public void ResetMap()
    {
        lock (Sync)
        {
            Array.Clear(_grid);
            for (int x = 0; x < Width; x++) { _grid[x, 0] = Cell.Wall; _grid[x, Height - 1] = Cell.Wall; }
            for (int y = 0; y < Height; y++) { _grid[0, y] = Cell.Wall; _grid[Width - 1, y] = Cell.Wall; }
            _grid[6, 3] = Cell.Wall;
            _grid[6, 4] = Cell.Wall;
            _grid[10, 6] = Cell.Wall;
            _grid[4, 6] = Cell.LampOff;
            _grid[12, 2] = Cell.LampOff;
            _grid[9, 7] = Cell.LampOff;
            _x = 1;
            _y = 1;
            _dir = 0;
            _data = 0;
            _status = 0;
            Version++;
        }
    }
}
