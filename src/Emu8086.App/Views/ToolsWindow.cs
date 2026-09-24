using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Emu8086.App.Services;
using Emu8086.Core.Assembler;

namespace Emu8086.App.Views;

/// <summary>Number converter / calculator and ASCII (code page 437) table.</summary>
public sealed class ToolsWindow
{
    private const int AsciiColumns = 16;

    private readonly Window _window;
    private readonly TabControl _tabs = new();
    private readonly TextBox _input = new() { FontSize = 16 };
    private readonly StackPanel _results = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _error = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Encoding _cp437;

    public ToolsWindow(Window owner)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _cp437 = Encoding.GetEncoding(437);
        var loc = Loc.Instance;

        _tabs.Items.Add(new TabItem { Header = loc["tools.converter"], Content = BuildConverter() });
        _tabs.Items.Add(new TabItem { Header = loc["tools.ascii"], Content = BuildAsciiTable() });

        _window = new Window
        {
            Owner = owner,
            Title = loc["tools.title"],
            Width = 760,
            Height = 620,
            MinWidth = 560,
            MinHeight = 420,
            Icon = owner.Icon,
            FontFamily = owner.FontFamily,
            FontSize = owner.FontSize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        ToolWindowChrome.Apply(_window, _tabs);
        _window.SetResourceReference(Window.BackgroundProperty, "Bg.Panel");
        _window.SetResourceReference(Window.ForegroundProperty, "Fg.Primary");
        _window.Loaded += (_, _) => _input.Focus();
    }

    public void Show(bool ascii)
    {
        _tabs.SelectedIndex = ascii ? 1 : 0;
        _window.Show();
    }

    public void Activate(bool ascii)
    {
        _tabs.SelectedIndex = ascii ? 1 : 0;
        _window.Activate();
    }

    public event EventHandler? Closed
    {
        add => _window.Closed += value;
        remove => _window.Closed -= value;
    }

    #region Converter

    private UIElement BuildConverter()
    {
        var loc = Loc.Instance;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(DialogParts.Label(loc["tools.inputHint"], true));
        _input.FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono");
        _input.TextChanged += (_, _) => Recalculate();
        panel.Children.Add(_input);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        panel.Children.Add(_error);
        panel.Children.Add(_results);
        _input.Text = "0FFh + 1010b";
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void Recalculate()
    {
        _results.Children.Clear();
        _error.Text = "";
        if (string.IsNullOrWhiteSpace(_input.Text)) return;
        long value;
        try
        {
            value = ExpressionCalculator.Evaluate(_input.Text);
        }
        catch (AsmException e)
        {
            _error.Text = BuildService.Describe(new AsmDiagnostic(DiagnosticSeverity.Error, e.Code, e.Args, "", 0, ""));
            return;
        }

        var loc = Loc.Instance;
        ushort word = (ushort)value;
        byte low = (byte)value;
        Row(loc["tools.hex"], $"{word:X4}h" + (value is > 0xFFFF or < -0x8000 ? $"   ({(uint)value:X8}h)" : ""));
        Row(loc["tools.unsigned"], $"{word}" + (value is >= 0 and <= 0xFF ? "" : $"   (8 bit: {low})"));
        Row(loc["tools.signed"], $"{(short)word}   (8 bit: {(sbyte)low})");
        Row(loc["tools.binary"], Binary(word));
        Row(loc["tools.octal"], Convert.ToString(word, 8) + "o");
        string ch = _cp437.GetString([low]);
        Row(loc["tools.char"], low < 32 ? $"(control {low})" : $"'{ch}'");
    }

    private static string Binary(ushort value)
    {
        string bits = Convert.ToString(value, 2).PadLeft(16, '0');
        return $"{bits[..4]} {bits[4..8]}  {bits[8..12]} {bits[12..]}b";
    }

    private void Row(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Secondary");
        var box = new TextBox { Text = value, IsReadOnly = true, FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"), FontSize = 15 };
        Grid.SetColumn(box, 1);
        grid.Children.Add(name);
        grid.Children.Add(box);
        _results.Children.Add(grid);
    }

    #endregion

    #region ASCII table

    private UIElement BuildAsciiTable()
    {
        var mono = (FontFamily)Application.Current.FindResource("Font.Mono");
        var grid = new UniformGrid { Columns = AsciiColumns, Margin = new Thickness(12) };
        for (int code = 0; code < 256; code++)
        {
            string glyph = code switch
            {
                < 32 => ((char)"\u0000☺☻♥♦♣♠•◘○◙♂♀♪♫☼►◄↕‼¶§▬↨↑↓→←∟↔▲▼"[code]).ToString(),
                127 => "⌂",
                _ => _cp437.GetString([(byte)code]),
            };
            var cell = new StackPanel { Margin = new Thickness(2) };
            cell.Children.Add(new TextBlock { Text = code == 0 ? " " : glyph, FontFamily = mono, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center });
            var hex = new TextBlock { Text = code.ToString("X2"), FontFamily = mono, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
            hex.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Muted");
            cell.Children.Add(hex);
            var border = new Border { Child = cell, CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 2, 0, 2), Cursor = System.Windows.Input.Cursors.Hand };
            border.SetResourceReference(Border.BackgroundProperty, "Bg.Panel2");
            border.ToolTip = $"{code}  =  {code:X2}h  =  {Convert.ToString(code, 2).PadLeft(8, '0')}b";
            int value = code;
            border.MouseLeftButtonUp += (_, _) =>
            {
                _input.Text = $"0{value:X2}h";
                _tabs.SelectedIndex = 0;
            };
            grid.Children.Add(border);
        }
        return new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    #endregion
}
