using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Emu8086.App.Services;
using Emu8086.App.ViewModels;
using Emu8086.Core.Analysis;
using Emu8086.Core.Machine;

namespace Emu8086.App.Views;

/// <summary>
/// Runs the current program (A) and another source file (B) side by side and shows where their
/// registers first differ, plus instructions, estimated cycles and screen output of each run.
/// </summary>
public sealed class CompareWindow
{
    private const int MaxRows = 2000;

    private readonly Window _window;
    private readonly MainViewModel _vm;
    private readonly IDialogService _dialogs;
    private readonly TextBlock _fileA = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _fileB = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) };
    private readonly ListBox _rows = new();
    private string? _pathB;

    public CompareWindow(Window owner, MainViewModel vm, IDialogService dialogs)
    {
        _vm = vm;
        _dialogs = dialogs;
        _window = new Window
        {
            Owner = owner,
            Title = Loc.Instance["compare.title"],
            Width = 980,
            Height = 680,
            MinWidth = 640,
            MinHeight = 420,
            Icon = owner.Icon,
            FontFamily = owner.FontFamily,
            FontSize = owner.FontSize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        ToolWindowChrome.Apply(_window, BuildLayout());
        _window.SetResourceReference(Window.BackgroundProperty, "Bg.Panel");
        _window.SetResourceReference(Window.ForegroundProperty, "Fg.Primary");
        _fileA.Text = _vm.ComparisonSource() is { } a ? Path.GetFileName(a.Path) : Loc.Instance["compare.none"];
        _fileB.Text = Loc.Instance["compare.none"];
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
        var loc = Loc.Instance;
        var header = new StackPanel();
        header.Children.Add(DialogParts.Label(loc["compare.hint"], true));
        header.Children.Add(FileRow(loc["compare.programA"], _fileA, null));
        header.Children.Add(FileRow(loc["compare.programB"], _fileB, DialogParts.Button("compare.choose", false, ChooseB)));
        header.Children.Add(DialogParts.Buttons(DialogParts.Button("compare.run", true, Run)));
        header.Children.Add(_summary);

        _rows.FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono");
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_rows);
        return root;
    }

    private static Grid FileRow(string label, TextBlock value, Button? button)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        grid.Children.Add(text);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        if (button != null)
        {
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }
        return grid;
    }

    private void ChooseB()
    {
        if (_dialogs.PickSourceFile() is not { } path) return;
        _pathB = path;
        _fileB.Text = Path.GetFileName(path);
    }

    private void Run()
    {
        var loc = Loc.Instance;
        if (_vm.ComparisonSource() is not { } a || _pathB == null)
        {
            _summary.Text = loc["compare.needBoth"];
            return;
        }
        _fileA.Text = Path.GetFileName(a.Path);
        var imageA = Build(a.Text, a.Path);
        var imageB = Build(File.ReadAllText(_pathB), _pathB);
        if (imageA == null || imageB == null) return;

        var result = ProgramComparer.Compare(imageA, imageB, AppPaths.VirtualDriveDirectory);
        _summary.Text = string.Join("\n",
            Describe("compare.programA", result.A),
            Describe("compare.programB", result.B),
            result.FirstDifference is { } step
                ? loc.Format("compare.firstDifference", step, string.Join(", ", result.Steps[step - 1].Differences))
                : loc["compare.noDifference"],
            loc[result.SameScreen ? "compare.sameScreen" : "compare.differentScreen"]);
        ShowRows(result);
    }

    private ProgramImage? Build(string text, string path)
    {
        var build = BuildService.Build(text, path);
        if (build.Image != null) return build.Image;
        _summary.Text = Loc.Instance.Format("compare.buildFailed", Path.GetFileName(path));
        _rows.Items.Clear();
        return null;
    }

    private static string Describe(string nameKey, RunSummary run)
    {
        var loc = Loc.Instance;
        string end = run.WaitingForInput ? loc["compare.waiting"]
            : run.Stop == StopReason.None ? loc.Format("compare.limit", ProgramComparer.DefaultMaxSteps)
            : loc.Format("stop." + run.Stop, run.ExitCode, run.Instructions);
        return loc.Format("compare.run.summary", loc[nameKey], run.Instructions, run.Cycles, CycleTable.Microseconds(run.Cycles), end);
    }

    private void ShowRows(CompareResult result)
    {
        _rows.Items.Clear();
        foreach (var step in result.Steps.Take(MaxRows))
        {
            string differences = step.Differences.Count == 0 ? "" : "  <> " + string.Join(" ", step.Differences);
            var text = new TextBlock { Text = $"{step.Index,5}  {Cell(step.A),-28}{Cell(step.B),-28}{differences}" };
            if (step.Index == result.FirstDifference)
            {
                text.FontWeight = FontWeights.Bold;
                text.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
            }
            _rows.Items.Add(text);
        }
        if (result.FirstDifference is { } first && first <= MaxRows) _rows.ScrollIntoView(_rows.Items[first - 1]);
    }

    private static string Cell(Emu8086.Core.Disassembler.DisassembledInstruction? instruction) =>
        instruction == null ? "-" : instruction.Text;
}
