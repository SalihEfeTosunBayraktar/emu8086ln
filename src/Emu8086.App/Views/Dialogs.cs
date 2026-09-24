using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Emu8086.App.Services;
using Emu8086.App.ViewModels;
using Microsoft.Win32;

namespace Emu8086.App.Views;

/// <summary>Shared building blocks for the small code-built dialogs.</summary>
internal static class DialogParts
{
    public static Window Create(Window owner, string title, UIElement content, double width = 440)
    {
        var window = new Window
        {
            Owner = owner,
            Title = title,
            Style = (Style)Application.Current.FindResource("DialogWindow"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border { Padding = new Thickness(20, 16, 20, 18), Child = content, Width = width },
        };
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) window.Close();
        };
        return window;
    }

    public static StackPanel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var b in buttons)
        {
            b.Margin = new Thickness(8, 0, 0, 0);
            b.MinWidth = 90;
            panel.Children.Add(b);
        }
        return panel;
    }

    public static Button Button(string textKey, bool accent, Action onClick)
    {
        var button = new Button { Content = Loc.Instance[textKey] };
        if (accent) button.Style = (Style)Application.Current.FindResource("AccentButton");
        button.Click += (_, _) => onClick();
        return button;
    }

    public static TextBlock Label(string text, bool secondary = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        if (secondary) block.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Secondary");
        return block;
    }
}

public static class MessageDialog
{
    public static void Show(Window owner, string message)
    {
        Window? window = null;
        var content = new StackPanel();
        content.Children.Add(DialogParts.Label(message));
        content.Children.Add(DialogParts.Buttons(DialogParts.Button("button.ok", true, () => window!.Close())));
        window = DialogParts.Create(owner, "emu8086ln", content);
        window.ShowDialog();
    }

    public static ConfirmResult Ask(Window owner, string message)
    {
        Window? window = null;
        var result = ConfirmResult.Cancel;
        void Close(ConfirmResult r)
        {
            result = r;
            window!.Close();
        }
        var content = new StackPanel();
        content.Children.Add(DialogParts.Label(message));
        content.Children.Add(DialogParts.Buttons(
            DialogParts.Button("button.yes", true, () => Close(ConfirmResult.Yes)),
            DialogParts.Button("button.no", false, () => Close(ConfirmResult.No)),
            DialogParts.Button("button.cancel", false, () => Close(ConfirmResult.Cancel))));
        window = DialogParts.Create(owner, "emu8086ln", content);
        window.ShowDialog();
        return result;
    }
}

public sealed class InputDialog
{
    private readonly Window _window;
    private readonly TextBox _box;
    private bool _accepted;

    public InputDialog(string title, string prompt, string initial)
    {
        _box = new TextBox { Text = initial };
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Accept();
        };
        var content = new StackPanel();
        content.Children.Add(DialogParts.Label(prompt));
        content.Children.Add(_box);
        content.Children.Add(DialogParts.Buttons(
            DialogParts.Button("button.ok", true, Accept),
            DialogParts.Button("button.cancel", false, () => _window!.Close())));
        _window = DialogParts.Create(Application.Current.MainWindow, title, content);
        _window.Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    public Window Owner
    {
        set => _window.Owner = value;
    }

    public string Value => _box.Text.Trim();

    private void Accept()
    {
        _accepted = true;
        _window.Close();
    }

    public bool? ShowDialog()
    {
        _window.ShowDialog();
        return _accepted;
    }
}

/// <summary>New project wizard: name, location and template (with a description of each).</summary>
public sealed class NewProjectDialog
{
    private readonly Window _window;
    private readonly TextBox _name;
    private readonly TextBox _location;
    private readonly ListBox _templates;
    private readonly TextBlock _error;

