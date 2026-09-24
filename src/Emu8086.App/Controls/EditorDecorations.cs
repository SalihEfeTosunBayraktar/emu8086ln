using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Emu8086.App.Services;
using Emu8086.App.ViewModels;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace Emu8086.App.Controls;

/// <summary>Left margin showing breakpoints (click to toggle) and the execution arrow.</summary>
public sealed class BreakpointMargin : AbstractMargin
{
    private const double MarginWidth = 20;

    public DocumentViewModel? Model { get; set; }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var view = TextView;
        if (view == null || Model == null) return;
        var pos = e.GetPosition(view);
        var line = view.GetVisualLineFromVisualTop(pos.Y + view.VerticalOffset);
        if (line == null) return;
        Model.ToggleBreakpoint(line.FirstDocumentLine.LineNumber);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var view = TextView;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (view == null || !view.VisualLinesValid || Model == null) return;
        var breakpoint = ThemeService.Resource<Brush>("Breakpoint");
        var exec = ThemeService.Resource<Brush>("ExecLine.Border");

        foreach (var line in view.VisualLines)
        {
            int number = line.FirstDocumentLine.LineNumber;
            double top = line.VisualTop - view.VerticalOffset;
            double center = top + line.Height / 2;
            if (Model.Breakpoints.Contains(number))
                dc.DrawEllipse(breakpoint, null, new Point(MarginWidth / 2, center), 5.5, 5.5);
            if (Model.ExecutionLine == number)
            {
                var arrow = new StreamGeometry();
                using (var g = arrow.Open())
                {
                    g.BeginFigure(new Point(4, center - 5), true, true);
                    g.LineTo(new Point(15, center), true, false);
                    g.LineTo(new Point(4, center + 5), true, false);
                }
                dc.DrawGeometry(exec, null, arrow);
            }
        }
    }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();
}

/// <summary>Highlights the executing line and lines with assembly errors.</summary>
public sealed class LineHighlightRenderer : IBackgroundRenderer
{
    public DocumentViewModel? Model { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (Model == null || !textView.VisualLinesValid) return;
        var exec = ThemeService.Resource<Brush>("ExecLine");
        var execBorder = new Pen(ThemeService.Resource<Brush>("ExecLine.Border"), 1);
        var error = ThemeService.Resource<Brush>("ErrorLine");
        var warning = ThemeService.Resource<Brush>("WarningLine");
        var warningLines = Model.WarningLines;
        var breakpoint = ThemeService.Resource<Brush>("Breakpoint");
        double width = textView.ActualWidth;

        foreach (var line in textView.VisualLines)
        {
            int number = line.FirstDocumentLine.LineNumber;
            double top = line.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, top, width, line.Height);
            if (Model.ErrorLines.Contains(number)) dc.DrawRectangle(error, null, rect);
            else if (warningLines.Contains(number)) dc.DrawRectangle(warning, null, rect);
            if (Model.Breakpoints.Contains(number))
                dc.DrawRectangle(null, new Pen(breakpoint, 1) { DashStyle = DashStyles.Dash }, rect);
            if (Model.ExecutionLine == number) dc.DrawRectangle(exec, execBorder, rect);
        }
    }
}
