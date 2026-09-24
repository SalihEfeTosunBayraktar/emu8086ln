using Emu8086.Core.Cpu;

namespace Emu8086.Core.Machine;

/// <summary>
/// Video adapter state. Text is stored in video memory at B800:0000 and the cursor in the
/// BIOS data area, so programs that access either directly see consistent values.
/// </summary>
public sealed class Video
{
    public const ushort TextSegment = 0xB800;
    public const ushort GraphicsSegment = 0xA000;
    public const int GraphicsWidth = 320;
    public const int GraphicsHeight = 200;
    public const byte DefaultAttribute = 0x07;

    private const int BdaMode = 0x449;
    private const int BdaColumns = 0x44A;
    private const int BdaCursor = 0x450;
    private const int BdaPage = 0x462;
    private const int BdaRows = 0x484;

    private readonly Memory _mem;

    public Video(Memory memory)
    {
        _mem = memory;
        Palette = BuildDefaultPalette();
    }

    public int Mode => _mem.Read8(BdaMode);
    public int Columns => Math.Max(1, (int)_mem.Read16(BdaColumns));
    public int Rows => _mem.Read8(BdaRows) + 1;
    public bool IsGraphics => Mode == 0x13;
    public int ActivePage => _mem.Read8(BdaPage);

    public int CursorX
    {
        get => _mem.Read8(BdaCursor);
        set => _mem.Write8(BdaCursor, (byte)value);
    }

    public int CursorY
    {
        get => _mem.Read8(BdaCursor + 1);
        set => _mem.Write8(BdaCursor + 1, (byte)value);
    }

    public bool CursorVisible { get; set; } = true;
    /// <summary>When false, attribute bit 7 selects a bright background instead of blinking.</summary>
    public bool BlinkEnabled { get; set; } = true;
    /// <summary>256-entry RGB palette (0x00RRGGBB) for mode 13h.</summary>
    public uint[] Palette { get; }

    /// <summary>Incremented when the mode or palette changes so views can rebuild.</summary>
    public int LayoutVersion { get; private set; }

    public void SetMode(int mode, bool clear = true)
    {
        int columns = mode switch
        {
            0 or 1 => 40,
            0x13 => 40,
            _ => 80,
        };
        if (mode is not (0 or 1 or 2 or 3 or 7 or 0x13)) mode = 3;
        _mem.Write8(BdaMode, (byte)mode);
        _mem.Write16(BdaColumns, (ushort)columns);
        _mem.Write8(BdaRows, 24);
        _mem.Write8(BdaPage, 0);
        CursorX = 0;
        CursorY = 0;
        CursorVisible = true;
        if (clear)
        {
            ClearText();
            if (mode == 0x13)
                for (int i = 0; i < GraphicsWidth * GraphicsHeight; i++) _mem.Write8(Memory.Physical(GraphicsSegment, 0) + i, 0);
        }
        LayoutVersion++;
    }

    private void ClearText()
    {
        for (int i = 0; i < 80 * 25; i++) WriteCell(i % 80, i / 80, ' ', DefaultAttribute, 80);
    }

    private int CellAddress(int x, int y, int columns) => Memory.Physical(TextSegment, 0) + (y * columns + x) * 2;

    private void WriteCell(int x, int y, char ch, byte attr, int columns)
    {
        int a = CellAddress(x, y, columns);
        _mem.Write8(a, (byte)ch);
        _mem.Write8(a + 1, attr);
    }

    public (byte Char, byte Attr) ReadCell(int x, int y)
    {
        int a = CellAddress(x, y, Columns);
        return (_mem.Read8(a), _mem.Read8(a + 1));
    }

    public void WriteCell(int x, int y, byte ch, byte attr)
    {
        if (x < 0 || y < 0 || x >= Columns || y >= Rows) return;
        WriteCell(x, y, (char)ch, attr, Columns);
    }

