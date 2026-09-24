using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Emu8086.App.Controls;
using Emu8086.App.Services;
using Emu8086.App.ViewModels;
using Emu8086.Core.Assembler;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace Emu8086.App.Views;

public partial class EditorView : UserControl
{
    public const double MinFontSize = 9;
    public const double MaxFontSize = 32;
    private const int MinPrefixForCompletion = 2;

    private static readonly string[] Keywords =
        Assembler8086.Mnemonics.Concat(AsmHighlighting.RegisterNames).Concat(AsmHighlighting.Directives)
            .Select(k => k.ToUpperInvariant()).Distinct().Order().ToArray();

    private readonly BreakpointMargin _margin = new();
    private readonly LineHighlightRenderer _renderer = new();
    private DocumentViewModel? _model;
    private CompletionWindow? _completion;

    public EditorView()
    {
        InitializeComponent();
        Editor.TextArea.LeftMargins.Insert(0, _margin);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_model != null) _model.CaretLine = Editor.TextArea.Caret.Line;
        };
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.PreviewMouseWheel += OnPreviewMouseWheel;

        ApplyTheme();
        ApplySettings();
        ThemeService.ThemeChanged += ApplyTheme;
        SettingsService.Changed += ApplySettings;
        DataContextChanged += (_, e) => Attach(e.NewValue as DocumentViewModel);
        Unloaded += (_, _) =>
        {
            ThemeService.ThemeChanged -= ApplyTheme;
            SettingsService.Changed -= ApplySettings;
        };
    }

    public TextArea TextArea => Editor.TextArea;

    private void ApplyTheme()
    {
        Editor.SyntaxHighlighting = AsmHighlighting.Create();
        Editor.TextArea.TextView.CurrentLineBackground = ThemeService.Resource<Brush>("Bg.Panel");
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(ThemeService.Resource<Brush>("Border"), 1);
        Editor.TextArea.SelectionBrush = ThemeService.Resource<Brush>("Bg.Selected");
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.Caret.CaretBrush = ThemeService.Resource<Brush>("Accent");
        Redraw();
    }

    private void ApplySettings()
    {
        var s = SettingsService.Current;
        Editor.FontFamily = new FontFamily($"{s.EditorFontFamily}, Consolas");
        Editor.FontSize = s.EditorFontSize;
        Editor.ShowLineNumbers = s.ShowLineNumbers;
        Editor.WordWrap = s.WordWrap;
        Editor.Options.IndentationSize = s.IndentSize;
        Editor.Options.HighlightCurrentLine = s.HighlightCurrentLine;
    }

    private void Attach(DocumentViewModel? model)
    {
        if (_model != null)
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.BreakpointsChanged -= Redraw;
            _model.NavigateRequested -= Navigate;
        }
        _model = model;
        _margin.Model = model;
        _renderer.Model = model;
        if (model == null)
        {
            Editor.Document = new TextDocument();
            return;
        }
        Editor.Document = model.Document;
        model.PropertyChanged += OnModelChanged;
        model.BreakpointsChanged += Redraw;
        model.NavigateRequested += Navigate;
        Redraw();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.ExecutionLine))
        {
            if (_model?.ExecutionLine is int line) Editor.ScrollTo(line, 0);
            Redraw();
        }
        else if (e.PropertyName == nameof(DocumentViewModel.ErrorLines)) Redraw();
    }

    private void Redraw()
    {
        Editor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
        _margin.InvalidateVisual();
    }

    private void Navigate(int line)
    {
        if (line < 1 || line > Editor.Document.LineCount) return;
        Editor.TextArea.Caret.Line = line;
        Editor.TextArea.Caret.Column = 1;
        Editor.ScrollTo(line, 1);
        Editor.TextArea.Focus();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SettingsService.Current.EditorFontSize = Math.Clamp(Editor.FontSize + Math.Sign(e.Delta), MinFontSize, MaxFontSize);
        SettingsService.NotifyChanged();
        e.Handled = true;
    }

    #region Completion

    private void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (!SettingsService.Current.AutoComplete || _completion != null || e.Text.Length != 1 || !char.IsLetter(e.Text[0])) return;
        var (start, prefix) = CurrentWord();
        if (prefix.Length < MinPrefixForCompletion || IsInComment()) return;

        var candidates = Keywords.Concat(DocumentLabels())
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !k.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToList();
        if (candidates.Count == 0) return;

        bool lower = prefix.All(c => !char.IsLetter(c) || char.IsLower(c));
        _completion = new CompletionWindow(Editor.TextArea)
        {
            StartOffset = start,
            Background = ThemeService.Resource<Brush>("Bg.Panel2"),
            Foreground = ThemeService.Resource<Brush>("Fg.Primary"),
            BorderBrush = ThemeService.Resource<Brush>("Border.Strong"),
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
        };
        _completion.CompletionList.ListBox.Background = ThemeService.Resource<Brush>("Bg.Panel2");
        _completion.CompletionList.ListBox.Foreground = ThemeService.Resource<Brush>("Fg.Primary");
        foreach (var c in candidates)
            _completion.CompletionList.CompletionData.Add(new CompletionItem(lower && Keywords.Contains(c) ? c.ToLowerInvariant() : c));
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }

    private (int Start, string Prefix) CurrentWord()
    {
        int offset = Editor.CaretOffset;
        int start = offset;
        var doc = Editor.Document;
        while (start > 0 && Lexer.IsIdentPart(doc.GetCharAt(start - 1))) start--;
        return (start, doc.GetText(start, offset - start));
    }

    private bool IsInComment()
    {
        var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        string before = Editor.Document.GetText(line.Offset, Editor.CaretOffset - line.Offset);
        return Lexer.StripComment(before).Length != before.Length;
    }

    private IEnumerable<string> DocumentLabels() =>
        Regex.Matches(Editor.Document.Text, @"^\s*([A-Za-z_@?][\w@?$]*)\s*(?::|\s+(?:db|dw|dd|proc|equ|label)\b)",
                RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value);

    private sealed class CompletionItem(string text) : ICompletionData
    {
        public System.Windows.Media.ImageSource? Image => null;
        public string Text { get; } = text;
        public object Content => Text;
        public object? Description => null;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, Text);
    }

    #endregion
}
