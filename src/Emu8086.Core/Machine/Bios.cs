using System.Text;
using Emu8086.Core.Cpu;

namespace Emu8086.Core.Machine;

/// <summary>Native BIOS (INT 10h, 11h, 12h, 15h, 16h, 17h, 1Ah, 33h) and DOS (INT 20h, 21h) services.</summary>
public sealed class Bios : IInterruptHandler
{
    private const int MaxDollarString = 0x10000;
    private const double TicksPerSecond = 1193182.0 / 65536.0;

    private readonly Machine _m;
    private List<byte>? _line;
    private DateTime? _waitUntil;

    public Bios(Machine machine)
    {
        _m = machine;
    }

    public ushort DtaSegment { get; set; }
    public ushort DtaOffset { get; set; }

    /// <summary>Local time source; replaceable for tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    public void Reset()
    {
        _diskStatus = 0;
        _line = null;
        _waitUntil = null;
    }

    private Memory Mem => _m.Memory;
    private Video Video => _m.Video;

    public InterruptResult Handle(int vector, Cpu8086 cpu) => vector switch
    {
        0x00 => Stop(StopReason.DivideError),
        0x03 => Break(),
        0x06 => Stop(StopReason.InvalidOpcode),
        0x01 or 0x04 or 0x05 or 0x08 or 0x1C => InterruptResult.Handled,
        0x10 => Int10(cpu),
        0x11 => Set(() => cpu.AX = 0x0021),
        0x12 => Set(() => cpu.AX = 640),
        0x13 => Int13(cpu),
        0x15 => Int15(cpu),
        0x16 => Int16(cpu),
        0x17 => Int17(cpu),
        0x19 => Stop(StopReason.Reboot),
        0x1A => Int1A(cpu),
        0x20 => Terminate(0),
        0x21 => Int21(cpu),
        0x33 => Int33(cpu),
        _ => InterruptResult.NotHandled,
    };

    private static InterruptResult Set(Action action)
    {
        action();
        return InterruptResult.Handled;
    }

    private InterruptResult Stop(StopReason reason)
    {
        _m.StopReason = reason;
        return InterruptResult.Halt;
    }

    private InterruptResult Break()
    {
        _m.StopReason = StopReason.Breakpoint;
        return InterruptResult.Handled;
    }

    private InterruptResult Terminate(int code)
    {
        _m.ExitCode = code;
        return Stop(StopReason.Terminated);
    }

    #region INT 10h video

    private InterruptResult Int10(Cpu8086 cpu)
    {
        switch (cpu.AH)
        {
            case 0x00:
                Video.SetMode(cpu.AL & 0x7F, (cpu.AL & 0x80) == 0);
                break;
            case 0x01:
                Video.CursorVisible = (cpu.CH & 0x20) == 0;
                break;
            case 0x02:
                Video.CursorY = Math.Min(cpu.DH, (byte)(Video.Rows - 1));
                Video.CursorX = Math.Min(cpu.DL, (byte)(Video.Columns - 1));
                break;
            case 0x03:
                cpu.DH = (byte)Video.CursorY;
                cpu.DL = (byte)Video.CursorX;
                cpu.CX = Video.CursorVisible ? (ushort)0x0607 : (ushort)0x2000;
                break;
            case 0x05:
                break; // single display page
            case 0x06:
            case 0x07:
                Video.Scroll(cpu.AH == 0x06, cpu.AL, cpu.BH, cpu.CH, cpu.CL, cpu.DH, cpu.DL);
                break;
            case 0x08:
            {
                var cell = Video.ReadCell(Video.CursorX, Video.CursorY);
                cpu.AL = cell.Char;
                cpu.AH = cell.Attr;
                break;
            }
            case 0x09:
            case 0x0A:
            {
                int x = Video.CursorX, y = Video.CursorY;
                for (int i = 0; i < cpu.CX; i++)
                {
                    int cx = (x + i) % Video.Columns;
                    int cy = y + (x + i) / Video.Columns;
                    if (cy >= Video.Rows) break;
                    byte attr = cpu.AH == 0x09 ? cpu.BL : Video.ReadCell(cx, cy).Attr;
                    Video.WriteCell(cx, cy, cpu.AL, attr);
                }
                break;
            }
            case 0x0C:
                Video.PutPixel(cpu.CX, cpu.DX, cpu.AL);
                break;
            case 0x0D:
                cpu.AL = Video.GetPixel(cpu.CX, cpu.DX);
                break;
            case 0x0E:
                Video.Teletype(cpu.AL);
                break;
            case 0x0F:
                cpu.AL = (byte)Video.Mode;
                cpu.AH = (byte)Video.Columns;
                cpu.BH = 0;
                break;
            case 0x10:
                if (cpu.AL == 0x03) Video.BlinkEnabled = cpu.BL != 0;
                else if (cpu.AL == 0x10) Video.SetPaletteEntry(cpu.BX, cpu.DH, cpu.CH, cpu.CL);
                break;
            case 0x13:
                WriteString(cpu);
                break;
            case 0x1A:
                cpu.AL = 0x1A;
                cpu.BX = 0x0008;
                break;
        }
        return InterruptResult.Handled;
    }

