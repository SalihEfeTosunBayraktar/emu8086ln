using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace Emu8086.App.Views;

/// <summary>
/// Gives a resizable secondary window the same themed title bar as the main window
/// (logo, title, minimize / maximize / close) instead of the system frame.
/// </summary>
public static class ToolWindowChrome
{
    private const double CaptionHeight = 38;
    private const double MaximizedInset = 7;

    public static void Apply(Window window, UIElement content)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = CaptionHeight,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        var logo = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Margin = new Thickness(12, 0, 8, 0),
            Background = new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"))),
        };
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Window.Title)) { Source = window });

        var maximizeIcon = Icon("Icon.Maximize", 10);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(CaptionButton("CaptionButton", Icon("Icon.Minimize", 11), () => window.WindowState = WindowState.Minimized));
        buttons.Children.Add(CaptionButton("CaptionButton", maximizeIcon, () =>
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        buttons.Children.Add(CaptionButton("CloseCaptionButton", Icon("Icon.Close", 11), window.Close));

        var bar = new DockPanel { Height = CaptionHeight };
        bar.SetResourceReference(Panel.BackgroundProperty, "Bg.Window");
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(logo);
        bar.Children.Add(title);

        var layout = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        layout.Children.Add(bar);
        layout.Children.Add(content);

        var root = new Border { Child = layout, BorderThickness = new Thickness(1) };
        root.SetResourceReference(Border.BorderBrushProperty, "Border.Strong");
        window.Content = root;

        window.StateChanged += (_, _) =>
        {
            bool max = window.WindowState == WindowState.Maximized;
            root.Margin = new Thickness(max ? MaximizedInset : 0);
            root.BorderThickness = new Thickness(max ? 0 : 1);
            maximizeIcon.Data = (Geometry)Application.Current.FindResource(max ? "Icon.Restore" : "Icon.Maximize");
        };
    }

    private static System.Windows.Shapes.Path Icon(string key, double size) => new()
    {
        Data = (Geometry)Application.Current.FindResource(key),
        Style = (Style)Application.Current.FindResource("Icon"),
        Width = size,
        Height = size,
    };

    private static Button CaptionButton(string styleKey, UIElement icon, Action onClick)
    {
        var button = new Button { Content = icon, Style = (Style)Application.Current.FindResource(styleKey) };
        button.Click += (_, _) => onClick();
        return button;
    }
}
