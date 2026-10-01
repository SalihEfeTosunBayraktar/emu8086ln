using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Emu8086.App.Services;

namespace Emu8086.App.Controls;

/// <summary>Value columns of the memory list view.</summary>
[Flags]
public enum MemoryColumns
{
    None = 0,
    Hex = 1,
    Decimal = 2,
    Binary = 4,
    Char = 8,
    All = Hex | Decimal | Binary | Char,
}

/// <summary>
/// Hex/ASCII dump of one 64 KB segment. The loaded program's bytes are coloured, the instruction
/// about to run is boxed and bytes (and bits) written by the last instruction get a background mark;
/// click a byte and type hex digits to edit it while the program is paused.
/// </summary>
public sealed class HexView : FrameworkElement
{
    public const int BytesPerRow = 16;
    private const double FontSize = 12.5;
    private const double LineHeight = 18;
    private const double LeftPadding = 8;
    /// <summary>Height of the column header (+0 .. +F) above the rows.</summary>
    private const double HeaderHeight = LineHeight + 2;

    private byte[] _data = [];
    private int _programStart;
    private int _programLength;
    /// <summary>Physical address -> value before the last instruction, for bytes it changed.</summary>
    private Dictionary<int, byte> _changed = new();
    private int _execStart = -1;
    private int _execLength;
    private int _lastExecStart = -1;
    private object? _lastRecord;
    private int _rows;
    private int _selected = -1;
    private int _pendingNibble = -1;
    private long _lastVersion = -1;
    private Typeface? _typeface;
    private double _charWidth;

