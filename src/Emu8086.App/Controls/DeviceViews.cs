using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Emu8086.App.Services;
using Emu8086.Core.Devices;

namespace Emu8086.App.Controls;

/// <summary>Base for device renderings: redraws only when the device's version changes.</summary>
public abstract class DeviceView<T> : FrameworkElement where T : DeviceBase
{
    private int _lastVersion = -1;

    protected DeviceView()
    {
        ThemeService.ThemeChanged += InvalidateVisual;
    }

    public T? Device { get; set; }

    public void Refresh()
    {
        if (Device == null || Device.Version == _lastVersion || !IsVisible) return;
        _lastVersion = Device.Version;
        InvalidateVisual();
    }

    protected static Brush Res(string key) => ThemeService.Resource<Brush>(key);

    protected FormattedText Text(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Device != null) Draw(dc);
    }

    protected abstract void Draw(DrawingContext dc);
}

/// <summary>Four traffic lights around a crossroads.</summary>
public sealed class TrafficLightsView : DeviceView<TrafficLights>
{
    private static readonly Color[] LampColors = [Color.FromRgb(0xF2, 0x4B, 0x4B), Color.FromRgb(0xF5, 0xC2, 0x3D), Color.FromRgb(0x3D, 0xD6, 0x7C)];

    public TrafficLightsView()
    {
        Height = 190;
    }

    protected override void Draw(DrawingContext dc)
    {
        double cx = RenderSize.Width / 2, cy = RenderSize.Height / 2;
        var road = new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x44));
        dc.DrawRectangle(road, null, new Rect(cx - 28, 0, 56, RenderSize.Height));
        dc.DrawRectangle(road, null, new Rect(0, cy - 28, RenderSize.Width, 56));
        var dash = new Pen(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), 1.5) { DashStyle = new DashStyle([4, 4], 0) };
        dc.DrawLine(dash, new Point(cx, 0), new Point(cx, cy - 30));
        dc.DrawLine(dash, new Point(cx, cy + 30), new Point(cx, RenderSize.Height));
        dc.DrawLine(dash, new Point(0, cy), new Point(cx - 30, cy));
        dc.DrawLine(dash, new Point(cx + 30, cy), new Point(RenderSize.Width, cy));

        Point[] positions = [new(cx - 80, cy - 78), new(cx + 50, cy - 78), new(cx + 50, cy + 20), new(cx - 80, cy + 20)];
        for (int light = 0; light < 4; light++)
        {
            var p = positions[light];
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x18, 0x19, 0x1C)), new Pen(Res("Border.Strong"), 1),
                new Rect(p.X, p.Y, 30, 58), 6, 6);
            for (int lamp = 0; lamp < 3; lamp++)
            {
                bool on = Device!.IsOn(light, lamp);
                var color = LampColors[lamp];
                var brush = on ? new SolidColorBrush(color) : new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B));
                dc.DrawEllipse(brush, null, new Point(p.X + 15, p.Y + 10 + lamp * 19), 7, 7);
            }
            dc.DrawText(Text((light + 1).ToString(), 10, Res("Fg.Muted")), new Point(p.X + 11, p.Y + 60));
        }
    }
}

/// <summary>Motor shaft with its current angle and the three coil states.</summary>
public sealed class StepperMotorView : DeviceView<StepperMotor>
{
    public StepperMotorView()
    {
        Height = 150;
    }

    protected override void Draw(DrawingContext dc)
    {
        double cx = 80, cy = RenderSize.Height / 2, r = 55;
        dc.DrawEllipse(Res("Bg.Panel2"), new Pen(Res("Border.Strong"), 2), new Point(cx, cy), r, r);
        double angle = (Device!.Angle - 90) * Math.PI / 180;
        dc.DrawLine(new Pen(Res("Accent"), 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            new Point(cx, cy), new Point(cx + Math.Cos(angle) * (r - 10), cy + Math.Sin(angle) * (r - 10)));
        dc.DrawEllipse(Res("Fg.Primary"), null, new Point(cx, cy), 6, 6);
        dc.DrawText(Text($"{Device.Angle:0.0}°", 20, Res("Fg.Primary")), new Point(160, cy - 30));
        int coils = Device.Coils;
        for (int i = 0; i < 3; i++)
        {
            bool on = (coils >> i & 1) != 0;
            dc.DrawRoundedRectangle(on ? Res("Success") : Res("Bg.Panel2"), new Pen(Res("Border.Strong"), 1),
                new Rect(160 + i * 34, cy + 6, 28, 22), 4, 4);
            dc.DrawText(Text($"b{i}", 11, on ? Brushes.White : Res("Fg.Muted")), new Point(167 + i * 34, cy + 9));
        }
    }
}

/// <summary>Seven-segment display for the value written to port 199.</summary>
public sealed class LedDisplayView : DeviceView<LedDisplay>
{
    // Segments a..g as line segments on a 10x20 digit cell.
    private static readonly (double X1, double Y1, double X2, double Y2)[] Segments =
        [(1, 0, 9, 0), (10, 1, 10, 9), (10, 11, 10, 19), (1, 20, 9, 20), (0, 11, 0, 19), (0, 1, 0, 9), (1, 10, 9, 10)];

