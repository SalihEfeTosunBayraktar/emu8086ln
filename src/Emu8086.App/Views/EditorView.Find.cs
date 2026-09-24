using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Emu8086.App.Services;

namespace Emu8086.App.Views;

/// <summary>Find / replace bar and go-to-line for the editor.</summary>
public partial class EditorView
{
    private void InitializeFind()
    {
        Editor.TextArea.PreviewKeyDown += OnEditorKeyDown;
        ReplaceBox.Visibility = Visibility.Collapsed;
        ReplaceButtons.Visibility = Visibility.Collapsed;
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.F when ctrl:
                OpenFind(replace: false);
                break;
            case Key.H when ctrl:
                OpenFind(replace: true);
                break;
            case Key.G when ctrl:
                GoToLine();
                break;
            case Key.F3:
                Find(forward: !shift);
                break;
            case Key.Escape when FindBar.Visibility == Visibility.Visible:
                CloseFind();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OpenFind(bool replace)
    {
        FindBar.Visibility = Visibility.Visible;
        ReplaceBox.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceButtons.Visibility = ReplaceBox.Visibility;
        string selected = Editor.TextArea.Selection.GetText();
        if (selected.Length > 0 && !selected.Contains('\n')) FindBox.Text = selected;
        FindBox.Focus();
        FindBox.SelectAll();
        UpdateMatchCount();
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        Editor.TextArea.Focus();
    }

    private Regex? Pattern()
    {
        string text = FindBox.Text;
        if (text.Length == 0) return null;
        string pattern = Regex.Escape(text);
        if (WholeWord.IsChecked == true) pattern = $@"(?<![\w@?$.]){pattern}(?![\w@?$])";
        var options = MatchCase.IsChecked == true ? RegexOptions.None : RegexOptions.IgnoreCase;
        return new Regex(pattern, options | RegexOptions.CultureInvariant);
    }

    private void UpdateMatchCount()
    {
        var regex = Pattern();
        FindStatus.Text = regex == null ? "" : Loc.Instance.Format("find.count", regex.Matches(Editor.Document.Text).Count);
    }

    /// <summary>Selects the next (or previous) match, wrapping around the document.</summary>
    private bool Find(bool forward)
    {
        var regex = Pattern();
        if (regex == null)
        {
            OpenFind(replace: false);
            return false;
        }
        string text = Editor.Document.Text;
        var matches = regex.Matches(text).ToList();
        if (matches.Count == 0)
        {
            FindStatus.Text = Loc.Instance["find.notFound"];
            return false;
        }

        int caret = Editor.CaretOffset;
        int selectionStart = Editor.SelectionLength > 0 ? Editor.SelectionStart : caret;
        var match = forward
            ? matches.FirstOrDefault(m => m.Index >= caret) ?? matches[0]
            : matches.LastOrDefault(m => m.Index < selectionStart) ?? matches[^1];

        Editor.Select(match.Index, match.Length);
        Editor.CaretOffset = match.Index + match.Length;
        var location = Editor.Document.GetLocation(match.Index);
        Editor.ScrollTo(location.Line, location.Column);
        FindStatus.Text = Loc.Instance.Format("find.position", matches.IndexOf(match) + 1, matches.Count);
        return true;
    }

    private bool SelectionMatches(Regex regex)
    {
        string selected = Editor.SelectedText;
        var m = regex.Match(selected);
        return selected.Length > 0 && m.Success && m.Index == 0 && m.Length == selected.Length;
    }

    private void ReplaceOne()
    {
        var regex = Pattern();
        if (regex == null) return;
        if (SelectionMatches(regex))
            Editor.Document.Replace(Editor.SelectionStart, Editor.SelectionLength, ReplaceBox.Text);
        Find(forward: true);
    }

    private void ReplaceAll()
    {
        var regex = Pattern();
        if (regex == null) return;
        var matches = regex.Matches(Editor.Document.Text).ToList();
        if (matches.Count == 0)
        {
            FindStatus.Text = Loc.Instance["find.notFound"];
            return;
        }
        // One undo step for the whole operation; replace from the end so offsets stay valid.
        Editor.Document.BeginUpdate();
        try
        {
            for (int i = matches.Count - 1; i >= 0; i--)
                Editor.Document.Replace(matches[i].Index, matches[i].Length, ReplaceBox.Text);
        }
        finally
        {
            Editor.Document.EndUpdate();
        }
        FindStatus.Text = Loc.Instance.Format("find.replaced", matches.Count);
    }

    private void GoToLine()
    {
        var loc = Loc.Instance;
        var dialog = new InputDialog(loc["goto.title"], loc.Format("goto.prompt", Editor.Document.LineCount), Editor.TextArea.Caret.Line.ToString())
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() != true || !int.TryParse(dialog.Value, out int line)) return;
        Navigate(Math.Clamp(line, 1, Editor.Document.LineCount));
    }

    private void OnFindKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Find(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
                e.Handled = true;
                break;
            case Key.Escape:
                CloseFind();
                e.Handled = true;
                break;
        }
    }

    private void OnReplaceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReplaceOne();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void OnFindTextChanged(object sender, RoutedEventArgs e) => UpdateMatchCount();
    private void OnFindNext(object sender, RoutedEventArgs e) => Find(forward: true);
    private void OnFindPrevious(object sender, RoutedEventArgs e) => Find(forward: false);
    private void OnReplaceOne(object sender, RoutedEventArgs e) => ReplaceOne();
    private void OnReplaceAll(object sender, RoutedEventArgs e) => ReplaceAll();
    private void OnCloseFind(object sender, RoutedEventArgs e) => CloseFind();
}
