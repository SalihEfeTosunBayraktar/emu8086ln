using System.Text;
using Emu8086.Core.Assembler;
using Emu8086.Core.Cpu;

namespace Emu8086.Core.Machine;

public enum StopReason
{
    None,
    Terminated,
    HaltInstruction,
    DivideError,
    InvalidOpcode,
    Breakpoint,
    Reboot,
}

public sealed class MouseState
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Buttons { get; set; }
    public bool Visible { get; set; }
}

/// <summary>The complete emulated PC: CPU, memory, BIOS/DOS, video, keyboard, disk and devices.</summary>
public sealed class Machine : IDisposable
{
    public const ushort PspSegment = 0x0700;
    public const ushort ExeLoadSegment = PspSegment + 0x10;
    public const ushort BootSegment = 0x0000;
    public const ushort BootOffset = 0x7C00;

    private readonly StringBuilder _printer = new();

    public Machine(string diskRoot)
    {
        Memory = new Memory();
        Cpu = new Cpu8086(Memory);
        Video = new Video(Memory);
        Keyboard = new Keyboard();
        Ports = new PortBus();
        Disk = new VirtualDisk(diskRoot);
        History = new ExecutionHistory(Memory);
        Bios = new Bios(this);
        Cpu.Ports = Ports;
        Cpu.Interrupts = Bios;
        Ports.Accessed += History.RecordPort;
        PowerOn();
    }

    public Memory Memory { get; }
    public Cpu8086 Cpu { get; }
    public Video Video { get; }
    public Keyboard Keyboard { get; }
    public PortBus Ports { get; }
    public VirtualDisk Disk { get; }
    public Bios Bios { get; }
    public ExecutionHistory History { get; }
    public MouseState Mouse { get; } = new();

    public StopReason StopReason { get; set; }
    public int ExitCode { get; set; }
    public bool HistoryEnabled { get; set; } = true;

    /// <summary>Segment where each assembler segment was loaded (for mapping listing lines to CS:IP).</summary>
    public ushort[] SegmentBases { get; private set; } = [];
    public ProgramImage? Program { get; private set; }

    public string PrinterText
    {
        get { lock (_printer) return _printer.ToString(); }
    }

    public event Action<char>? Printed;

    public void Print(byte ch)
    {
        lock (_printer) _printer.Append((char)ch);
        Printed?.Invoke((char)ch);
    }

    /// <summary>Clears memory and puts BIOS, vectors and video into their power-on state.</summary>
    public void PowerOn()
    {
        Memory.Journal = null;
        Memory.Clear();
        Cpu.Reset();
        Cpu.InstallBiosStubs();
        Memory.Write16(0x413, 640); // BDA: base memory size in KB
        Video.SetMode(3);
        Keyboard.Clear();
        Ports.Reset();
        Disk.CloseAll();
        Bios.Reset();
        History.Clear();
        lock (_printer) _printer.Clear();
        StopReason = StopReason.None;
        ExitCode = 0;
        Mouse.X = Mouse.Y = Mouse.Buttons = 0;
        Mouse.Visible = false;
    }

    public void Load(ProgramImage image)
    {
        PowerOn();
        Program = image;
        switch (image.Format)
        {
            case OutputFormat.Com:
            case OutputFormat.Bin:
                LoadFlat(image, PspSegment);
                break;
            case OutputFormat.Boot:
                LoadBoot(image);
                break;
            default:
                LoadExe(image);
                break;
        }
        ApplyPresets(image.RegisterPresets);
        History.Clear();
    }

    private void WritePsp(ushort segment)
    {
        Memory.Write8(segment, 0, 0xCD); // INT 20h at PSP:0000
        Memory.Write8(segment, 1, 0x20);
        Memory.Write16(segment, 2, 0xA000); // top of memory
        Memory.Write8(segment, 0x80, 0);    // empty command tail
        Memory.Write8(segment, 0x81, 0x0D);
        Bios.DtaSegment = segment;
        Bios.DtaOffset = 0x80;
    }

