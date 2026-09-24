using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Emu8086.App.Services;
using Emu8086.App.ViewModels;

namespace Emu8086.App.Views;

/// <summary>Editor and appearance settings; every change applies immediately.</summary>
public sealed class SettingsWindow
{
    private const double MinUiFontSize = 11;
    private const double MaxUiFontSize = 18;
    private static readonly int[] IndentSizes = [2, 4, 8];

    private readonly Window _window;
    private readonly MainViewModel _vm;
    private readonly StackPanel _content = new();
    private bool _loading;

    public SettingsWindow(Window owner, MainViewModel vm)
    {
        _vm = vm;
        _window = DialogParts.Create(owner, Loc.Instance["settings.title"], new ScrollViewer
        {
            Content = _content,
            MaxHeight = 640,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        }, 480);
        Build();
    }

    public void ShowDialog() => _window.ShowDialog();

    private static AppSettings S => SettingsService.Current;

    private void Build()
    {
        _loading = true;
        _content.Children.Clear();
        var loc = Loc.Instance;

        Section(loc["settings.editor"]);
        var fonts = new ComboBox { ItemsSource = MonospaceFonts(), SelectedItem = S.EditorFontFamily };
        fonts.SelectionChanged += (_, _) => Update(() => S.EditorFontFamily = fonts.SelectedItem as string ?? AppSettings.DefaultEditorFont);
        Row(loc["settings.fontFamily"], fonts);

        var preview = new TextBlock { Text = loc["settings.preview"], Margin = new Thickness(0, 4, 0, 10) };
        void UpdatePreview()
        {
            preview.FontFamily = new FontFamily($"{S.EditorFontFamily}, Consolas");
            preview.FontSize = S.EditorFontSize;
        }
        UpdatePreview();
        SettingsService.Changed += UpdatePreview;
        _window.Closed += (_, _) => SettingsService.Changed -= UpdatePreview;

        Row(loc["settings.fontSize"], SliderWithValue(EditorView.MinFontSize, EditorView.MaxFontSize, S.EditorFontSize,
            v => S.EditorFontSize = v));
        _content.Children.Add(preview);

        var indent = new ComboBox { ItemsSource = IndentSizes, SelectedItem = S.IndentSize, Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
        indent.SelectionChanged += (_, _) => Update(() => S.IndentSize = indent.SelectedItem is int n ? n : 4);
        Row(loc["settings.indentSize"], indent);

        Check(loc["settings.lineNumbers"], S.ShowLineNumbers, v => S.ShowLineNumbers = v);
        Check(loc["settings.wordWrap"], S.WordWrap, v => S.WordWrap = v);
        Check(loc["settings.currentLine"], S.HighlightCurrentLine, v => S.HighlightCurrentLine = v);
        Check(loc["settings.autoComplete"], S.AutoComplete, v => S.AutoComplete = v);
        Check(loc["settings.completionHelp"], S.CompletionHelp, v => S.CompletionHelp = v);
        Check(loc["settings.codeAnalysis"], S.CodeAnalysis, v => S.CodeAnalysis = v);

        Section(loc["settings.appearance"]);
        Row(loc["settings.uiFontSize"], SliderWithValue(MinUiFontSize, MaxUiFontSize, S.UiFontSize, v => S.UiFontSize = v));

        var theme = new ComboBox
        {
            ItemsSource = new[] { loc["settings.themeDark"], loc["settings.themeLight"] },
            SelectedIndex = ThemeService.Current == ThemeService.Light ? 1 : 0,
        };
        theme.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            string selected = theme.SelectedIndex == 1 ? ThemeService.Light : ThemeService.Dark;
            ThemeService.Apply(selected);
            S.Theme = selected;
        };
        Row(loc["settings.theme"], theme);

        var language = new ComboBox { ItemsSource = _vm.Languages, DisplayMemberPath = nameof(LanguageInfo.Name), SelectedItem = _vm.SelectedLanguage };
        language.SelectionChanged += (_, _) =>
        {
            if (_loading || language.SelectedItem is not LanguageInfo info) return;
            _vm.SelectedLanguage = info;
            _window.Title = Loc.Instance["settings.title"];
            _window.Dispatcher.BeginInvoke(Build);
        };
        Row(loc["settings.language"], language);

        Section(loc["settings.saving"]);
        var modes = new[] { Emu8086.Core.Editing.AutoSaveMode.Off, Emu8086.Core.Editing.AutoSaveMode.Interval, Emu8086.Core.Editing.AutoSaveMode.Smart };
        var mode = new ComboBox
        {
            ItemsSource = modes.Select(m => loc["autosave." + m]).ToList(),
            SelectedIndex = Array.IndexOf(modes, S.AutoSaveMode),
        };
        mode.SelectionChanged += (_, _) => Update(() => S.AutoSaveMode = modes[Math.Max(0, mode.SelectedIndex)]);
        Row(loc["settings.autoSave"], mode);
        Row(loc["settings.autoSaveInterval"], SliderWithValue(5, 600, S.AutoSaveIntervalSeconds, v => S.AutoSaveIntervalSeconds = (int)v));
        _content.Children.Add(DialogParts.Label(loc["settings.autoSaveHint"], true));

        Section(loc["settings.tools"]);
        _content.Children.Add(DialogParts.Label(loc["settings.toolsHint"], true));
        Check(loc["settings.memoryMap"], S.MemoryMap, v => S.MemoryMap = v);
        Check(loc["settings.lineExplain"], S.LineExplain, v => S.LineExplain = v);
        Check(loc["settings.cycleCounter"], S.CycleCounter, v => S.CycleCounter = v);
        Check(loc["settings.compareRuns"], S.CompareRuns, v => S.CompareRuns = v);

        Section(loc["settings.emulator"]);
        Check(loc["settings.timeline"], S.ExecutionTimeline, v => S.ExecutionTimeline = v);
        Check(loc["settings.externalIo"], S.ExternalIo, v => S.ExternalIo = v);
        _content.Children.Add(DialogParts.Label(loc.Format("settings.externalIoHint", AppPaths.ExternalIoFile), true));
        Check(loc["settings.checkUpdates"], S.CheckForUpdates, v => S.CheckForUpdates = v);

        var reset = DialogParts.Button("settings.reset", false, () =>
        {
            SettingsService.ResetAppearance();
            Build();
        });
        var buttons = DialogParts.Buttons(reset, DialogParts.Button("button.close", true, () => _window.Close()));
        _content.Children.Add(buttons);
        _loading = false;
    }

