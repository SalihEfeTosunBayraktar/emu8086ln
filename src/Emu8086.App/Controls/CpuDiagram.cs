using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Emu8086.App.Services;
using Emu8086.Core.Analysis;
using Emu8086.Core.Cpu;

namespace Emu8086.App.Controls;

/// <summary>
/// Block diagram of the 8086 (BIU, EU, memory, I/O). For the current <see cref="StepAnalysis"/>
/// it highlights, phase by phase, the units and buses the instruction used.
/// Drawn on a fixed 1000 x 640 canvas scaled to fit.
/// </summary>
public sealed class CpuDiagram : FrameworkElement
{
    private const double CanvasWidth = 1000;
    private const double CanvasHeight = 640;
    private const int QueueCells = 6;
    private const int MemoryRows = 11;

    private static readonly string[] GeneralRegisters = ["AX", "BX", "CX", "DX"];
    private static readonly string[] PointerRegisters = ["SP", "BP", "SI", "DI"];
    private static readonly string[] SegmentRegisters = ["CS", "DS", "SS", "ES"];
    private static readonly string[] FlagOrder = ["OF", "DF", "IF", "TF", "SF", "ZF", "AF", "PF", "CF"];
    private static readonly CpuFlags[] FlagBits =
        [CpuFlags.OF, CpuFlags.DF, CpuFlags.IF, CpuFlags.TF, CpuFlags.SF, CpuFlags.ZF, CpuFlags.AF, CpuFlags.PF, CpuFlags.CF];

    // Layout (canvas coordinates)
    private static readonly Rect BiuBox = new(16, 44, 300, 440);
    private static readonly Rect EuBox = new(336, 44, 432, 580);
    private static readonly Rect MemoryBox = new(788, 44, 196, 440);
    private static readonly Rect IoBox = new(788, 504, 196, 120);
    private static readonly Point AdderCenter = new(250, 200);
    private const double AdderRadius = 32;
    private const double DataBusY = 500;
    private const double AddressBusY = 118;

    private StepAnalysis? _step;
    private StepPhase _phase = StepPhase.WriteBack;
    private bool _showAll = true;
    private Typeface? _ui, _mono;
    private double _dpi = 1;

    public CpuDiagram()
    {
        ThemeService.ThemeChanged += InvalidateVisual;
        Loc.Instance.LanguageChanged += InvalidateVisual;
    }

    /// <summary>Shows a step; with <paramref name="phase"/> null every phase is drawn at once.</summary>
    public void Show(StepAnalysis? step, StepPhase? phase)
    {
        _step = step;
        _showAll = phase == null;
        _phase = phase ?? StepPhase.WriteBack;
        InvalidateVisual();
    }

    /// <summary>True while phase <paramref name="p"/> is the one being shown (always, in the static view).</summary>
    private bool At(StepPhase p) => _showAll || _phase == p;
    private bool Reached(StepPhase p) => _showAll || _phase >= p;

    #region Drawing primitives

    private static Brush Res(string key) => ThemeService.Resource<Brush>(key);

