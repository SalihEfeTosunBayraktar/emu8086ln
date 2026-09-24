using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Emu8086.App.Services;
using Emu8086.Core.Cpu;
using Emu8086.Core.Machine;
using Keyboard = System.Windows.Input.Keyboard;

namespace Emu8086.App.Controls;

/// <summary>
/// The emulated monitor: renders text mode (from B800:0000) or mode 13h (from A000:0000)
/// and forwards keyboard and mouse input to the machine while focused.
/// </summary>
public sealed class ScreenView : FrameworkElement
{
    private const int BlinkPeriodMs = 530;
    private static readonly Lazy<GlyphAtlas> Atlas = new(() => new GlyphAtlas());

    private static readonly Dictionary<Key, byte> ExtendedKeys = new()
    {
        [Key.Up] = 0x48, [Key.Down] = 0x50, [Key.Left] = 0x4B, [Key.Right] = 0x4D,
        [Key.Home] = 0x47, [Key.End] = 0x4F, [Key.PageUp] = 0x49, [Key.PageDown] = 0x51,
        [Key.Insert] = 0x52, [Key.Delete] = 0x53,
        [Key.F1] = 0x3B, [Key.F2] = 0x3C, [Key.F3] = 0x3D, [Key.F4] = 0x3E, [Key.F5] = 0x3F,
        [Key.F6] = 0x40, [Key.F7] = 0x41, [Key.F8] = 0x42, [Key.F9] = 0x43, [Key.F10] = 0x44,
    };

    private static readonly Dictionary<Key, (byte Ascii, byte Scan)> ControlKeys = new()
    {
        [Key.Enter] = (13, 0x1C), [Key.Back] = (8, 0x0E), [Key.Escape] = (27, 0x01), [Key.Tab] = (9, 0x0F),
    };

    private const string ScanRows = "1234567890-=|qwertyuiop[]|asdfghjkl;'`|\\zxcvbnm,./";
    private static readonly byte[] ScanRowStart = [0x02, 0x10, 0x1E, 0x2B];

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Encoding _cp437;
    private WriteableBitmap? _bitmap;
    private uint[] _pixels = [];
    private readonly byte[] _textBuffer = new byte[80 * 25 * 2];
    private readonly byte[] _graphicsBuffer = new byte[Video.GraphicsWidth * Video.GraphicsHeight];
    private long _lastMemoryVersion = -1;
    private bool _lastBlinkPhase;
    private int _lastLayout = -1;
    private Rect _screenRect;

    public ScreenView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _cp437 = Encoding.GetEncoding(437, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
        Cursor = Cursors.IBeam;
    }

    public EmulatorSession? Session { get; set; }

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(ScreenView), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Overlay text shown while the program waits for input and the screen is not focused.</summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>Redraws if video memory changed or the blink phase flipped. Called by the UI timer.</summary>
    public void Refresh()
    {
        var session = Session;
        if (session == null) return;
        bool blinkPhase = _clock.ElapsedMilliseconds / BlinkPeriodMs % 2 == 0;

        int columns, rows, cursorX, cursorY;
        bool cursorVisible, blinkEnabled, graphics;
        uint[] palette;
        lock (session.Sync)
        {
            var machine = session.Machine;
            var video = machine.Video;
            if (machine.Memory.Version == _lastMemoryVersion && blinkPhase == _lastBlinkPhase && video.LayoutVersion == _lastLayout)
                return;
            _lastMemoryVersion = machine.Memory.Version;
            _lastLayout = video.LayoutVersion;
            graphics = video.IsGraphics;
            columns = video.Columns;
            rows = video.Rows;
            cursorX = video.CursorX;
            cursorY = video.CursorY;
            cursorVisible = video.CursorVisible;
            blinkEnabled = video.BlinkEnabled;
            palette = video.Palette;
            if (graphics)
                machine.Memory.Span(Memory.Physical(Video.GraphicsSegment, 0), _graphicsBuffer.Length).CopyTo(_graphicsBuffer);
            machine.Memory.Span(Memory.Physical(Video.TextSegment, 0), _textBuffer.Length).CopyTo(_textBuffer);
        }
        _lastBlinkPhase = blinkPhase;

        if (graphics) RenderGraphics(palette);
        else RenderText(columns, rows, cursorX, cursorY, cursorVisible && IsKeyboardFocused, blinkEnabled, blinkPhase);
        InvalidateVisual();
    }