    private static readonly int[] DigitMasks = [0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F];
    private const int Digits = 6;

    public LedDisplayView()
    {
        Height = 90;
    }

    protected override void Draw(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x13)), null, new Rect(0, 0, RenderSize.Width, RenderSize.Height), 8, 8);
        int value = Device!.Value;
        string text = value.ToString(CultureInfo.InvariantCulture).PadLeft(Digits);
        var on = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D)), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var off = new Pen(new SolidColorBrush(Color.FromArgb(28, 0xFF, 0x4D, 0x4D)), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        double scale = 2.2, left = 18, top = 22;
        for (int d = 0; d < Digits; d++)
        {
            char c = text[d];
            int mask = c == '-' ? 0x40 : char.IsDigit(c) ? DigitMasks[c - '0'] : 0;
            double ox = left + d * 16 * scale;
            for (int s = 0; s < 7; s++)
            {
                var seg = Segments[s];
                dc.DrawLine((mask >> s & 1) != 0 ? on : off,
                    new Point(ox + seg.X1 * scale, top + seg.Y1 * scale), new Point(ox + seg.X2 * scale, top + seg.Y2 * scale));
            }
        }
    }
}

/// <summary>Thermometer column and heater indicator.</summary>
public sealed class ThermometerView : DeviceView<Thermometer>
{
    private const double MaxTemperature = 100;

    public ThermometerView()
    {
        Height = 170;
    }

    protected override void Draw(DrawingContext dc)
    {
        double t = Device!.Temperature;
        double x = 30, top = 10, height = 120;
        dc.DrawRoundedRectangle(Res("Bg.Panel2"), new Pen(Res("Border.Strong"), 1.5), new Rect(x, top, 18, height), 9, 9);
        double fill = Math.Clamp(t / MaxTemperature, 0, 1) * (height - 6);
        var red = new SolidColorBrush(Color.FromRgb(0xF2, 0x4B, 0x4B));
        dc.DrawRoundedRectangle(red, null, new Rect(x + 4, top + height - 3 - fill, 10, fill), 5, 5);
        dc.DrawEllipse(red, new Pen(Res("Border.Strong"), 1.5), new Point(x + 9, top + height + 12), 14, 14);
        dc.DrawText(Text($"{t:0.0} °C", 26, Res("Fg.Primary")), new Point(90, 30));
        bool heater = Device.HeaterOn;
        dc.DrawRoundedRectangle(heater ? Res("Warning") : Res("Bg.Panel2"), new Pen(Res("Border.Strong"), 1),
            new Rect(90, 80, 130, 30), 6, 6);
        dc.DrawText(Text(Loc.Instance[heater ? "device.heaterOn" : "device.heaterOff"], 13, heater ? Brushes.Black : Res("Fg.Secondary")),
            new Point(102, 86));
    }
}

/// <summary>Robot on its grid; click a cell to cycle empty / wall / lamp.</summary>
public sealed class RobotView : DeviceView<Robot>
{
    private const double Cell = 22;

    public RobotView()
    {
        Height = Robot.Height * Cell + 4;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Device == null) return;
        var p = e.GetPosition(this);
        int x = (int)(p.X / Cell), y = (int)(p.Y / Cell);
        if (x < Robot.Width && y < Robot.Height) Device.Toggle(x, y);
        Refresh();
    }

    protected override void Draw(DrawingContext dc)
    {
        var robot = Device!;
        var grid = new Pen(Res("Border"), 1);
        for (int y = 0; y < Robot.Height; y++)
        for (int x = 0; x < Robot.Width; x++)
        {
            var rect = new Rect(x * Cell, y * Cell, Cell, Cell);
            Brush fill = robot[x, y] switch
            {
                Robot.Cell.Wall => Res("Fg.Muted"),
                Robot.Cell.LampOn => new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4A)),
                Robot.Cell.LampOff => new SolidColorBrush(Color.FromRgb(0x5A, 0x4D, 0x1E)),
                _ => Res("Bg.Panel2"),
            };
            dc.DrawRectangle(fill, grid, rect);
        }
        var (rx, ry, dir) = robot.Position;
        var center = new Point(rx * Cell + Cell / 2, ry * Cell + Cell / 2);
        dc.DrawEllipse(Res("Accent"), null, center, Cell / 2 - 2, Cell / 2 - 2);
        (double dx, double dy) = dir switch { 0 => (1, 0), 1 => (0, 1), 2 => (-1, 0), _ => (0, -1) };
        dc.DrawLine(new Pen(Brushes.White, 3) { EndLineCap = PenLineCap.Round },
            center, new Point(center.X + dx * (Cell / 2 - 3), center.Y + dy * (Cell / 2 - 3)));
    }
}
