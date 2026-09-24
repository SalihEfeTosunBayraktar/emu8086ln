using System.Diagnostics;
using System.IO;
using System.Threading;
using Emu8086.Core.Assembler;
using Emu8086.Core.Cpu;
using Emu8086.Core.Devices;
using Emu8086.Core.Disassembler;
using Emu8086.Core.Machine;

namespace Emu8086.App.Services;

public enum SessionState { Empty, Ready, Running, Paused, WaitingInput, Stopped }

/// <summary>
/// Owns the emulated machine and runs it on a background thread. All access to
/// <see cref="Machine"/> from other threads must hold <see cref="Sync"/>.
/// </summary>
public sealed class EmulatorSession : IDisposable
{
    private const int BatchInstructions = 20000;
    private static readonly TimeSpan BatchTime = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan InputPoll = TimeSpan.FromMilliseconds(15);

    private readonly AutoResetEvent _wake = new(false);
    private readonly Dictionary<int, ListingEntry> _addressToLine = new();
    private readonly Dictionary<int, int> _lineToAddress = new();
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private SessionState _state = SessionState.Empty;

    public EmulatorSession()
    {
        Directory.CreateDirectory(AppPaths.VirtualDriveDirectory);
        Machine = new Machine(AppPaths.VirtualDriveDirectory);
        Machine.Ports.Attach(new TrafficLights());
        Machine.Ports.Attach(new StepperMotor());
        Machine.Ports.Attach(new LedDisplay());
        Machine.Ports.Attach(new Thermometer());
        Machine.Ports.Attach(new Robot());
        Machine.Keyboard.KeyAvailable += () => _wake.Set();
        Disassembler = new Disassembler8086(Machine.Memory);
    }

    public object Sync { get; } = new();
    public Machine Machine { get; }
    public Disassembler8086 Disassembler { get; }
    public BuildOutput? Build { get; private set; }
    /// <summary>Physical addresses with a breakpoint.</summary>
    public HashSet<int> Breakpoints { get; } = new();
    /// <summary>Delay between instructions (ms). 0 = full speed.</summary>
    public int StepDelayMs { get; set; }

