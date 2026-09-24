using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Emu8086.App.Services;

namespace Emu8086.App.Views;

public sealed record ReferenceEntry(string Name, string Category, string Syntax, string Description, string Flags, string Example);

/// <summary>Searchable instruction / interrupt reference loaded from config/reference/&lt;lang&gt;.json.</summary>
public sealed class ReferenceWindow
{
    private readonly Window _window;
    private readonly List<ReferenceEntry> _entries;
    private readonly ListBox _list = new() { Width = 200 };
    private readonly StackPanel _details = new() { Margin = new Thickness(20, 4, 8, 8) };
    private readonly TextBox _search = new();

    public ReferenceWindow()
    {
        _entries = Load();
        var loc = Loc.Instance;

        _search.TextChanged += (_, _) => Filter();
        _list.DisplayMemberPath = nameof(ReferenceEntry.Name);
        _list.SelectionChanged += (_, _) => Show(_list.SelectedItem as ReferenceEntry);

        var left = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(_search, Dock.Top);
        _search.Margin = new Thickness(0, 0, 0, 8);
        _search.ToolTip = loc["reference.search"];
        left.Children.Add(_search);
        left.Children.Add(_list);

        var root = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        _window = new Window
        {
            Title = loc["reference.title"],
            Width = 820,
            Height = 600,
            Content = root,
            FontFamily = (FontFamily)Application.Current.FindResource("Font.UI"),
            FontSize = 13,
        };
        _window.SetResourceReference(Window.BackgroundProperty, "Bg.Panel");
        _window.SetResourceReference(Window.ForegroundProperty, "Fg.Primary");
        Filter();
        _window.Loaded += (_, _) => _search.Focus();
    }

    public Window Owner
    {
        set => _window.Owner = value;
    }

    public void Show() => _window.Show();

    private static List<ReferenceEntry> Load()
    {
        foreach (var lang in new[] { Loc.Instance.CurrentCode, "en" })
        {
            string path = Path.Combine(AppPaths.ConfigDirectory, "reference", lang + ".json");
            if (!File.Exists(path)) continue;
            try
            {
                return JsonSerializer.Deserialize<List<ReferenceEntry>>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
        return [];
    }

    private void Filter()
    {
        string q = _search.Text.Trim();
        var items = _entries.Where(e => q.Length == 0
                                        || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                        || e.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _list.ItemsSource = items;
        if (items.Count > 0) _list.SelectedIndex = 0;
    }

    private void Show(ReferenceEntry? entry)
    {
        _details.Children.Clear();
        if (entry == null) return;
        var loc = Loc.Instance;
        var mono = (FontFamily)Application.Current.FindResource("Font.Mono");

        _details.Children.Add(new TextBlock { Text = entry.Name, FontSize = 24, FontWeight = FontWeights.SemiBold });
        var category = new TextBlock { Text = entry.Category, Margin = new Thickness(0, 0, 0, 12) };
        category.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        _details.Children.Add(category);
        _details.Children.Add(new TextBlock { Text = entry.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        void Block(string titleKey, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var title = new TextBlock { Text = loc[titleKey], FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 4) };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Muted");
            var body = new Border { Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6), Child = new TextBlock { Text = text, FontFamily = mono, TextWrapping = TextWrapping.Wrap } };
            body.SetResourceReference(Border.BackgroundProperty, "Bg.Editor");
            _details.Children.Add(title);
            _details.Children.Add(body);
        }

        Block("reference.syntax", entry.Syntax);
        Block("reference.flags", entry.Flags);
        Block("reference.example", entry.Example);
    }
}