    public NewProjectDialog()
    {
        var loc = Loc.Instance;
        Directory.CreateDirectory(AppPaths.DefaultProjectsDirectory);
        _name = new TextBox { Text = ProjectService.UniqueName(AppPaths.DefaultProjectsDirectory, loc["newProject.defaultName"]) };
        _location = new TextBox { Text = AppPaths.DefaultProjectsDirectory };
        _error = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Error");

        _templates = new ListBox { Height = 210, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var t in ProjectService.Templates()) _templates.Items.Add(t);
        _templates.SelectedIndex = 0;
        _templates.ItemTemplate = TemplateItem();

        var browse = new Button { Content = "...", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        var locationRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        locationRow.Children.Add(browse);
        locationRow.Children.Add(_location);

        var content = new StackPanel();
        content.Children.Add(DialogParts.Label(loc["newProject.template"], true));
        content.Children.Add(_templates);
        content.Children.Add(DialogParts.Label(loc["newProject.name"], true));
        content.Children.Add(_name);
        content.Children.Add(new Border { Height = 10 });
        content.Children.Add(DialogParts.Label(loc["newProject.location"], true));
        content.Children.Add(locationRow);
        content.Children.Add(_error);
        content.Children.Add(DialogParts.Buttons(
            DialogParts.Button("button.create", true, Accept),
            DialogParts.Button("button.cancel", false, () => _window!.Close())));

        _window = DialogParts.Create(Application.Current.MainWindow, loc["newProject.title"], content, 520);
        _window.Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    public Window Owner
    {
        set => _window.Owner = value;
    }

    public NewProjectRequest? Result { get; private set; }

    private static DataTemplate TemplateItem()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 4, 2, 4));
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProjectTemplate.Id)) { Converter = new TemplateTextConverter(".name") });
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        var description = new FrameworkElementFactory(typeof(TextBlock));
        description.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProjectTemplate.Id)) { Converter = new TemplateTextConverter(".desc") });
        description.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        description.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Secondary");
        root.AppendChild(title);
        root.AppendChild(description);
        return new DataTemplate { VisualTree = root };
    }

    private sealed class TemplateTextConverter(string suffix) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            Loc.Instance["template." + value + suffix];

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private void Browse()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = Directory.Exists(_location.Text) ? _location.Text : AppPaths.DocumentsDirectory };
        if (dialog.ShowDialog(_window) == true) _location.Text = dialog.FolderName;
    }

    private void Accept()
    {
        var loc = Loc.Instance;
        string name = _name.Text.Trim();
        string parent = _location.Text.Trim();
        if (!ProjectService.IsValidName(name))
        {
            _error.Text = loc["newProject.invalidName"];
            return;
        }
        if (Directory.Exists(Path.Combine(parent, name)))
        {
            _error.Text = loc["newProject.exists"];
            return;
        }
        if (_templates.SelectedItem is not ProjectTemplate template) return;
        Result = new NewProjectRequest(name, parent, template);
        _window.Close();
    }

    public bool? ShowDialog()
    {
        _window.ShowDialog();
        return Result != null;
    }
}

public sealed class AboutWindow
{
    private readonly Window _window;

    public AboutWindow()
    {
        var loc = Loc.Instance;
        var content = new StackPanel();
        var title = new TextBlock { Text = "emu8086ln", FontSize = 22, FontWeight = FontWeights.SemiBold };
        content.Children.Add(title);
        string version = typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        content.Children.Add(DialogParts.Label(loc.Format("about.version", version), true));
        content.Children.Add(new Border { Height = 8 });
        content.Children.Add(DialogParts.Label(loc["about.text"]));
        content.Children.Add(DialogParts.Label(loc.Format("about.paths", AppPaths.DocumentsDirectory), true));
        content.Children.Add(DialogParts.Buttons(DialogParts.Button("button.ok", true, () => _window!.Close())));
        _window = DialogParts.Create(Application.Current.MainWindow, loc["cmd.about"], content, 480);
    }

    public Window Owner
    {
        set => _window.Owner = value;
    }

    public void ShowDialog() => _window.ShowDialog();
}