    private void WriteString(Cpu8086 cpu)
    {
        int savedX = Video.CursorX, savedY = Video.CursorY;
        Video.CursorY = cpu.DH;
        Video.CursorX = cpu.DL;
        bool withAttributes = (cpu.AL & 2) != 0;
        ushort offset = cpu.BP;
        for (int i = 0; i < cpu.CX; i++)
        {
            byte ch = Mem.Read8(cpu.ES, offset++);
            byte attr = withAttributes ? Mem.Read8(cpu.ES, offset++) : cpu.BL;
            if (ch is 7 or 8 or 10 or 13) Video.Teletype(ch);
            else Video.Teletype(ch, attr);
        }
        if ((cpu.AL & 1) == 0)
        {
            Video.CursorX = savedX;
            Video.CursorY = savedY;
        }
    }

    #endregion

    #region Keyboard, timer, misc BIOS

    #region INT 13h disk

    private const byte DiskOk = 0x00;
    private const byte DiskBadCommand = 0x01;
    private const byte DiskSectorNotFound = 0x04;
    private const byte DiskTimeout = 0x80;
    private byte _diskStatus;

    /// <summary>Floppy services for drive A: (DL = 0) backed by the virtual floppy image.</summary>
    private InterruptResult Int13(Cpu8086 cpu)
    {
        void Finish(byte status, byte? count = null)
        {
            _diskStatus = status;
            cpu.AH = status;
            if (count is byte c) cpu.AL = c;
            cpu.SetFlag(CpuFlags.CF, status != DiskOk);
        }

        if (cpu.AH is not (0x00 or 0x01) && cpu.DL != 0)
        {
            Finish(DiskTimeout, 0);
            return InterruptResult.Handled;
        }

        switch (cpu.AH)
        {
            case 0x00:
                Finish(DiskOk);
                break;
            case 0x01:
                cpu.AL = _diskStatus;
                Finish(DiskOk);
                cpu.AL = _diskStatus;
                break;
            case 0x02:
            case 0x03:
            {
                int cylinder = cpu.CH | ((cpu.CL & 0xC0) << 2);
                int lba = VirtualFloppy.ToLba(cylinder, cpu.DH, cpu.CL & 0x3F);
                int count = cpu.AL;
                if (lba < 0 || count == 0 || lba + count > VirtualFloppy.TotalSectors)
                {
                    Finish(DiskSectorNotFound, 0);
                    break;
                }
                try
                {
                    int bytes = count * VirtualFloppy.SectorSize;
                    if (cpu.AH == 0x02)
                    {
                        var data = _m.Floppy.Read(lba, count);
                        for (int i = 0; i < bytes; i++) Mem.Write8(cpu.ES, (ushort)(cpu.BX + i), data[i]);
                    }
                    else
                    {
                        var data = new byte[bytes];
                        for (int i = 0; i < bytes; i++) data[i] = Mem.Read8(cpu.ES, (ushort)(cpu.BX + i));
                        _m.Floppy.Write(lba, data);
                    }
                    Finish(DiskOk, (byte)count);
                }
                catch (IOException)
                {
                    Finish(DiskTimeout, 0);
                }
                break;
            }
            case 0x04:
                Finish(DiskOk, cpu.AL); // verify
                break;
            case 0x08:
                cpu.BL = 4; // 1.44 MB
                cpu.CH = VirtualFloppy.Cylinders - 1;
                cpu.CL = VirtualFloppy.SectorsPerTrack;
                cpu.DH = VirtualFloppy.Heads - 1;
                cpu.DL = 1;
                Finish(DiskOk);
                break;
            case 0x15:
                Finish(DiskOk);
                cpu.AH = 1; // floppy without change-line
                break;
            default:
                Finish(DiskBadCommand);
                break;
        }
        return InterruptResult.Handled;
    }