    private FormattedText Text(string text, double size, Brush brush, bool mono = false, bool bold = false)
    {
        var face = mono ? _mono! : _ui!;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, _dpi);
        if (bold) ft.SetFontWeight(FontWeights.SemiBold);
        return ft;
    }

    private void Label(DrawingContext dc, string text, double x, double y, double size, Brush brush, bool mono = false, bool bold = false) =>
        dc.DrawText(Text(text, size, brush, mono, bold), new Point(x, y));

    private void Centered(DrawingContext dc, string text, Rect r, double size, Brush brush, bool mono = false, bool bold = false)
    {
        var ft = Text(text, size, brush, mono, bold);
        dc.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2));
    }

    private static void Box(DrawingContext dc, Rect r, Brush fill, Brush stroke, double thickness = 1, double radius = 6) =>
        dc.DrawRoundedRectangle(fill, new Pen(stroke, thickness), r, radius, radius);

    private static void Arrow(DrawingContext dc, Brush brush, double thickness, params Point[] points)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        for (int i = 1; i < points.Length; i++) dc.DrawLine(pen, points[i - 1], points[i]);
        var end = points[^1];
        var prev = points[^2];
        var dir = end - prev;
        if (dir.Length < 0.1) return;
        dir.Normalize();
        var normal = new Vector(-dir.Y, dir.X);
        double size = 5 + thickness;
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(end, true, true);
            g.LineTo(end - dir * size + normal * size * 0.6, true, false);
            g.LineTo(end - dir * size - normal * size * 0.6, true, false);
        }
        dc.DrawGeometry(brush, null, head);
    }

    #endregion

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? CanvasWidth : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? CanvasHeight : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _ui ??= new Typeface("Segoe UI");
        _mono ??= new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        double scale = Math.Min(RenderSize.Width / CanvasWidth, RenderSize.Height / CanvasHeight);
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((RenderSize.Width - CanvasWidth * scale) / 2, (RenderSize.Height - CanvasHeight * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));

        DrawPhaseBar(dc);
        DrawBuses(dc);
        DrawBiu(dc);
        DrawEu(dc);
        DrawMemory(dc);
        DrawIo(dc);

        dc.Pop();
        dc.Pop();
    }

    private CpuState State => _step == null ? default : Reached(StepPhase.WriteBack) ? _step.After : _step.Before;

    private void DrawPhaseBar(DrawingContext dc)
    {
        var loc = Loc.Instance;
        string[] keys = ["viz.phase.fetch", "viz.phase.decode", "viz.phase.execute", "viz.phase.write"];
        double x = 16;
        for (int i = 0; i < keys.Length; i++)
        {
            bool active = _step != null && !_showAll && (int)_phase == i;
            bool done = _step != null && (_showAll || (int)_phase > i);
            var r = new Rect(x, 6, 150, 28);
            Box(dc, r, active ? Res("Accent") : done ? Res("Accent.Soft") : Res("Bg.Panel2"), Res("Border"), 1, 14);
            Centered(dc, loc[keys[i]], r, 13, active ? Brushes.White : Res("Fg.Primary"), bold: active);
            x += 160;
        }
        if (_step != null)
            Label(dc, _step.Instruction.Text, x + 20, 9, 16, Res("Syntax.Mnemonic"), mono: true, bold: true);
    }

    private (Brush Fill, Brush Stroke, double Thickness) UnitStyle(bool read, bool write, bool active = false)
    {
        if (write) return (Res("Bg.Panel2"), Res("Viz.Write"), 2.5);
        if (read) return (Res("Bg.Panel2"), Res("Viz.Read"), 2.5);
        if (active) return (Res("Bg.Panel2"), Res("Viz.Active"), 2.5);
        return (Res("Bg.Panel2"), Res("Border"), 1);
    }

    private bool IsRead(string reg) => _step != null && At(StepPhase.Execute) && _step.RegistersRead.Contains(reg);

    private bool IsWritten(string reg) => _step != null && Reached(StepPhase.WriteBack) && _step.RegistersWritten.Any(r => r.Name == reg);

    private void Register(DrawingContext dc, Rect r, string name, ushort value, bool read, bool written, bool split = false)
    {
        var (fill, stroke, thick) = UnitStyle(read, written);
        Box(dc, r, fill, stroke, thick);
        Label(dc, name, r.X + 8, r.Y + (r.Height - 17) / 2, 13, Res("Fg.Secondary"), mono: true, bold: true);
        var valueBrush = written ? Res("Viz.Write") : Res("Fg.Primary");
        if (split)
        {
            double w = (r.Width - 44) / 2;
            var high = new Rect(r.X + 40, r.Y + 5, w - 2, r.Height - 10);
            var low = new Rect(r.X + 40 + w + 2, r.Y + 5, w - 2, r.Height - 10);
            Box(dc, high, Res("Bg.Editor"), Res("Border"), 1, 4);
            Box(dc, low, Res("Bg.Editor"), Res("Border"), 1, 4);
            Centered(dc, (value >> 8).ToString("X2"), high, 14, valueBrush, mono: true);
            Centered(dc, (value & 0xFF).ToString("X2"), low, 14, valueBrush, mono: true);
            Label(dc, name[0] + "H", high.X + 2, r.Bottom - 2, 9, Res("Fg.Muted"), mono: true);
            Label(dc, name[0] + "L", low.X + 2, r.Bottom - 2, 9, Res("Fg.Muted"), mono: true);
        }
        else
        {
            var v = new Rect(r.X + 40, r.Y + 5, r.Width - 46, r.Height - 10);
            Box(dc, v, Res("Bg.Editor"), Res("Border"), 1, 4);
            Centered(dc, value.ToString("X4"), v, 14, valueBrush, mono: true);
        }
    }

    private void DrawBiu(DrawingContext dc)
    {
        var loc = Loc.Instance;
        Box(dc, BiuBox, Res("Bg.Panel"), Res("Border.Strong"), 1.2, 10);
        Label(dc, loc["viz.biu"], BiuBox.X + 12, BiuBox.Y + 8, 12, Res("Fg.Muted"), bold: true);

        var s = State;
        bool fetch = _step != null && At(StepPhase.Fetch);
        string? segUsed = _step != null && Reached(StepPhase.Execute) ? _step.Address?.Segment : null;
        double y = BiuBox.Y + 40;
        foreach (var seg in SegmentRegisters)
        {
            bool read = seg == "CS" && fetch || seg == segUsed || IsRead(seg);
            Register(dc, new Rect(BiuBox.X + 14, y, 150, 34), seg, StepAnalyzer.RegisterValue(s, seg), read, IsWritten(seg));
            y += 42;
        }
        bool ipChanged = _step != null && Reached(StepPhase.WriteBack);
        Register(dc, new Rect(BiuBox.X + 14, y + 8, 150, 34), "IP", s.IP, fetch, ipChanged);

        // Address adder: segment * 16 + offset
        bool adderActive = fetch || segUsed != null;
        var (_, stroke, thick) = UnitStyle(false, false, adderActive);
        dc.DrawEllipse(Res("Bg.Panel2"), new Pen(stroke, thick), AdderCenter, AdderRadius, AdderRadius);
        Centered(dc, "Σ", new Rect(AdderCenter.X - AdderRadius, AdderCenter.Y - AdderRadius - 4, AdderRadius * 2, AdderRadius * 2), 26,
            adderActive ? Res("Viz.Active") : Res("Fg.Secondary"));
        var adderText = Text(loc["viz.adder"], 10, Res("Fg.Muted"));
        adderText.MaxTextWidth = 120;
        adderText.TextAlignment = TextAlignment.Center;
        dc.DrawText(adderText, new Point(AdderCenter.X - 60, AdderCenter.Y + AdderRadius + 6));
        if (_step != null && adderActive)
        {
            int physical = fetch ? _step.FetchAddress : _step.Address?.PhysicalAddress ?? 0;
            Centered(dc, $"{physical:X5}h", new Rect(AdderCenter.X - 60, AdderCenter.Y + AdderRadius + 40, 120, 20), 14, Res("Viz.Active"), mono: true, bold: true);
            Arrow(dc, Res("Viz.Active"), 2, new Point(BiuBox.X + 164, BiuBox.Y + 57), new Point(AdderCenter.X - AdderRadius, AdderCenter.Y - 12));
            Arrow(dc, Res("Viz.Active"), 2, new Point(BiuBox.X + 164, BiuBox.Y + 223), new Point(AdderCenter.X - AdderRadius + 4, AdderCenter.Y + 14));
        }

        // Instruction queue
        var qTitle = new Rect(BiuBox.X + 14, BiuBox.Bottom - 80, 270, 16);
        Label(dc, loc["viz.queue"], qTitle.X, qTitle.Y, 11, Res("Fg.Muted"));
        bool queueActive = _step != null && (fetch || At(StepPhase.Decode));
        for (int i = 0; i < QueueCells; i++)
        {
            var cell = new Rect(BiuBox.X + 14 + i * 45, BiuBox.Bottom - 58, 40, 32);
            bool has = _step != null && i < _step.Instruction.Bytes.Length;
            Box(dc, cell, Res("Bg.Editor"), has && queueActive ? Res("Viz.Read") : Res("Border"), has && queueActive ? 2 : 1, 4);
            if (has) Centered(dc, _step!.Instruction.Bytes[i].ToString("X2"), cell, 14, Res("Fg.Primary"), mono: true);
        }
    }

    private void DrawEu(DrawingContext dc)
    {
        var loc = Loc.Instance;
        Box(dc, EuBox, Res("Bg.Panel"), Res("Border.Strong"), 1.2, 10);
        Label(dc, loc["viz.eu"], EuBox.X + 12, EuBox.Y + 8, 12, Res("Fg.Muted"), bold: true);
        var s = State;

        // Control unit
        var control = new Rect(EuBox.X + 14, EuBox.Y + 32, EuBox.Width - 28, 50);
        bool decoding = _step != null && (_showAll || _phase >= StepPhase.Decode);
        var (_, cs, ct) = UnitStyle(false, false, decoding && At(StepPhase.Decode));
        Box(dc, control, Res("Bg.Panel2"), cs, ct);
        Label(dc, loc["viz.control"], control.X + 10, control.Y + 4, 10, Res("Fg.Muted"), bold: true);
        if (decoding) Label(dc, _step!.Instruction.Text, control.X + 10, control.Y + 20, 16, Res("Syntax.Mnemonic"), mono: true, bold: true);
        if (_step != null && At(StepPhase.Decode))
            Arrow(dc, Res("Viz.Active"), 2.5, new Point(BiuBox.Right - 20, BiuBox.Bottom - 40), new Point(BiuBox.Right + 8, BiuBox.Bottom - 40),
                new Point(BiuBox.Right + 8, control.Y + 25), new Point(control.X, control.Y + 25));

        // General purpose and pointer/index registers
        double y = EuBox.Y + 100;
        for (int i = 0; i < 4; i++)
        {
            string g = GeneralRegisters[i];
            bool read = IsRead(g) || IsRead(g[0] + "L") || IsRead(g[0] + "H");
            Register(dc, new Rect(EuBox.X + 14, y, 196, 38), g, StepAnalyzer.RegisterValue(s, g), read, IsWritten(g), split: true);
            string p = PointerRegisters[i];
            Register(dc, new Rect(EuBox.X + 222, y, 196, 38), p, StepAnalyzer.RegisterValue(s, p), IsRead(p), IsWritten(p));
            y += 50;
        }

        DrawAlu(dc);
        DrawFlags(dc, s);
    }

    private void DrawAlu(DrawingContext dc)
    {
        var loc = Loc.Instance;
        double cx = EuBox.X + EuBox.Width / 2, top = EuBox.Y + 318, bottom = top + 80;
        var alu = _step?.Alu;
        bool active = alu != null && Reached(StepPhase.Execute);
        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(new Point(cx - 130, top), true, true);
            g.LineTo(new Point(cx - 18, top), true, false);
            g.LineTo(new Point(cx, top + 18), true, false);
            g.LineTo(new Point(cx + 18, top), true, false);
            g.LineTo(new Point(cx + 130, top), true, false);
            g.LineTo(new Point(cx + 70, bottom), true, false);
            g.LineTo(new Point(cx - 70, bottom), true, false);
        }
        var (_, stroke, thick) = UnitStyle(false, false, active);
        dc.DrawGeometry(Res("Bg.Panel2"), new Pen(stroke, thick), shape);
        Centered(dc, loc["viz.alu"], new Rect(cx - 70, top + 26, 140, 20), 14, active ? Res("Viz.Active") : Res("Fg.Secondary"), bold: true);

        if (!active) return;
        string format = alu!.Word ? "X4" : "X2";
        string V(int? v) => v is int x ? x.ToString(format) : "--";
        Centered(dc, alu.Operation, new Rect(cx - 70, top + 48, 140, 20), 13, Res("Fg.Primary"), mono: true, bold: true);
        Centered(dc, "A = " + V(alu.OperandA), new Rect(cx - 150, top - 26, 130, 20), 13, Res("Viz.Read"), mono: true);
        Centered(dc, "B = " + V(alu.OperandB), new Rect(cx + 20, top - 26, 130, 20), 13, Res("Viz.Read"), mono: true);
        if (Reached(StepPhase.WriteBack))
            Centered(dc, "= " + V(alu.Result), new Rect(cx - 70, bottom + 4, 140, 20), 14, Res("Viz.Write"), mono: true, bold: true);
    }

    private void DrawFlags(DrawingContext dc, CpuState s)
    {
        var loc = Loc.Instance;
        double x = EuBox.X + 14, y = EuBox.Bottom - 70;
        Label(dc, loc["viz.flags"], x, y - 18, 11, Res("Fg.Muted"), bold: true);
        for (int i = 0; i < FlagOrder.Length; i++)
        {
            bool on = (s.Flags & (ushort)FlagBits[i]) != 0;
            bool changed = _step != null && Reached(StepPhase.WriteBack) && _step.FlagsChanged.Any(f => f.Name == FlagOrder[i]);
            var r = new Rect(x + i * 45, y, 40, 44);
            Box(dc, r, on ? Res("Accent.Soft") : Res("Bg.Panel2"), changed ? Res("Viz.Write") : Res("Border"), changed ? 2.5 : 1, 5);
            Centered(dc, FlagOrder[i], new Rect(r.X, r.Y + 3, r.Width, 16), 11, Res("Fg.Secondary"), mono: true);
            Centered(dc, on ? "1" : "0", new Rect(r.X, r.Y + 20, r.Width, 20), 15, changed ? Res("Viz.Write") : Res("Fg.Primary"), mono: true, bold: true);
        }
    }

    private void DrawBuses(DrawingContext dc)
    {
        var loc = Loc.Instance;
        bool fetch = _step != null && At(StepPhase.Fetch);
        bool addressExec = _step?.Address != null && At(StepPhase.Execute);
        bool addressActive = fetch || addressExec;

        // Address bus: adder -> memory
        var addressBrush = addressActive ? Res("Viz.Active") : Res("Viz.Bus");
        Arrow(dc, addressBrush, addressActive ? 5 : 4, new Point(AdderCenter.X + AdderRadius, AdderCenter.Y),
            new Point(AdderCenter.X + 60, AdderCenter.Y), new Point(AdderCenter.X + 60, AddressBusY), new Point(MemoryBox.X, AddressBusY));
        Label(dc, loc["viz.addressBus"], AdderCenter.X + 80, AddressBusY - 18, 10, Res("Fg.Muted"));

        // Data bus: memory <-> BIU / EU
        bool reads = fetch || (_step != null && Reached(StepPhase.Execute) && (_step.MemoryReads.Count > 0 || _step.Ports.Any(p => !p.IsWrite)));
        bool writes = _step != null && Reached(StepPhase.WriteBack) && (_step.MemoryWrites.Count > 0 || _step.Ports.Any(p => p.IsWrite));
        var dataBrush = writes ? Res("Viz.Write") : reads ? Res("Viz.Read") : Res("Viz.Bus");
        double thick = reads || writes ? 5 : 4;
        var pen = new Pen(dataBrush, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(pen, new Point(BiuBox.X + 150, DataBusY), new Point(MemoryBox.Right - 30, DataBusY));
        dc.DrawLine(pen, new Point(MemoryBox.Right - 30, DataBusY), new Point(MemoryBox.Right - 30, MemoryBox.Bottom));
        dc.DrawLine(pen, new Point(MemoryBox.Right - 30, DataBusY), new Point(MemoryBox.Right - 30, IoBox.Y));
        dc.DrawLine(pen, new Point(BiuBox.X + 150, DataBusY), new Point(BiuBox.X + 150, BiuBox.Bottom));
        double aluX = EuBox.X + EuBox.Width / 2;
        dc.DrawLine(pen, new Point(aluX, DataBusY), new Point(aluX, EuBox.Y + 398));
        Label(dc, loc["viz.dataBus"], MemoryBox.X - 110, DataBusY + 4, 10, Res("Fg.Muted"));
        if (writes) Arrow(dc, dataBrush, 3, new Point(MemoryBox.X - 60, DataBusY - 10), new Point(MemoryBox.X - 20, DataBusY - 10));
        else if (reads) Arrow(dc, dataBrush, 3, new Point(MemoryBox.X - 20, DataBusY - 10), new Point(MemoryBox.X - 60, DataBusY - 10));
    }

    private void DrawMemory(DrawingContext dc)
    {
        var loc = Loc.Instance;
        Box(dc, MemoryBox, Res("Bg.Panel"), Res("Border.Strong"), 1.2, 10);
        Label(dc, loc["viz.memory"], MemoryBox.X + 12, MemoryBox.Y + 8, 12, Res("Fg.Muted"), bold: true);
        if (_step == null) return;

        var rows = new List<(int Address, string Value, Brush Brush)>();
        if (At(StepPhase.Fetch))
            for (int i = 0; i < _step.Instruction.Bytes.Length; i++)
                rows.Add((_step.FetchAddress + i, _step.Instruction.Bytes[i].ToString("X2"), Res("Viz.Active")));
        if (Reached(StepPhase.Execute))
            rows.AddRange(_step.MemoryReads.Select(r => (r.Address, r.Value.ToString("X2"), Res("Viz.Read"))));
        if (Reached(StepPhase.WriteBack))
            rows.AddRange(_step.MemoryWrites.Select(w => (w.Address, $"{w.OldValue:X2}→{w.Value:X2}", Res("Viz.Write"))));

        double y = MemoryBox.Y + 36;
        foreach (var (address, value, brush) in rows.Take(MemoryRows))
        {
            var r = new Rect(MemoryBox.X + 10, y, MemoryBox.Width - 20, 30);
            Box(dc, r, Res("Bg.Panel2"), brush, 2, 5);
            Label(dc, $"{address:X5}h", r.X + 8, r.Y + 6, 13, Res("Fg.Secondary"), mono: true);
            Label(dc, value, r.X + 94, r.Y + 6, 13, brush, mono: true, bold: true);
            y += 34;
        }
        if (rows.Count > MemoryRows) Label(dc, $"+{rows.Count - MemoryRows}", MemoryBox.X + 12, y, 12, Res("Fg.Muted"));
    }

    private void DrawIo(DrawingContext dc)
    {
        var loc = Loc.Instance;
        bool active = _step != null && _step.Ports.Count > 0 && Reached(StepPhase.Execute);
        Box(dc, IoBox, Res("Bg.Panel"), active ? Res("Viz.Write") : Res("Border.Strong"), active ? 2.5 : 1.2, 10);
        Label(dc, loc["viz.io"], IoBox.X + 12, IoBox.Y + 8, 12, Res("Fg.Muted"), bold: true);
        if (!active) return;
        double y = IoBox.Y + 34;
        foreach (var p in _step!.Ports.Take(3))
        {
            string text = $"{(p.IsWrite ? "OUT" : "IN ")} {p.Port,3}: {p.Value.ToString(p.Word ? "X4" : "X2")}h";
            Label(dc, text, IoBox.X + 12, y, 13, p.IsWrite ? Res("Viz.Write") : Res("Viz.Read"), mono: true, bold: true);
            y += 24;
        }
    }
}