    public HexView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
    }

    public EmulatorSession? Session { get; set; }

    public static readonly DependencyProperty SegmentProperty = DependencyProperty.Register(nameof(Segment), typeof(int), typeof(HexView),
        new FrameworkPropertyMetadata(0x0700, (d, _) => ((HexView)d).ForceRefresh()));

    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(nameof(Offset), typeof(int), typeof(HexView),
        new FrameworkPropertyMetadata(0x0100, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HexView)d).ForceRefresh(),
            (d, v) => ((HexView)d).CoerceOffset((int)v)));

    public static readonly DependencyProperty ListModeProperty = DependencyProperty.Register(nameof(ListMode), typeof(bool), typeof(HexView),
        new FrameworkPropertyMetadata(false, (d, _) =>
        {
            d.CoerceValue(OffsetProperty);
            ((HexView)d).ForceRefresh();
        }));

    /// <summary>One byte per row (address, hex, decimal, binary, character) instead of 16 per row.</summary>
    public bool ListMode
    {
        get => (bool)GetValue(ListModeProperty);
        set => SetValue(ListModeProperty, value);
    }

    private int RowBytes => ListMode ? 1 : BytesPerRow;

    private int CoerceOffset(int value) => Math.Clamp(value & ~(RowBytes - 1), 0, 0x10000 - RowBytes);

    public int Segment
    {
        get => (int)GetValue(SegmentProperty);
        set => SetValue(SegmentProperty, value);
    }

    /// <summary>Offset of the first visible row (multiple of 16 in grid mode).</summary>
    public int Offset
    {
        get => (int)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public int VisibleRows => Math.Max(1, (int)((ActualHeight - HeaderHeight) / LineHeight));

    private void ForceRefresh()
    {
        _lastVersion = -1;
        Refresh();
    }

    public void Refresh()
    {
        var session = Session;
        if (session == null || !IsVisible) return;
        _rows = VisibleRows;
        int length = _rows * RowBytes;
        lock (session.Sync)
        {
            var memory = session.Machine.Memory;
            var record = session.Machine.History.Last;
            var cpu = session.Machine.Cpu;
            int execStart = session.State == SessionState.Empty ? -1 : Emu8086.Core.Cpu.Memory.Physical(cpu.CS, cpu.IP);
            if (memory.Version == _lastVersion && _data.Length == length && ReferenceEquals(record, _lastRecord) && execStart == _lastExecStart) return;
            _lastExecStart = execStart;
            _execStart = execStart;
            _execLength = execStart < 0 ? 0 : new Emu8086.Core.Disassembler.Disassembler8086(memory).Decode(cpu.CS, cpu.IP).Bytes.Length;
            _lastVersion = memory.Version;
            _lastRecord = record;
            var data = new byte[length];
            for (int i = 0; i < length; i++) data[i] = memory.Read8((ushort)Segment, (ushort)(Offset + i));
            _data = data;
            _programStart = session.Machine.ProgramStart;
            _programLength = session.Machine.Program?.Bytes.Length ?? 0;
            // Physical addresses whose value the last instruction really changed.
            _changed = new();
            // A byte may be written twice by one instruction; the first old value is the one before it.
            foreach (var w in record?.MemoryWrites ?? [])
                if (!_changed.ContainsKey(w.Address)) _changed[w.Address] = w.OldValue;
            foreach (var same in _changed.Where(c => memory.Peek(c.Key) == c.Value).Select(c => c.Key).ToList()) _changed.Remove(same);
        }
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ForceRefresh();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        _typeface ??= new Typeface((FontFamily)FindResource("Font.Mono"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var normal = Res("Fg.Primary");
        var muted = Res("Fg.Muted");
        var changed = Res("Changed");
        var selection = Res("Bg.Selected");
        var mark = Res("ChangedMark");
        var box = new Pen(Res("ExecLine.Border"), 1.5);

        FormattedText Text(string s, Brush b) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, FontSize, b, dpi);
        _charWidth = Text("0", normal).WidthIncludingTrailingWhitespace;

        if (ListMode)
        {
            RenderList(dc, Text, normal, muted, changed, selection, mark, box);
            return;
        }

        // Column header: the low digit to add to the row address, e.g. row 0100 + column 5 = 0105.
        for (int i = 0; i < BytesPerRow; i++)
        {
            bool selectedColumn = _selected >= 0 && _selected % BytesPerRow == i;
            dc.DrawText(Text($"+{i:X}", selectedColumn ? normal : muted), new Point(HexColumnX(i), 2));
        }
        if (_selected >= 0)
            dc.DrawText(Text($"{Segment:X4}:{(Offset + _selected) & 0xFFFF:X4}", normal), new Point(AsciiColumnX(0), 2));

        for (int row = 0; row < _rows && row * BytesPerRow < _data.Length; row++)
        {
            double y = HeaderHeight + row * LineHeight + 2;
            int rowOffset = Offset + row * BytesPerRow;
            dc.DrawText(Text($"{Segment:X4}:{rowOffset:X4}", muted), new Point(LeftPadding, y));

            for (int i = 0; i < BytesPerRow; i++)
            {
                int index = row * BytesPerRow + i;
                byte b = _data[index];
                double x = HexColumnX(i);
                if (index == _selected)
                    dc.DrawRoundedRectangle(selection, null, new Rect(x - 2, y - 1, _charWidth * 2 + 4, LineHeight - 2), 3, 3);
                int physical = Emu8086.Core.Cpu.Memory.Physical((ushort)Segment, (ushort)(rowOffset + i));
                bool isProgram = physical >= _programStart && physical < _programStart + _programLength;
                if (IsExecuting(physical)) dc.DrawRectangle(null, box, new Rect(x - 2, y - 1, _charWidth * 2 + 4, LineHeight - 2));
                if (_changed.ContainsKey(physical))
                {
                    dc.DrawRoundedRectangle(mark, null, new Rect(x - 2, y - 1, _charWidth * 2 + 4, LineHeight - 2), 3, 3);
                    dc.DrawRoundedRectangle(mark, null, new Rect(AsciiColumnX(i), y - 1, _charWidth, LineHeight - 2), 2, 2);
                }
                dc.DrawText(Text(b.ToString("X2"), isProgram ? changed : normal), new Point(x, y));
                char c = b is >= 32 and < 127 ? (char)b : '.';
                dc.DrawText(Text(c.ToString(), isProgram ? changed : muted), new Point(AsciiColumnX(i), y));
            }
        }
    }

    /// <summary>
    /// One address per row. Addresses between the 16-byte boundaries are indented, so the
    /// boundaries (0100, 0110, ...) stand out like headings. Only the chosen value columns are drawn.
    /// </summary>
    private void RenderList(DrawingContext dc, Func<string, Brush, FormattedText> text,
        Brush normal, Brush muted, Brush changed, Brush selection, Brush mark, Pen box)
    {
        // Lay the visible columns out left to right.
        var columns = new List<(MemoryColumns Kind, int X)>();
        int next = ListValueColumn;
        foreach (var (kind, _, width) in ListColumnSpecs)
        {
            if (!Columns.HasFlag(kind)) continue;
            columns.Add((kind, next));
            next += width + ListColumnGap;
        }
        double rowWidth = _charWidth * next;

        var loc = Loc.Instance;
        dc.DrawText(text(loc["memory.col.address"], muted), new Point(ListX(0), 2));
        foreach (var (kind, x) in columns)
            dc.DrawText(text(loc[ListColumnSpecs.First(c => c.Kind == kind).TitleKey], muted), new Point(ListX(x), 2));

        int firstExecRow = -1, lastExecRow = -1;
        for (int row = 0; row < _rows && row < _data.Length; row++)
        {
            double y = HeaderHeight + row * LineHeight + 2;
            int offset = (Offset + row) & 0xFFFF;
            byte b = _data[row];
            bool boundary = offset % BytesPerRow == 0;
            int physical = Emu8086.Core.Cpu.Memory.Physical((ushort)Segment, (ushort)offset);
            bool isProgram = physical >= _programStart && physical < _programStart + _programLength;
            var value = isProgram ? changed : normal;
            bool wasChanged = _changed.TryGetValue(physical, out byte old);

            if (row == _selected) dc.DrawRoundedRectangle(selection, null, new Rect(LeftPadding - 2, y - 1, rowWidth, LineHeight - 2), 3, 3);
            if (IsExecuting(physical))
            {
                if (firstExecRow < 0) firstExecRow = row;
                lastExecRow = row;
            }
            dc.DrawText(text($"{Segment:X4}:{offset:X4}", boundary ? normal : muted), new Point(ListX(boundary ? 0 : ListIndent), y));

            foreach (var (kind, x) in columns)
            {
                string cell = kind switch
                {
                    MemoryColumns.Hex => b.ToString("X2"),
                    MemoryColumns.Decimal => b.ToString().PadLeft(3),
                    MemoryColumns.Binary => Convert.ToString(b, 2).PadLeft(8, '0'),
                    _ => (b is >= 32 and < 127 ? (char)b : '.').ToString(),
                };
                if (wasChanged && kind == MemoryColumns.Binary)
                {
                    // Mark exactly the bits that flipped.
                    for (int bit = 0; bit < 8; bit++)
                        if ((((old ^ b) >> (7 - bit)) & 1) != 0)
                            dc.DrawRectangle(mark, null, new Rect(ListX(x + bit), y - 1, _charWidth, LineHeight - 2));
                }
                else if (wasChanged)
                {
                    dc.DrawRoundedRectangle(mark, null, new Rect(ListX(x) - 2, y - 1, _charWidth * cell.Length + 4, LineHeight - 2), 3, 3);
                }
                dc.DrawText(text(cell, kind == MemoryColumns.Char && !isProgram ? muted : value), new Point(ListX(x), y));
            }
        }
        // One box around all rows of the instruction that runs next.
        if (firstExecRow >= 0)
            dc.DrawRectangle(null, box, new Rect(LeftPadding - 3, HeaderHeight + firstExecRow * LineHeight + 1,
                rowWidth, (lastExecRow - firstExecRow + 1) * LineHeight));
    }

    private bool IsExecuting(int physical) => _execStart >= 0 && physical >= _execStart && physical < _execStart + _execLength;

    private const int ListIndent = 2;
    private const int ListValueColumn = 14;
    private const int ListColumnGap = 3;

    /// <summary>Value columns of the list view in display order, with their title key and width in characters.</summary>
    private static readonly (MemoryColumns Kind, string TitleKey, int Width)[] ListColumnSpecs =
    [
        (MemoryColumns.Hex, "memory.col.hex", 3),
        (MemoryColumns.Decimal, "memory.col.decimal", 3),
        (MemoryColumns.Binary, "memory.col.binary", 8),
        (MemoryColumns.Char, "memory.col.char", 3),
    ];

    private MemoryColumns _columns = MemoryColumns.All;

    /// <summary>Which value columns the list view shows.</summary>
    public MemoryColumns Columns
    {
        get => _columns;
        set
        {
            _columns = value;
            InvalidateVisual();
        }
    }

    private double ListX(int column) => LeftPadding + _charWidth * column;

    private double HexColumnX(int i) => LeftPadding + _charWidth * (11 + i * 3 + (i >= 8 ? 1 : 0));
    private double AsciiColumnX(int i) => LeftPadding + _charWidth * (11 + BytesPerRow * 3 + 2 + i);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Offset -= Math.Sign(e.Delta) * 3 * RowBytes;
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var p = e.GetPosition(this);
        int row = (int)((p.Y - HeaderHeight - 2) / LineHeight);
        if (p.Y < HeaderHeight) row = -1;
        _selected = -1;
        _pendingNibble = -1;
        if (ListMode && row >= 0 && row < _data.Length) _selected = row;
        for (int i = 0; i < BytesPerRow && _charWidth > 0 && !ListMode; i++)
        {
            double x = HexColumnX(i);
            if (row >= 0 && p.X >= x - 2 && p.X <= x + _charWidth * 2 + 2) _selected = row * BytesPerRow + i;
        }
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.PageDown: Offset += VisibleRows * RowBytes; e.Handled = true; return;
            case Key.PageUp: Offset -= VisibleRows * RowBytes; e.Handled = true; return;
            case Key.Down: Offset += RowBytes; e.Handled = true; return;
            case Key.Up: Offset -= RowBytes; e.Handled = true; return;
        }
        int digit = e.Key switch
        {
            >= Key.D0 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            >= Key.A and <= Key.F => e.Key - Key.A + 10,
            _ => -1,
        };
        if (digit < 0 || _selected < 0 || Session == null || Session.IsBusy) return;

        if (_pendingNibble < 0)
        {
            _pendingNibble = digit;
        }
        else
        {
            byte value = (byte)((_pendingNibble << 4) | digit);
            lock (Session.Sync) Session.Machine.Memory.Write8((ushort)Segment, (ushort)(Offset + _selected), value);
            _pendingNibble = -1;
            _selected = Math.Min(_selected + 1, _data.Length - 1);
            Refresh();
        }
        e.Handled = true;
    }
}
