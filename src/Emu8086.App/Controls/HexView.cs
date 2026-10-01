using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Emu8086.App.Services;

namespace Emu8086.App.Controls;

/// <summary>
/// Hex/ASCII dump of one 64 KB segment. The loaded program's bytes are coloured and bytes written
/// by the last executed instruction get a background mark;
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
    private HashSet<int> _changed = new();
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
            if (memory.Version == _lastVersion && _data.Length == length && ReferenceEquals(record, _lastRecord)) return;
            _lastVersion = memory.Version;
            _lastRecord = record;
            var data = new byte[length];
            for (int i = 0; i < length; i++) data[i] = memory.Read8((ushort)Segment, (ushort)(Offset + i));
            _data = data;
            _programStart = session.Machine.ProgramStart;
            _programLength = session.Machine.Program?.Bytes.Length ?? 0;
            // Physical addresses whose value the last instruction really changed.
            _changed = record == null ? new() : record.MemoryWrites.Where(w => w.OldValue != w.NewValue).Select(w => w.Address).ToHashSet();
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

        FormattedText Text(string s, Brush b) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, FontSize, b, dpi);
        _charWidth = Text("0", normal).WidthIncludingTrailingWhitespace;

        if (ListMode)
        {
            RenderList(dc, Text, normal, muted, changed, selection, mark);
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
                if (_changed.Contains(physical))
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
    /// boundaries (0100, 0110, ...) stand out like headings.
    /// </summary>
    private void RenderList(DrawingContext dc, Func<string, Brush, FormattedText> text,
        Brush normal, Brush muted, Brush changed, Brush selection, Brush mark)
    {
        for (int row = 0; row < _rows && row < _data.Length; row++)
        {
            double y = HeaderHeight + row * LineHeight + 2;
            int offset = (Offset + row) & 0xFFFF;
            byte b = _data[row];
            bool boundary = offset % BytesPerRow == 0;
            int physical = Emu8086.Core.Cpu.Memory.Physical((ushort)Segment, (ushort)offset);
            bool isProgram = physical >= _programStart && physical < _programStart + _programLength;
            var value = isProgram ? changed : normal;
            double width = _charWidth * ListColumns;

            if (row == _selected) dc.DrawRoundedRectangle(selection, null, new Rect(LeftPadding - 2, y - 1, width, LineHeight - 2), 3, 3);
            if (_changed.Contains(physical)) dc.DrawRoundedRectangle(mark, null, new Rect(ListX(ListHexColumn) - 2, y - 1, _charWidth * 26, LineHeight - 2), 3, 3);

            dc.DrawText(text($"{Segment:X4}:{offset:X4}", boundary ? normal : muted), new Point(ListX(boundary ? 0 : ListIndent), y));
            dc.DrawText(text(b.ToString("X2"), value), new Point(ListX(ListHexColumn), y));
            dc.DrawText(text(b.ToString().PadLeft(3), value), new Point(ListX(ListHexColumn + 5), y));
            dc.DrawText(text(Convert.ToString(b, 2).PadLeft(8, '0'), value), new Point(ListX(ListHexColumn + 11), y));
            char c = b is >= 32 and < 127 ? (char)b : '.';
            dc.DrawText(text(c.ToString(), isProgram ? changed : muted), new Point(ListX(ListHexColumn + 22), y));
        }
    }

    private const int ListIndent = 2;
    private const int ListHexColumn = 14;
    private const int ListColumns = 40;

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
