using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Emu8086.App.Services;

namespace Emu8086.App.Controls;

/// <summary>
/// Hex/ASCII dump of one 64 KB segment. Bytes changed since the previous refresh are highlighted;
/// click a byte and type hex digits to edit it while the program is paused.
/// </summary>
public sealed class HexView : FrameworkElement
{
    public const int BytesPerRow = 16;
    private const double FontSize = 12.5;
    private const double LineHeight = 18;
    private const double LeftPadding = 8;

    private byte[] _data = [];
    private byte[] _previous = [];
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
            (_, v) => Math.Clamp((int)v & ~(BytesPerRow - 1), 0, 0x10000 - BytesPerRow)));

    public int Segment
    {
        get => (int)GetValue(SegmentProperty);
        set => SetValue(SegmentProperty, value);
    }

    /// <summary>Offset of the first visible row (multiple of 16).</summary>
    public int Offset
    {
        get => (int)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public int VisibleRows => Math.Max(1, (int)(ActualHeight / LineHeight));

    private void ForceRefresh()
    {
        _lastVersion = -1;
        _previous = [];
        Refresh();
    }

    public void Refresh()
    {
        var session = Session;
        if (session == null || !IsVisible) return;
        _rows = VisibleRows;
        int length = _rows * BytesPerRow;
        lock (session.Sync)
        {
            var memory = session.Machine.Memory;
            if (memory.Version == _lastVersion && _data.Length == length) return;
            _lastVersion = memory.Version;
            var data = new byte[length];
            for (int i = 0; i < length; i++) data[i] = memory.Read8((ushort)Segment, (ushort)(Offset + i));
            _previous = _data.Length == length ? _data : data;
            _data = data;
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

        FormattedText Text(string s, Brush b) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, FontSize, b, dpi);
        _charWidth = Text("0", normal).WidthIncludingTrailingWhitespace;

        for (int row = 0; row < _rows && row * BytesPerRow < _data.Length; row++)
        {
            double y = row * LineHeight + 2;
            int rowOffset = Offset + row * BytesPerRow;
            dc.DrawText(Text($"{Segment:X4}:{rowOffset:X4}", muted), new Point(LeftPadding, y));

            for (int i = 0; i < BytesPerRow; i++)
            {
                int index = row * BytesPerRow + i;
                byte b = _data[index];
                double x = HexColumnX(i);
                if (index == _selected)
                    dc.DrawRoundedRectangle(selection, null, new Rect(x - 2, y - 1, _charWidth * 2 + 4, LineHeight - 2), 3, 3);
                bool isChanged = _previous.Length == _data.Length && _previous[index] != b;
                dc.DrawText(Text(b.ToString("X2"), isChanged ? changed : normal), new Point(x, y));
                char c = b is >= 32 and < 127 ? (char)b : '.';
                dc.DrawText(Text(c.ToString(), isChanged ? changed : muted), new Point(AsciiColumnX(i), y));
            }
        }
    }

    private double HexColumnX(int i) => LeftPadding + _charWidth * (11 + i * 3 + (i >= 8 ? 1 : 0));
    private double AsciiColumnX(int i) => LeftPadding + _charWidth * (11 + BytesPerRow * 3 + 2 + i);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Offset -= Math.Sign(e.Delta) * 3 * BytesPerRow;
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var p = e.GetPosition(this);
        int row = (int)((p.Y - 2) / LineHeight);
        _selected = -1;
        _pendingNibble = -1;
        for (int i = 0; i < BytesPerRow && _charWidth > 0; i++)
        {
            double x = HexColumnX(i);
            if (p.X >= x - 2 && p.X <= x + _charWidth * 2 + 2) _selected = row * BytesPerRow + i;
        }
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.PageDown: Offset += VisibleRows * BytesPerRow; e.Handled = true; return;
            case Key.PageUp: Offset -= VisibleRows * BytesPerRow; e.Handled = true; return;
            case Key.Down: Offset += BytesPerRow; e.Handled = true; return;
            case Key.Up: Offset -= BytesPerRow; e.Handled = true; return;
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