    private void LoadFlat(ProgramImage image, ushort segment)
    {
        if (image.Format == OutputFormat.Com) WritePsp(segment);
        Memory.Load(Memory.Physical(segment, (ushort)image.Origin), image.Bytes);
        Cpu.CS = Cpu.DS = Cpu.ES = Cpu.SS = segment;
        Cpu.IP = (ushort)image.EntryOffset;
        Cpu.SP = 0xFFFE;
        if (image.Format == OutputFormat.Com) Memory.Write16(segment, 0xFFFE, 0); // RET -> PSP:0000 -> INT 20h
        SegmentBases = Enumerable.Repeat(segment, image.SegmentParagraphs.Length).ToArray();
    }

    private void LoadBoot(ProgramImage image)
    {
        // With ORG 7C00h the code runs at 0000:7C00; otherwise at 07C0:0000 (same physical address).
        ushort segment = image.Origin == BootOffset ? BootSegment : (ushort)(BootOffset >> 4);
        Memory.Load(Memory.Physical(segment, (ushort)image.Origin), image.Bytes);
        Cpu.CS = Cpu.DS = Cpu.ES = Cpu.SS = segment;
        Cpu.IP = (ushort)image.EntryOffset;
        Cpu.SP = image.Origin == BootOffset ? BootOffset : (ushort)0xFFFE;
        Cpu.DX = 0; // boot drive in DL
        SegmentBases = Enumerable.Repeat(segment, image.SegmentParagraphs.Length).ToArray();
    }

    private void LoadExe(ProgramImage image)
    {
        WritePsp(PspSegment);
        ushort load = ExeLoadSegment;
        Memory.Load(Memory.Physical(load, 0), image.Bytes);
        foreach (int r in image.Relocations)
        {
            int at = Memory.Physical(load, 0) + r;
            Memory.Load(at, BitConverter.GetBytes((ushort)(Memory.Read16(at) + load)));
        }
        Cpu.CS = (ushort)(load + image.EntryParagraph);
        Cpu.IP = (ushort)image.EntryOffset;
        Cpu.SS = (ushort)(load + image.StackParagraph);
        Cpu.SP = (ushort)image.StackPointer;
        Cpu.DS = Cpu.ES = PspSegment;
        SegmentBases = image.SegmentParagraphs.Select(p => (ushort)(load + p)).ToArray();
    }

    private void ApplyPresets(Dictionary<string, ushort> presets)
    {
        foreach (var (name, value) in presets)
        {
            switch (name)
            {
                case "AX": Cpu.AX = value; break;
                case "BX": Cpu.BX = value; break;
                case "CX": Cpu.CX = value; break;
                case "DX": Cpu.DX = value; break;
                case "SI": Cpu.SI = value; break;
                case "DI": Cpu.DI = value; break;
                case "BP": Cpu.BP = value; break;
                case "SP": Cpu.SP = value; break;
                case "CS": Cpu.CS = value; break;
                case "DS": Cpu.DS = value; break;
                case "ES": Cpu.ES = value; break;
                case "SS": Cpu.SS = value; break;
                case "IP": Cpu.IP = value; break;
            }
        }
    }

    public bool IsStopped => Cpu.Halted || StopReason is not (StopReason.None or StopReason.Breakpoint);

    /// <summary>Executes one instruction, recording it for step-back when history is enabled.</summary>
    public StepResult Step()
    {
        if (IsStopped) return StepResult.Halted;
        if (StopReason == StopReason.Breakpoint) StopReason = StopReason.None;

        StepResult result;
        if (!HistoryEnabled) result = Cpu.Step();
        else
        {
            History.Begin(Cpu.GetState());
            Memory.Journal = History;
            result = Cpu.Step();
            Memory.Journal = null;
            // A waiting instruction did not complete; it is retried and recorded then.
            if (result == StepResult.Waiting) History.Cancel();
            else History.Commit(Cpu.GetState());
        }
        if (Cpu.Halted && StopReason == StopReason.None) StopReason = StopReason.HaltInstruction;
        return result;
    }

    /// <summary>Undoes the last instruction. Returns false when there is nothing to undo.</summary>
    public bool StepBack()
    {
        if (!History.Undo(Cpu)) return false;
        StopReason = StopReason.None;
        return true;
    }

    public int PhysicalAddress(int segmentIndex, int offset) =>
        segmentIndex >= 0 && segmentIndex < SegmentBases.Length
            ? Memory.Physical(SegmentBases[segmentIndex], (ushort)offset)
            : -1;

    public void Dispose() => Disk.Dispose();
}
