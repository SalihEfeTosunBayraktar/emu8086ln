using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Emu8086.App.Services;
using Emu8086.App.ViewModels;

namespace Emu8086.App.Controls;

/// <summary>
/// Picture of the program's memory: the stack as a column that grows downwards (new slots fade
/// in, the newest is marked SP) and the variables as coloured blocks sized by their byte count
/// (a block flashes when its value changes).
/// </summary>
public sealed class MemoryMapView : StackPanel
{
    private const int MaxStackSlots = 32;
    private const double BytePixels = 6;
    private const double MinBlockWidth = 150;
    private const double MaxBlockWidth = 280;
    private static readonly Duration AnimationTime = new(TimeSpan.FromMilliseconds(400));
    private static readonly string[] Palette = ["Viz.Active", "Viz.Read", "Viz.Write", "Accent"];

    private readonly StackPanel _stack = new();
    private readonly WrapPanel _variables = new();
    private readonly TextBlock _stackTitle = Title();
    private List<(ushort Offset, ushort Value)> _lastSlots = new();
    private Dictionary<string, string> _lastValues = new();

    public MemoryMapView()
    {
        Children.Add(_stackTitle);
        Children.Add(_stack);
        var variablesTitle = Title();
        variablesTitle.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[memoryMap.variables]") { Source = Loc.Instance });
        variablesTitle.Margin = new Thickness(0, 16, 0, 6);
        Children.Add(variablesTitle);
        Children.Add(_variables);
    }

    public void Refresh(EmulatorSession session, ushort stackTop, IReadOnlyList<VariableRow> variables)
    {
        ushort ss, sp;
        var slots = new List<(ushort Offset, ushort Value)>();
        lock (session.Sync)
        {
            var cpu = session.Machine.Cpu;
            ss = cpu.SS;
            sp = cpu.SP;
            // Oldest (highest address) first, so the column grows downwards like the real stack.
            for (int off = stackTop - 2; off >= sp && slots.Count < MaxStackSlots; off -= 2)
                slots.Add(((ushort)off, session.Machine.Memory.Read16(ss, (ushort)off)));
        }
        _stackTitle.Text = Loc.Instance.Format("memoryMap.stack", ss, sp, slots.Count);
        if (!slots.SequenceEqual(_lastSlots)) RenderStack(ss, slots);
        RenderVariables(variables);
    }

    private void RenderStack(ushort ss, List<(ushort Offset, ushort Value)> slots)
    {
        var previous = _lastSlots.ToHashSet();
        _lastSlots = slots;
        _stack.Children.Clear();
        if (slots.Count == 0)
        {
            _stack.Children.Add(Muted(Loc.Instance["memoryMap.stackEmpty"]));
            return;
        }
        for (int i = 0; i < slots.Count; i++)
        {
            var (offset, value) = slots[i];
            bool top = i == slots.Count - 1;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var address = Mono($"{ss:X4}:{offset:X4}", 96);
            address.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Muted");
            DockPanel.SetDock(address, Dock.Left);
            row.Children.Add(address);
            var marker = Mono(top ? "SP" : "", 30);
            marker.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            DockPanel.SetDock(marker, Dock.Right);
            row.Children.Add(marker);
            var cell = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                BorderThickness = new Thickness(top ? 2 : 1),
                Child = Mono($"{value:X4}h   {value,5}", double.NaN),
            };
            cell.SetResourceReference(Border.BackgroundProperty, "Bg.Panel2");
            cell.SetResourceReference(Border.BorderBrushProperty, top ? "Accent" : "Border");
            row.Children.Add(cell);
            if (!previous.Contains(slots[i])) Animate(row, 0);
            _stack.Children.Add(row);
        }
    }

    private void RenderVariables(IReadOnlyList<VariableRow> variables)
    {
        var values = variables.ToDictionary(v => v.Name, v => v.Value);
        bool sameSet = values.Keys.ToHashSet().SetEquals(_lastValues.Keys);
        if (sameSet && values.All(v => _lastValues[v.Key] == v.Value)) return;
        var before = _lastValues;
        _lastValues = values;
        _variables.Children.Clear();
        if (variables.Count == 0)
        {
            _variables.Children.Add(Muted(Loc.Instance["variables.empty"]));
            return;
        }
        for (int i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            int bytes = v.ElementSize * v.Length;
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = v.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var info = Mono($"{v.Address}  {v.Type}", double.NaN);
            info.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Muted");
            info.FontSize = 11;
            content.Children.Add(info);
            var value = Mono(v.Value, double.NaN);
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            content.Children.Add(value);
            var block = new Border
            {
                Width = Math.Clamp(bytes * BytePixels + MinBlockWidth / 2, MinBlockWidth, MaxBlockWidth),
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(4, 1, 1, 1),
                Child = content,
                ToolTip = Loc.Instance.Format("memoryMap.bytes", v.Name, bytes),
            };
            block.SetResourceReference(Border.BackgroundProperty, "Bg.Panel2");
            block.SetResourceReference(Border.BorderBrushProperty, Palette[i % Palette.Length]);
            if (sameSet && before.TryGetValue(v.Name, out var old) && old != v.Value) Animate(block, 0.25);
            _variables.Children.Add(block);
        }
    }

    private static void Animate(UIElement element, double from) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(from, 1, AnimationTime));

    private static TextBlock Title()
    {
        var text = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Muted");
        return text;
    }

    private static TextBlock Mono(string text, double width) => new()
    {
        Text = text,
        Width = width,
        VerticalAlignment = VerticalAlignment.Center,
        FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"),
    };

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Muted");
        return block;
    }
}