    /// <summary>INT 10h/06h and 07h.</summary>
    public void Scroll(bool up, int lines, byte attr, int top, int left, int bottom, int right)
    {
        right = Math.Min(right, Columns - 1);
        bottom = Math.Min(bottom, Rows - 1);
        if (top > bottom || left > right) return;
        int height = bottom - top + 1;
        if (lines == 0 || lines > height) lines = height;

        if (up)
        {
            for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                int src = y + lines;
                var cell = src <= bottom ? ReadCell(x, src) : ((byte)' ', attr);
                WriteCell(x, y, cell.Item1, cell.Item2);
            }
        }
        else
        {
            for (int y = bottom; y >= top; y--)
            for (int x = left; x <= right; x++)
            {
                int src = y - lines;
                var cell = src >= top ? ReadCell(x, src) : ((byte)' ', attr);
                WriteCell(x, y, cell.Item1, cell.Item2);
            }
        }
    }

    /// <summary>Teletype output (INT 10h/0Eh), handling BEL, BS, TAB, LF and CR.</summary>
    public void Teletype(byte ch, byte? attr = null)
    {
        int x = CursorX, y = CursorY;
        switch (ch)
        {
            case 7:
                Bell?.Invoke();
                return;
            case 8:
                if (x > 0) x--;
                break;
            case 9:
                x = (x / 8 + 1) * 8;
                break;
            case 10:
                y++;
                break;
            case 13:
                x = 0;
                break;
            default:
                var current = ReadCell(x, y);
                WriteCell(x, y, ch, attr ?? (current.Attr == 0 ? DefaultAttribute : current.Attr));
                x++;
                break;
        }
        if (x >= Columns)
        {
            x = 0;
            y++;
        }
        if (y >= Rows)
        {
            Scroll(true, 1, DefaultAttribute, 0, 0, Rows - 1, Columns - 1);
            y = Rows - 1;
        }
        CursorX = x;
        CursorY = y;
    }

    public event Action? Bell;

    public void PutPixel(int x, int y, byte color)
    {
        if (!IsGraphics || x < 0 || y < 0 || x >= GraphicsWidth || y >= GraphicsHeight) return;
        int a = Memory.Physical(GraphicsSegment, 0) + y * GraphicsWidth + x;
        // Bit 7 set XORs the pixel, as the real BIOS does.
        _mem.Write8(a, (color & 0x80) != 0 ? (byte)(_mem.Read8(a) ^ (color & 0x7F)) : color);
    }

    public byte GetPixel(int x, int y)
    {
        if (!IsGraphics || x < 0 || y < 0 || x >= GraphicsWidth || y >= GraphicsHeight) return 0;
        return _mem.Read8(Memory.Physical(GraphicsSegment, 0) + y * GraphicsWidth + x);
    }

    public void SetPaletteEntry(int index, int r6, int g6, int b6)
    {
        Palette[index & 0xFF] = (uint)((Scale6(r6) << 16) | (Scale6(g6) << 8) | Scale6(b6));
        LayoutVersion++;
    }

    private static int Scale6(int v) => (v & 0x3F) * 255 / 63;

    /// <summary>Returns the visible text, one line per row (for tests and copy-to-clipboard).</summary>
    public string ReadText()
    {
        var lines = new List<string>();
        for (int y = 0; y < Rows; y++)
        {
            var chars = new char[Columns];
            for (int x = 0; x < Columns; x++)
            {
                byte c = ReadCell(x, y).Char;
                chars[x] = c < 32 ? ' ' : (char)c;
            }
            lines.Add(new string(chars).TrimEnd());
        }
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    /// <summary>16 CGA colours in 0x00RRGGBB.</summary>
    public static readonly uint[] TextPalette =
    [
        0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xAA5500, 0xAAAAAA,
        0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF,
    ];

    /// <summary>Standard VGA mode 13h palette: 16 CGA colours, 16 greys, then 9 hue/saturation/value rings.</summary>
    private static uint[] BuildDefaultPalette()
    {
        var p = new uint[256];
        TextPalette.CopyTo(p, 0);
        int[] greys = [0, 5, 8, 11, 14, 17, 20, 24, 28, 32, 36, 40, 45, 50, 56, 63];
        for (int i = 0; i < 16; i++)
        {
            int g = Scale6(greys[i]);
            p[16 + i] = (uint)((g << 16) | (g << 8) | g);
        }

        // Each ring: 24 hues interpolated between a high and low component level.
        (int Hi, int Lo)[] rings =
        [
            (63, 0), (63, 31), (63, 45),
            (28, 0), (28, 14), (28, 20),
            (16, 0), (16, 8), (16, 11),
        ];
        int index = 32;
        foreach (var (hi, lo) in rings)
        {
            int[] steps = [lo, lo + (hi - lo) / 4, lo + (hi - lo) / 2, lo + 3 * (hi - lo) / 4, hi];
            for (int h = 0; h < 24; h++)
            {
                int seg = h / 4, pos = h % 4;
                int up = steps[pos], down = steps[4 - pos];
                (int r, int g, int b) = seg switch
                {
                    0 => (up, lo, hi),
                    1 => (hi, lo, down),
                    2 => (hi, up, lo),
                    3 => (down, hi, lo),
                    4 => (lo, hi, up),
                    _ => (lo, down, hi),
                };
                if (index < 248) p[index++] = (uint)((Scale6(r) << 16) | (Scale6(g) << 8) | Scale6(b));
            }
        }
        return p;
    }
}