    public SessionState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            StateChanged?.Invoke(value);
        }
    }

    /// <summary>Raised on the emulator thread; subscribers must marshal to the UI thread.</summary>
    public event Action<SessionState>? StateChanged;

    public bool IsBusy => State is SessionState.Running or SessionState.WaitingInput;
    public bool CanRun => State is SessionState.Ready or SessionState.Paused;

    public void Load(BuildOutput build)
    {
        Stop();
        Build = build;
        _addressToLine.Clear();
        _lineToAddress.Clear();
        if (build.Image == null)
        {
            State = SessionState.Empty;
            return;
        }
        lock (Sync)
        {
            Machine.Load(build.Image);
            foreach (var entry in build.Result.Listing.Where(l => l.FromMainFile && l.IsCode))
            {
                int address = Machine.PhysicalAddress(entry.Segment, entry.Offset);
                if (address < 0) continue;
                _addressToLine.TryAdd(address, entry);
                _lineToAddress.TryAdd(entry.Line, address);
            }
        }
        State = SessionState.Ready;
    }

    /// <summary>Restarts the loaded program from the beginning.</summary>
    public void Reset()
    {
        if (Build != null) Load(Build);
    }

    public int CurrentAddress
    {
        get
        {
            lock (Sync) return Memory.Physical(Machine.Cpu.CS, Machine.Cpu.IP);
        }
    }

    /// <summary>Editor line of the instruction at CS:IP, or null if it is not user code.</summary>
    public int? CurrentLine => LineOfAddress(CurrentAddress);

    public int? LineOfAddress(int address) => _addressToLine.TryGetValue(address, out var e) ? e.Line : null;

    public int? AddressOfLine(int line) => _lineToAddress.TryGetValue(line, out int a) ? a : null;

    public IEnumerable<int> CodeLines => _lineToAddress.Keys;

    #region Execution control

    public void Run() => Start(new RunRequest());

    public void StepInto() => Start(new RunRequest { MaxInstructions = 1 });

    public void RunTo(int address) => Start(new RunRequest { StopAddress = address });

    /// <summary>Executes CALL / INT / LOOP / REP as a single step.</summary>
    public void StepOver()
    {
        DisassembledInstruction ins;
        ushort sp;
        lock (Sync)
        {
            ins = Disassembler.Decode(Machine.Cpu.CS, Machine.Cpu.IP);
            sp = Machine.Cpu.SP;
        }
        string m = ins.Mnemonic;
        bool over = m.StartsWith("CALL") || m.StartsWith("INT") || m.StartsWith("LOOP") || m.StartsWith("REP");
        if (!over)
        {
            StepInto();
            return;
        }
        int next = Memory.Physical(ins.Segment, (ushort)(ins.Offset + ins.Bytes.Length));
        Start(new RunRequest { StopAddress = next, MinStackPointer = sp });
    }

    public void StepBack()
    {
        if (IsBusy) return;
        lock (Sync)
        {
            if (!Machine.StepBack()) return;
        }
        State = SessionState.Paused;
        StateChanged?.Invoke(State);
    }

    public void Pause()
    {
        _runCts?.Cancel();
        _wake.Set();
        _runTask?.Wait(500);
    }

    public void Stop()
    {
        Pause();
        _runCts = null;
        _runTask = null;
    }

    private sealed class RunRequest
    {
        public long MaxInstructions { get; init; } = long.MaxValue;
        public int StopAddress { get; init; } = -1;
        public int MinStackPointer { get; init; } = -1;
    }

    private void Start(RunRequest request)
    {
        if (!CanRun) return;
        Pause();
        var cts = new CancellationTokenSource();
        _runCts = cts;
        State = SessionState.Running;
        _runTask = Task.Run(() => RunLoop(request, cts.Token));
    }

    private void RunLoop(RunRequest request, CancellationToken token)
    {
        long executed = 0;
        bool first = true;
        var thermometer = Machine.Ports.Find<Thermometer>();
        var clock = Stopwatch.StartNew();
        var result = SessionState.Paused;

        while (!token.IsCancellationRequested)
        {
            bool waiting = false;
            bool finished = false;
            lock (Sync)
            {
                var batch = Stopwatch.StartNew();
                int budget = StepDelayMs > 0 ? 1 : BatchInstructions;
                for (int i = 0; i < budget && batch.Elapsed < BatchTime; i++)
                {
                    var cpu = Machine.Cpu;
                    int address = Memory.Physical(cpu.CS, cpu.IP);
                    if (!first && (Breakpoints.Contains(address) || StopRequested(request, address, cpu.SP)))
                    {
                        finished = true;
                        break;
                    }
                    first = false;

                    var r = Machine.Step();
                    if (r == StepResult.Waiting)
                    {
                        waiting = true;
                        break;
                    }
                    executed++;
                    if (Machine.IsStopped)
                    {
                        result = SessionState.Stopped;
                        finished = true;
                        break;
                    }
                    if (Machine.StopReason == StopReason.Breakpoint || executed >= request.MaxInstructions)
                    {
                        finished = true;
                        break;
                    }
                }
                thermometer?.Tick(clock.Elapsed.TotalSeconds);
                clock.Restart();
            }

            if (finished) break;
            if (waiting)
            {
                State = SessionState.WaitingInput;
                _wake.WaitOne(InputPoll);
                continue;
            }
            State = SessionState.Running;
            if (StepDelayMs > 0) Thread.Sleep(StepDelayMs);
        }
        State = result;
        if (result == SessionState.Paused) StateChanged?.Invoke(result);
    }

    private static bool StopRequested(RunRequest request, int address, ushort sp) =>
        address == request.StopAddress && (request.MinStackPointer < 0 || sp >= request.MinStackPointer);

    #endregion

    /// <summary>Opens or closes the shared emu8086.io file for external devices.</summary>
    public void SetExternalIo(bool enabled)
    {
        lock (Sync)
        {
            var ports = Machine.Ports;
            if (enabled && ports.External == null)
            {
                try
                {
                    ports.External = new ExternalIoFile(AppPaths.ExternalIoFile);
                }
                catch (IOException)
                {
                    ports.External = null;
                }
            }
            else if (!enabled && ports.External != null)
            {
                ports.External.Dispose();
                ports.External = null;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        SetExternalIo(false);
        Machine.Dispose();
        _wake.Dispose();
    }
}