    #endregion

    private InterruptResult Int16(Cpu8086 cpu)
    {
        switch (cpu.AH)
        {
            case 0x00:
            case 0x10:
                if (!_m.Keyboard.TryRead(out ushort key)) return InterruptResult.Wait;
                cpu.AX = key;
                break;
            case 0x01:
            case 0x11:
                if (_m.Keyboard.TryPeek(out ushort peek))
                {
                    cpu.AX = peek;
                    cpu.SetFlag(CpuFlags.ZF, false);
                }
                else cpu.SetFlag(CpuFlags.ZF, true);
                break;
            case 0x02:
            case 0x12:
                cpu.AL = _m.Keyboard.ShiftFlags;
                break;
        }
        return InterruptResult.Handled;
    }

    private InterruptResult Int15(Cpu8086 cpu)
    {
        if (cpu.AH != 0x86)
        {
            cpu.SetFlag(CpuFlags.CF, true);
            cpu.AH = 0x86;
            return InterruptResult.Handled;
        }
        // Wait CX:DX microseconds without blocking the emulator thread.
        var now = Clock();
        _waitUntil ??= now.AddTicks(((long)cpu.CX << 16 | cpu.DX) * 10);
        if (now < _waitUntil) return InterruptResult.Wait;
        _waitUntil = null;
        cpu.SetFlag(CpuFlags.CF, false);
        return InterruptResult.Handled;
    }

    private InterruptResult Int17(Cpu8086 cpu)
    {
        if (cpu.AH == 0x00) _m.Print(cpu.AL);
        cpu.AH = 0x90; // not busy, selected
        return InterruptResult.Handled;
    }

    private static byte Bcd(int v) => (byte)((v / 10 << 4) | (v % 10));

    private InterruptResult Int1A(Cpu8086 cpu)
    {
        var now = Clock();
        switch (cpu.AH)
        {
            case 0x00:
            {
                uint ticks = (uint)(now.TimeOfDay.TotalSeconds * TicksPerSecond);
                cpu.CX = (ushort)(ticks >> 16);
                cpu.DX = (ushort)ticks;
                cpu.AL = 0;
                break;
            }
            case 0x02:
                cpu.CH = Bcd(now.Hour);
                cpu.CL = Bcd(now.Minute);
                cpu.DH = Bcd(now.Second);
                cpu.SetFlag(CpuFlags.CF, false);
                break;
            case 0x04:
                cpu.CH = Bcd(now.Year / 100);
                cpu.CL = Bcd(now.Year % 100);
                cpu.DH = Bcd(now.Month);
                cpu.DL = Bcd(now.Day);
                cpu.SetFlag(CpuFlags.CF, false);
                break;
        }
        return InterruptResult.Handled;
    }

    private InterruptResult Int33(Cpu8086 cpu)
    {
        var mouse = _m.Mouse;
        switch (cpu.AX)
        {
            case 0x0000:
                cpu.AX = 0xFFFF;
                cpu.BX = 2;
                mouse.Visible = false;
                break;
            case 0x0001: mouse.Visible = true; break;
            case 0x0002: mouse.Visible = false; break;
            case 0x0003:
                cpu.BX = (ushort)mouse.Buttons;
                cpu.CX = (ushort)mouse.X;
                cpu.DX = (ushort)mouse.Y;
                break;
            case 0x0004:
                mouse.X = cpu.CX;
                mouse.Y = cpu.DX;
                break;
        }
        return InterruptResult.Handled;
    }