    /// <summary>The current screen as a PNG image, or null before anything was drawn.</summary>
    public byte[]? ToPng()
    {
        Invalidate();
        Refresh();
        if (_bitmap == null) return null;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_bitmap));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Forces a full redraw on the next refresh (e.g. after a new program is loaded).</summary>
    public void Invalidate() => _lastMemoryVersion = -1;

    private void EnsureBitmap(int width, int height)
    {
        if (_bitmap != null && _bitmap.PixelWidth == width && _bitmap.PixelHeight == height) return;
        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
        _pixels = new uint[width * height];
        InvalidateMeasure();
    }

    private void RenderText(int columns, int rows, int cursorX, int cursorY, bool showCursor, bool blinkEnabled, bool blinkPhase)
    {
        int cw = GlyphAtlas.CellWidth, ch = GlyphAtlas.CellHeight;
        int width = columns * cw, height = rows * ch;
        EnsureBitmap(width, height);
        var atlas = Atlas.Value;
        var colors = Video.TextPalette;

        for (int row = 0; row < rows; row++)
        for (int col = 0; col < columns; col++)
        {
            int cell = (row * columns + col) * 2;
            byte code = _textBuffer[cell];
            byte attr = _textBuffer[cell + 1];
            uint fg = colors[attr & 0x0F];
            uint bg = colors[blinkEnabled ? (attr >> 4) & 0x07 : attr >> 4];
            bool hidden = blinkEnabled && (attr & 0x80) != 0 && !blinkPhase;
            var mask = atlas[code];
            int baseIndex = row * ch * width + col * cw;
            for (int y = 0; y < ch; y++)
            {
                int p = baseIndex + y * width;
                int m = y * cw;
                for (int x = 0; x < cw; x++)
                    _pixels[p + x] = hidden ? bg : Blend(bg, fg, mask[m + x]);
            }
        }

        if (showCursor && blinkPhase && cursorX < columns && cursorY < rows)
        {
            byte attr = _textBuffer[(cursorY * columns + cursorX) * 2 + 1];
            uint color = colors[attr & 0x0F];
            int top = cursorY * ch + ch - 3;
            for (int y = top; y < top + 2; y++)
            for (int x = 0; x < cw; x++)
                _pixels[y * width + cursorX * cw + x] = color;
        }
        _bitmap!.WritePixels(new Int32Rect(0, 0, width, height), _pixels, width * 4, 0);
    }

    private void RenderGraphics(uint[] palette)
    {
        EnsureBitmap(Video.GraphicsWidth, Video.GraphicsHeight);
        for (int i = 0; i < _graphicsBuffer.Length; i++) _pixels[i] = palette[_graphicsBuffer[i]];
        _bitmap!.WritePixels(new Int32Rect(0, 0, Video.GraphicsWidth, Video.GraphicsHeight), _pixels, Video.GraphicsWidth * 4, 0);
    }

    private static uint Blend(uint bg, uint fg, byte alpha)
    {
        if (alpha == 0) return bg;
        if (alpha == 255) return fg;
        uint r = (((fg >> 16) & 0xFF) * alpha + ((bg >> 16) & 0xFF) * (255u - alpha)) / 255;
        uint g = (((fg >> 8) & 0xFF) * alpha + ((bg >> 8) & 0xFF) * (255u - alpha)) / 255;
        uint b = ((fg & 0xFF) * alpha + (bg & 0xFF) * (255u - alpha)) / 255;
        return (r << 16) | (g << 8) | b;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 500 : availableSize.Height;
        return new Size(w, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
        if (_bitmap == null) return;

        // Keep a 4:3 monitor aspect ratio.
        double aspect = 4.0 / 3.0;
        double w = RenderSize.Width, h = RenderSize.Height;
        if (w / h > aspect) w = h * aspect; else h = w / aspect;
        _screenRect = new Rect((RenderSize.Width - w) / 2, (RenderSize.Height - h) / 2, w, h);

        bool pixelArt = _bitmap.PixelWidth == Video.GraphicsWidth;
        RenderOptions.SetBitmapScalingMode(this, pixelArt ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        dc.DrawImage(_bitmap, _screenRect);

        if (!IsKeyboardFocused && !string.IsNullOrEmpty(Hint))
        {
            var text = new FormattedText(Hint, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var box = new Rect(_screenRect.Left + (_screenRect.Width - text.Width) / 2 - 12, _screenRect.Bottom - text.Height - 26,
                text.Width + 24, text.Height + 12);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(220, 40, 90, 200)), null, box, 6, 6);
            dc.DrawText(text, new Point(box.Left + 12, box.Top + 6));
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Invalidate();
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Invalidate();
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        UpdateMouse(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        UpdateMouse(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateMouse(e);
    }

    private void UpdateMouse(MouseEventArgs e)
    {
        var session = Session;
        if (session == null || _screenRect.Width <= 0) return;
        var p = e.GetPosition(this);
        double fx = Math.Clamp((p.X - _screenRect.Left) / _screenRect.Width, 0, 0.999);
        double fy = Math.Clamp((p.Y - _screenRect.Top) / _screenRect.Height, 0, 0.999);
        int buttons = (e.LeftButton == MouseButtonState.Pressed ? 1 : 0) | (e.RightButton == MouseButtonState.Pressed ? 2 : 0);
        lock (session.Sync)
        {
            var video = session.Machine.Video;
            var mouse = session.Machine.Mouse;
            if (video.IsGraphics)
            {
                mouse.X = (int)(fx * Video.GraphicsWidth) * 2; // INT 33h reports 640-wide coordinates in mode 13h
                mouse.Y = (int)(fy * Video.GraphicsHeight);
            }
            else
            {
                mouse.X = (int)(fx * video.Columns) * 8;
                mouse.Y = (int)(fy * video.Rows) * 8;
            }
            mouse.Buttons = buttons;
        }
    }

    private void UpdateShiftFlags()
    {
        if (Session == null) return;
        var m = Keyboard.Modifiers;
        byte flags = 0;
        if ((m & ModifierKeys.Shift) != 0) flags |= 0x02;
        if ((m & ModifierKeys.Control) != 0) flags |= 0x04;
        if ((m & ModifierKeys.Alt) != 0) flags |= 0x08;
        if (Keyboard.IsKeyToggled(Key.CapsLock)) flags |= 0x40;
        if (Keyboard.IsKeyToggled(Key.NumLock)) flags |= 0x20;
        Session.Machine.Keyboard.ShiftFlags = flags;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        UpdateShiftFlags();
        var keyboard = Session?.Machine.Keyboard;
        if (keyboard == null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (ControlKeys.TryGetValue(key, out var ctl))
        {
            keyboard.Push(ctl.Ascii, ctl.Scan);
            e.Handled = true;
        }
        else if (ExtendedKeys.TryGetValue(key, out byte scan))
        {
            keyboard.Push(0, scan);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && key is >= Key.A and <= Key.Z)
        {
            int letter = key - Key.A;
            keyboard.Push((byte)(letter + 1), ScanCode((char)('a' + letter)));
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        UpdateShiftFlags();
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        var keyboard = Session?.Machine.Keyboard;
        if (keyboard == null) return;
        foreach (char c in e.Text)
        {
            if (c < 32) continue; // control keys are handled in OnKeyDown
            byte ascii = _cp437.GetBytes(c.ToString())[0];
            keyboard.Push(ascii, ScanCode(char.ToLowerInvariant(c)));
        }
        e.Handled = true;
    }

    /// <summary>US-layout scan code for a character (0 if unknown).</summary>
    private static byte ScanCode(char c)
    {
        if (c == ' ') return 0x39;
        var rows = ScanRows.Split('|');
        for (int r = 0; r < rows.Length; r++)
        {
            int i = rows[r].IndexOf(c);
            if (i >= 0) return (byte)(ScanRowStart[r] + i);
        }
        return 0;
    }
}
