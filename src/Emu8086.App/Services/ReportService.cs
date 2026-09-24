using System.IO;
using System.Net;
using System.Text;

namespace Emu8086.App.Services;

public sealed record ReportData(
    string Title,
    string Summary,
    string Source,
    byte[]? ScreenPng,
    IReadOnlyList<(string Name, string Value)> Registers,
    IReadOnlyList<(string Name, bool Set)> Flags,
    IReadOnlyList<(string Time, string Text)> Output);

/// <summary>Fills the HTML report template (config/report/template.html) with a program's state.</summary>
public static class ReportService
{
    public static string Build(ReportData data)
    {
        var loc = Loc.Instance;
        string E(string text) => WebUtility.HtmlEncode(text);
        string Cells(IEnumerable<(string Name, string Value)> items) =>
            string.Concat(items.Select(i => $"<div><b>{E(i.Name)}</b>{E(i.Value)}</div>"));

        string screen = data.ScreenPng == null
            ? $"<p class=\"meta\">{E(loc["report.noScreen"])}</p>"
            : $"<img alt=\"{E(loc["report.screen"])}\" src=\"data:image/png;base64,{Convert.ToBase64String(data.ScreenPng)}\">";

        var values = new Dictionary<string, string>
        {
            ["lang"] = E(loc.CurrentCode),
            ["title"] = E(data.Title),
            ["summary"] = E(data.Summary),
            ["h.screen"] = E(loc["report.screen"]),
            ["h.registers"] = E(loc["report.registers"]),
            ["h.flags"] = E(loc["report.flags"]),
            ["h.output"] = E(loc["report.output"]),
            ["h.source"] = E(loc["report.source"]),
            ["screen"] = screen,
            ["registers"] = Cells(data.Registers),
            ["flags"] = Cells(data.Flags.Select(f => (f.Name, f.Set ? "1" : "0"))),
            ["output"] = string.Concat(data.Output.Select(o => $"<tr><td>{E(o.Time)}</td><td>{E(o.Text)}</td></tr>")),
            ["source"] = E(data.Source),
        };

        var html = new StringBuilder(File.ReadAllText(AppPaths.ReportTemplate));
        foreach (var (key, value) in values) html.Replace("{{" + key + "}}", value);
        return html.ToString();
    }
}