    #endregion

    #region INT 21h DOS

    private static void DosResult(Cpu8086 cpu, int result)
    {
        if (result < 0)
        {
            cpu.AX = (ushort)-result;
            cpu.SetFlag(CpuFlags.CF, true);
        }
        else
        {
            cpu.AX = (ushort)result;
            cpu.SetFlag(CpuFlags.CF, false);
        }
    }

    private string ReadAsciiz(ushort segment, ushort offset)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 128; i++)
        {
            byte b = Mem.Read8(segment, (ushort)(offset + i));
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    private void Output(byte ch) => Video.Teletype(ch);

    private InterruptResult Int21(Cpu8086 cpu)
    {
        switch (cpu.AH)
        {
            case 0x00:
                return Terminate(0);
            case 0x01:
            {
                if (!_m.Keyboard.TryRead(out ushort key)) return InterruptResult.Wait;
                cpu.AL = (byte)key;
                if (cpu.AL != 0) Output(cpu.AL);
                if (cpu.AL == 13) Output(10);
                break;
            }
            case 0x02:
                Output(cpu.DL);
                cpu.AL = cpu.DL;
                break;
            case 0x05:
                _m.Print(cpu.DL);
                break;
            case 0x06:
                if (cpu.DL == 0xFF)
                {
                    if (_m.Keyboard.TryRead(out ushort k))
                    {
                        cpu.AL = (byte)k;
                        cpu.SetFlag(CpuFlags.ZF, false);
                    }
                    else
                    {
                        cpu.AL = 0;
                        cpu.SetFlag(CpuFlags.ZF, true);
                    }
                }
                else
                {
                    Output(cpu.DL);
                    cpu.AL = cpu.DL;
                }
                break;
            case 0x07:
            case 0x08:
            {
                if (!_m.Keyboard.TryRead(out ushort key)) return InterruptResult.Wait;
                cpu.AL = (byte)key;
                break;
            }
            case 0x09:
            {
                for (int i = 0; i < MaxDollarString; i++)
                {
                    byte ch = Mem.Read8(cpu.DS, (ushort)(cpu.DX + i));
                    if (ch == '$') break;
                    Output(ch);
                }
                cpu.AL = (byte)'$';
                break;
            }
            case 0x0A:
                return BufferedInput(cpu);
            case 0x0B:
                cpu.AL = _m.Keyboard.HasKey ? (byte)0xFF : (byte)0;
                break;
            case 0x0C:
            {
                if (_line == null) _m.Keyboard.Clear();
                byte function = cpu.AL;
                if (function is not (0x01 or 0x06 or 0x07 or 0x08 or 0x0A)) break;
                ushort saved = cpu.AX;
                cpu.AH = function;
                var r = Int21(cpu);
                if (r == InterruptResult.Wait) cpu.AX = saved;
                else cpu.AH = 0x0C;
                return r;
            }
            case 0x0E:
                cpu.AL = 26;
                break;
            case 0x19:
                cpu.AL = 2; // C:
                break;
            case 0x1A:
                DtaSegment = cpu.DS;
                DtaOffset = cpu.DX;
                break;
            case 0x25:
                Mem.Write16(cpu.AL * 4, cpu.DX);
                Mem.Write16(cpu.AL * 4 + 2, cpu.DS);
                break;
            case 0x2A:
            {
                var now = Clock();
                cpu.CX = (ushort)now.Year;
                cpu.DH = (byte)now.Month;
                cpu.DL = (byte)now.Day;
                cpu.AL = (byte)now.DayOfWeek;
                break;
            }
            case 0x2C:
            {
                var now = Clock();
                cpu.CH = (byte)now.Hour;
                cpu.CL = (byte)now.Minute;
                cpu.DH = (byte)now.Second;
                cpu.DL = (byte)(now.Millisecond / 10);
                break;
            }
            case 0x2F:
                cpu.ES = DtaSegment;
                cpu.BX = DtaOffset;
                break;
            case 0x30:
                cpu.AX = 0x0005; // DOS 5.0
                cpu.BX = 0;
                cpu.CX = 0;
                break;
            case 0x31:
                return Terminate(cpu.AL);
            case 0x35:
                cpu.BX = Mem.Read16(cpu.AL * 4);
                cpu.ES = Mem.Read16(cpu.AL * 4 + 2);
                break;
            case 0x39: DosResult(cpu, _m.Disk.MakeDirectory(ReadAsciiz(cpu.DS, cpu.DX))); break;
            case 0x3A: DosResult(cpu, _m.Disk.RemoveDirectory(ReadAsciiz(cpu.DS, cpu.DX))); break;
            case 0x3B: DosResult(cpu, _m.Disk.ChangeDirectory(ReadAsciiz(cpu.DS, cpu.DX))); break;
            case 0x3C: DosResult(cpu, _m.Disk.Create(ReadAsciiz(cpu.DS, cpu.DX))); break;
            case 0x3D: DosResult(cpu, _m.Disk.Open(ReadAsciiz(cpu.DS, cpu.DX), cpu.AL)); break;
            case 0x3E:
                DosResult(cpu, cpu.BX < 5 ? 0 : _m.Disk.Close(cpu.BX));
                break;
            case 0x3F:
                return ReadHandle(cpu);
            case 0x40:
                WriteHandle(cpu);
                break;
            case 0x41: DosResult(cpu, _m.Disk.Delete(ReadAsciiz(cpu.DS, cpu.DX))); break;
            case 0x42:
            {
                long pos = _m.Disk.Seek(cpu.BX, cpu.AL, (cpu.CX << 16) | cpu.DX);
                if (pos < 0) DosResult(cpu, (int)pos);
                else
                {
                    cpu.DX = (ushort)(pos >> 16);
                    cpu.AX = (ushort)pos;
                    cpu.SetFlag(CpuFlags.CF, false);
                }
                break;
            }
            case 0x43:
            {
                int attr = _m.Disk.GetAttributes(ReadAsciiz(cpu.DS, cpu.DX));
                if (attr < 0) DosResult(cpu, attr);
                else
                {
                    cpu.CX = (ushort)attr;
                    cpu.SetFlag(CpuFlags.CF, false);
                }
                break;
            }
            case 0x47:
            {
                string dir = _m.Disk.CurrentDirectory.ToUpperInvariant();
                for (int i = 0; i < dir.Length; i++) Mem.Write8(cpu.DS, (ushort)(cpu.SI + i), (byte)dir[i]);
                Mem.Write8(cpu.DS, (ushort)(cpu.SI + dir.Length), 0);
                cpu.SetFlag(CpuFlags.CF, false);
                break;
            }
            case 0x48:
                cpu.AX = 8; // insufficient memory
                cpu.BX = 0;
                cpu.SetFlag(CpuFlags.CF, true);
                break;
            case 0x49:
            case 0x4A:
                cpu.SetFlag(CpuFlags.CF, false);
                break;
            case 0x4C:
                return Terminate(cpu.AL);
            case 0x4E:
                FindResult(cpu, _m.Disk.FindFirst(ReadAsciiz(cpu.DS, cpu.DX)));
                break;
            case 0x4F:
                FindResult(cpu, _m.Disk.FindNext());
                break;
            case 0x56:
                DosResult(cpu, _m.Disk.Rename(ReadAsciiz(cpu.DS, cpu.DX), ReadAsciiz(cpu.ES, cpu.DI)));
                break;
            case 0x62:
                cpu.BX = Machine.PspSegment;
                break;
            default:
                cpu.SetFlag(CpuFlags.CF, true);
                cpu.AX = 1; // invalid function
                break;
        }
        return InterruptResult.Handled;
    }

    private void FindResult(Cpu8086 cpu, FileInfo? file)
    {
        if (file == null)
        {
            DosResult(cpu, -18); // no more files
            return;
        }
        ushort s = DtaSegment, o = DtaOffset;
        var t = file.LastWriteTime;
        Mem.Write8(s, (ushort)(o + 0x15), 0x20);
        Mem.Write16(s, (ushort)(o + 0x16), (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2)));
        Mem.Write16(s, (ushort)(o + 0x18), (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day));
        Mem.Write16(s, (ushort)(o + 0x1A), (ushort)file.Length);
        Mem.Write16(s, (ushort)(o + 0x1C), (ushort)(file.Length >> 16));
        string name = file.Name.ToUpperInvariant();
        if (name.Length > 12) name = name[..12];
        for (int i = 0; i < 13; i++)
            Mem.Write8(s, (ushort)(o + 0x1E + i), i < name.Length ? (byte)name[i] : (byte)0);
        DosResult(cpu, 0);
    }

    /// <summary>Line editing shared by INT 21h/0Ah and reading from standard input.</summary>
    /// <returns>The finished line (without CR) or null while still waiting for Enter.</returns>
    private List<byte>? EditLine(int maxChars)
    {
        _line ??= new List<byte>();
        while (_m.Keyboard.TryRead(out ushort key))
        {
            byte ch = (byte)key;
            if (ch == 13)
            {
                var done = _line;
                _line = null;
                Output(13);
                return done;
            }
            if (ch == 8)
            {
                if (_line.Count == 0) continue;
                _line.RemoveAt(_line.Count - 1);
                Output(8);
                Output((byte)' ');
                Output(8);
            }
            else if (ch != 0)
            {
                if (_line.Count >= maxChars)
                {
                    Output(7);
                    continue;
                }
                _line.Add(ch);
                Output(ch);
            }
        }
        return null;
    }

    private InterruptResult BufferedInput(Cpu8086 cpu)
    {
        int max = Mem.Read8(cpu.DS, cpu.DX);
        if (max == 0) return InterruptResult.Handled;
        var line = EditLine(max - 1);
        if (line == null) return InterruptResult.Wait;
        Mem.Write8(cpu.DS, (ushort)(cpu.DX + 1), (byte)line.Count);
        for (int i = 0; i < line.Count; i++) Mem.Write8(cpu.DS, (ushort)(cpu.DX + 2 + i), line[i]);
        Mem.Write8(cpu.DS, (ushort)(cpu.DX + 2 + line.Count), 13);
        return InterruptResult.Handled;
    }

    private InterruptResult ReadHandle(Cpu8086 cpu)
    {
        if (cpu.BX == 0)
        {
            var line = EditLine(Math.Max(0, cpu.CX - 2));
            if (line == null) return InterruptResult.Wait;
            Output(10);
            line.Add(13);
            line.Add(10);
            int n = Math.Min(line.Count, cpu.CX);
            for (int i = 0; i < n; i++) Mem.Write8(cpu.DS, (ushort)(cpu.DX + i), line[i]);
            DosResult(cpu, n);
            return InterruptResult.Handled;
        }

        var buffer = new byte[cpu.CX];
        int read = _m.Disk.Read(cpu.BX, buffer);
        if (read > 0)
            for (int i = 0; i < read; i++) Mem.Write8(cpu.DS, (ushort)(cpu.DX + i), buffer[i]);
        DosResult(cpu, read);
        return InterruptResult.Handled;
    }

    private void WriteHandle(Cpu8086 cpu)
    {
        var data = new byte[cpu.CX];
        for (int i = 0; i < data.Length; i++) data[i] = Mem.Read8(cpu.DS, (ushort)(cpu.DX + i));
        switch (cpu.BX)
        {
            case 1:
            case 2:
                foreach (byte b in data) Output(b);
                DosResult(cpu, data.Length);
                break;
            case 4:
                foreach (byte b in data) _m.Print(b);
                DosResult(cpu, data.Length);
                break;
            case 0:
            case 3:
                DosResult(cpu, data.Length);
                break;
            default:
                DosResult(cpu, _m.Disk.Write(cpu.BX, data));
                break;
        }
    }

    #endregion
}
