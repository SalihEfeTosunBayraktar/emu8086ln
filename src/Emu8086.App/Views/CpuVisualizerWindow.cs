using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Emu8086.App.Controls;
using Emu8086.App.Services;
using Emu8086.App.ViewModels;
using Emu8086.Core.Analysis;

namespace Emu8086.App.Views;

/// <summary>
/// Live view of the CPU architecture: after every step the diagram replays the fetch, decode,
/// execute and write-back phases while the side panel explains them in words.
/// </summary>
public sealed class CpuVisualizerWindow
{
    private static readonly TimeSpan PhaseDuration = TimeSpan.FromMilliseconds(650);

    private readonly Window _window;
    private readonly MainViewModel _vm;
    private readonly CpuDiagram _diagram = new();
    private readonly ItemsControl _narration = new();
    private readonly CheckBox _animate;
    private readonly DispatcherTimer _timer;
    private StepAnalysis? _step;
    private List<NarrationLine> _lines = new();
    private int _phase;

    public CpuVisualizerWindow(Window owner, MainViewModel vm)
    {
        _vm = vm;
        var loc = Loc.Instance;
        _animate = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        _animate.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("[viz.animate]") { Source = loc });
        _animate.Click += (_, _) => Replay();

        _timer = new DispatcherTimer { Interval = PhaseDuration };
        _timer.Tick += (_, _) => NextPhase();

        _window = new Window
        {
            Owner = owner,
            Width = 1320,
            Height = 780,
            MinWidth = 800,
            MinHeight = 500,
            FontFamily = (FontFamily)Application.Current.FindResource("Font.UI"),
            FontSize = 13,
            Icon = owner.Icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        ToolWindowChrome.Apply(_window, BuildLayout());
        _window.SetBinding(Window.TitleProperty, new System.Windows.Data.Binding("[viz.title]") { Source = loc });
        _window.SetResourceReference(Window.BackgroundProperty, "Bg.Window");
        _window.SetResourceReference(Window.ForegroundProperty, "Fg.Primary");

        vm.StepCompleted += OnStepCompleted;
        _window.Closed += (_, _) =>
        {
            vm.StepCompleted -= OnStepCompleted;
            _timer.Stop();
        };
        OnStepCompleted(vm.LastStep);
    }

    public void Show() => _window.Show();
    public void Activate() => _window.Activate();
    public event EventHandler? Closed
    {
        add => _window.Closed += value;
        remove => _window.Closed -= value;
    }

    private UIElement BuildLayout()
    {
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 8) };
        toolbar.Children.Add(ToolButton("Icon.StepBack", "cmd.stepBack", _vm.StepBackCommand));
        toolbar.Children.Add(ToolButton("Icon.StepInto", "cmd.stepInto", _vm.StepIntoCommand));
        toolbar.Children.Add(ToolButton("Icon.StepOver", "cmd.stepOver", _vm.StepOverCommand));
        toolbar.Children.Add(ToolButton("Icon.Reset", "cmd.reset", _vm.StopCommand));
        toolbar.Children.Add(_animate);
        var replay = new Button { Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
        replay.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("[viz.replay]") { Source = Loc.Instance });
        replay.Click += (_, _) => Replay();
        toolbar.Children.Add(replay);

        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        legend.Children.Add(LegendItem("Viz.Active", "viz.legend.active"));
        legend.Children.Add(LegendItem("Viz.Read", "viz.legend.read"));
        legend.Children.Add(LegendItem("Viz.Write", "viz.legend.write"));
        toolbar.Children.Add(legend);

        var title = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[viz.explanation]") { Source = Loc.Instance });
        var side = new DockPanel { Margin = new Thickness(0, 4, 12, 12), Width = 330 };
        DockPanel.SetDock(title, Dock.Top);
        side.Children.Add(title);
        side.Children.Add(new ScrollViewer { Content = _narration, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var body = new DockPanel();
        DockPanel.SetDock(side, Dock.Right);
        body.Children.Add(side);
        var diagramHost = new Border { Margin = new Thickness(12, 4, 12, 12), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Child = _diagram };
        diagramHost.SetResourceReference(Border.BackgroundProperty, "Bg.Editor");
        body.Children.Add(diagramHost);

        var root = new DockPanel();
        var toolbarHost = new Border { Child = toolbar, BorderThickness = new Thickness(0, 0, 0, 1) };
        toolbarHost.SetResourceReference(Border.BackgroundProperty, "Bg.Panel");
        toolbarHost.SetResourceReference(Border.BorderBrushProperty, "Border");
        DockPanel.SetDock(toolbarHost, Dock.Top);
        root.Children.Add(toolbarHost);
        root.Children.Add(body);
        return root;
    }

    private static Button ToolButton(string icon, string tooltipKey, System.Windows.Input.ICommand command)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = (Geometry)Application.Current.FindResource(icon),
            Style = (Style)Application.Current.FindResource("Icon"),
        };
        var button = new Button { Content = path, Command = command, Style = (Style)Application.Current.FindResource("ToolButton"), Margin = new Thickness(2, 0, 2, 0) };
        button.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding($"[{tooltipKey}]") { Source = Loc.Instance });
        return button;
    }

    private static UIElement LegendItem(string brushKey, string textKey)
    {
        var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(12, 0, 6, 0) };
        dot.SetResourceReference(Border.BackgroundProperty, brushKey);
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Secondary");
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding($"[{textKey}]") { Source = Loc.Instance });
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(dot);
        panel.Children.Add(text);
        return panel;
    }

    private void OnStepCompleted(StepAnalysis? step)
    {
        _step = step;
        _lines = step == null ? new() : StepNarrator.Describe(step);
        Replay();
    }

    /// <summary>Phases are animated when stepping, or when running slowly enough for all four phases to play.</summary>
    private bool Animating =>
        _animate.IsChecked == true && _step != null
        && (!_vm.Session.IsBusy || _vm.Session.StepDelayMs >= PhaseDuration.TotalMilliseconds * 4);

    private void Replay()
    {
        _timer.Stop();
        if (!Animating)
        {
            ShowPhase(null);
            return;
        }
        _phase = 0;
        ShowPhase(StepPhase.Fetch);
        _timer.Start();
    }

    private void NextPhase()
    {
        _phase++;
        if (_phase > (int)StepPhase.WriteBack)
        {
            _timer.Stop();
            ShowPhase(null);
            return;
        }
        ShowPhase((StepPhase)_phase);
    }

    private void ShowPhase(StepPhase? phase)
    {
        _diagram.Show(_step, phase);
        _narration.Items.Clear();
        if (_step == null)
        {
            _narration.Items.Add(new TextBlock { Text = Loc.Instance["viz.noStep"], TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var line in _lines)
        {
            bool current = phase == line.Phase;
            bool visible = phase == null || line.Phase <= phase;
            if (!visible) continue;
            var text = new TextBlock
            {
                Text = line.Text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"),
                FontSize = 12.5,
            };
            var card = new Border { Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(6), Child = text, BorderThickness = new Thickness(current ? 2 : 1) };
            card.SetResourceReference(Border.BackgroundProperty, "Bg.Panel2");
            card.SetResourceReference(Border.BorderBrushProperty, current ? "Accent" : "Border");
            _narration.Items.Add(card);
        }
    }
}
