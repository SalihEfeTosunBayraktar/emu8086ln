using System.IO;
using System.Windows.Data;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;

namespace Emu8086.App.Services;

/// <summary>
/// Saves and restores where the user put the dockable panels. Panels are matched by ContentId,
/// so the original content (with its bindings) is reattached and titles stay localized.
/// </summary>
public sealed class DockLayoutService
{
    private const string TitleKeyPrefix = "panel.";

    private readonly DockingManager _dock;
    private readonly Dictionary<string, object> _contents = new();
    private readonly string _defaultLayout;

    /// <param name="dock">The docking manager, still in its XAML default layout.</param>
    public DockLayoutService(DockingManager dock)
    {
        _dock = dock;
        foreach (var content in dock.Layout.Descendents().OfType<LayoutContent>())
            _contents[content.ContentId] = content.Content;
        _defaultLayout = Serialize();
    }

    public IEnumerable<LayoutAnchorable> Panels => _dock.Layout.Descendents().OfType<LayoutAnchorable>();

    public LayoutAnchorable Panel(string contentId) => Panels.First(p => p.ContentId == contentId);

    public void Restore()
    {
        if (!File.Exists(AppPaths.LayoutFile)) return;
        try
        {
            Deserialize(File.ReadAllText(AppPaths.LayoutFile));
            // A layout saved by an older version may lack newer panels.
            var restored = _dock.Layout.Descendents().OfType<LayoutContent>().Select(c => c.ContentId).ToHashSet();
            if (!_contents.Keys.All(restored.Contains)) Reset();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.Xml.XmlException)
        {
            // A damaged or outdated layout file is ignored; the default layout stays.
            Reset();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UserDataDirectory);
            File.WriteAllText(AppPaths.LayoutFile, Serialize());
        }
        catch (IOException)
        {
            // The layout is a convenience; failing to save it must not block closing.
        }
    }

    public void Reset() => Deserialize(_defaultLayout);

    private string Serialize()
    {
        using var writer = new StringWriter();
        new XmlLayoutSerializer(_dock).Serialize(writer);
        return writer.ToString();
    }

    private void Deserialize(string xml)
    {
        var serializer = new XmlLayoutSerializer(_dock);
        serializer.LayoutSerializationCallback += (_, e) =>
        {
            if (e.Model.ContentId is { } id && _contents.TryGetValue(id, out var content)) e.Content = content;
            else e.Cancel = true;
        };
        using var reader = new StringReader(xml);
        serializer.Deserialize(reader);
        BindTitles();
    }

    /// <summary>Deserialized panels get their title as fixed text; bind it back to the current language.</summary>
    private void BindTitles()
    {
        foreach (var content in _dock.Layout.Descendents().OfType<LayoutContent>())
        {
            string key = TitleKeyPrefix + char.ToLowerInvariant(content.ContentId[0]) + content.ContentId[1..];
            BindingOperations.SetBinding(content, LayoutContent.TitleProperty, new Binding($"[{key}]") { Source = Loc.Instance });
        }
    }
}