    private void Update(Action change)
    {
        if (_loading) return;
        change();
        SettingsService.NotifyChanged();
    }

    private void Section(string title)
    {
        var text = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, _content.Children.Count == 0 ? 0 : 16, 0, 8) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Muted");
        _content.Children.Add(text);
    }

    private void Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        _content.Children.Add(grid);
    }

    private void Check(string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 6) };
        box.Click += (_, _) => Update(() => set(box.IsChecked == true));
        _content.Children.Add(box);
    }

    private FrameworkElement SliderWithValue(double min, double max, double value, Action<double> set)
    {
        var valueText = new TextBlock { Text = value.ToString("0"), Width = 28, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            valueText.Text = e.NewValue.ToString("0");
            Update(() => set(e.NewValue));
        };
        var panel = new DockPanel();
        DockPanel.SetDock(valueText, Dock.Right);
        panel.Children.Add(valueText);
        panel.Children.Add(slider);
        return panel;
    }

    /// <summary>Installed fonts whose narrow and wide glyphs have the same advance width.</summary>
    private static List<string> MonospaceFonts()
    {
        var result = new List<string>();
        foreach (var family in Fonts.SystemFontFamilies)
        {
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out var glyphs)) continue;
            if (!glyphs.CharacterToGlyphMap.TryGetValue('i', out ushort narrow) || !glyphs.CharacterToGlyphMap.TryGetValue('W', out ushort wide)) continue;
            if (Math.Abs(glyphs.AdvanceWidths[narrow] - glyphs.AdvanceWidths[wide]) > 0.001) continue;
            string name = family.FamilyNames.Values.FirstOrDefault() ?? family.Source;
            result.Add(name);
        }
        if (!result.Contains(S.EditorFontFamily)) result.Add(S.EditorFontFamily);
        return result.Distinct().Order().ToList();
    }
}
