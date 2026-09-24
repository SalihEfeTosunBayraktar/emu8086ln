using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Emu8086.App.Services;
using Emu8086.Core.Analysis;

namespace Emu8086.App.Controls;

/// <summary>
/// Horizontal strip of the most recent instructions. Each card shows which building blocks the
/// instruction used (registers, ALU, memory, I/O); clicking one replays it, and it can be rewound to.
/// </summary>
public sealed class ExecutionTimeline : DockPanel
{
    private static readonly string[] Units = ["viz.unit.reg", "viz.unit.alu", "viz.unit.mem", "viz.unit.io"];

    private readonly StackPanel _cards = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer _scroll;
    private readonly Button _rewind;
    private IReadOnlyList<StepAnalysis> _steps = [];
    private int _selected = -1;

    public ExecutionTimeline()
    {
        Margin = new Thickness(12, 0, 12, 12);
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[viz.timeline]") { Source = Loc.Instance });
        SetDock(title, Dock.Left);
        Children.Add(title);

        _rewind = new Button { Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsEnabled = false };
        _rewind.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("[viz.rewindHere]") { Source = Loc.Instance });
        _rewind.Click += (_, _) => RewindRequested?.Invoke(_steps.Count - 1 - _selected);
        SetDock(_rewind, Dock.Right);
        Children.Add(_rewind);

        _scroll = new ScrollViewer
        {
            Content = _cards,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Children.Add(_scroll);
    }

    /// <summary>A card was clicked; the argument is the step to show.</summary>
    public event Action<StepAnalysis>? StepSelected;

    /// <summary>Rewind was clicked; the argument is how many instructions to undo.</summary>
    public event Action<int>? RewindRequested;

    public void Show(IReadOnlyList<StepAnalysis> steps)
    {
        _steps = steps;
        _selected = steps.Count - 1;
        Render();
        _scroll.ScrollToRightEnd();
    }

    private void Select(int index)
    {
        _selected = index;
        Render();
        StepSelected?.Invoke(_steps[index]);
    }

    private void Render()
    {
        _cards.Children.Clear();
        for (int i = 0; i < _steps.Count; i++) _cards.Children.Add(Card(_steps[i], i));
        _rewind.IsEnabled = _selected >= 0 && _selected < _steps.Count - 1;
    }

    private Border Card(StepAnalysis step, int index)
    {
        bool[] used =
        [
            step.RegistersWritten.Count > 0,
            step.Alu != null,
            step.MemoryReads.Count + step.MemoryWrites.Count > 0,
            step.Ports.Count > 0 || step.Interrupt != null,
        ];
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        for (int u = 0; u < Units.Length; u++)
        {
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Margin = new Thickness(2, 0, 2, 0), ToolTip = Loc.Instance[Units[u]] };
            dot.SetResourceReference(Border.BackgroundProperty, used[u] ? "Viz.Active" : "Border");
            dots.Children.Add(dot);
        }

        var text = new TextBlock
        {
            Text = step.Mnemonic,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"),
            FontSize = 12,
        };
        var content = new StackPanel();
        content.Children.Add(text);
        content.Children.Add(dots);

        bool selected = index == _selected;
        var card = new Border
        {
            Child = content,
            MinWidth = 56,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = $"{step.Instruction.Segment:X4}:{step.Instruction.Offset:X4}  {step.Instruction.Text}",
        };
        card.SetResourceReference(Border.BackgroundProperty, "Bg.Panel2");
        card.SetResourceReference(Border.BorderBrushProperty, selected ? "Accent" : "Border");
        card.MouseLeftButtonUp += (_, _) => Select(index);
        return card;
    }
}
