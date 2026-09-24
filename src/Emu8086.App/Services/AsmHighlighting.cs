using System.IO;
using System.Security;
using System.Text;
using System.Windows.Media;
using System.Xml;
using Emu8086.Core.Assembler;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Emu8086.App.Services;

/// <summary>
/// Builds the editor's syntax highlighting from the assembler's own keyword lists,
/// coloured with the active theme's Syntax.* brushes.
/// </summary>
public static class AsmHighlighting
{
    public static readonly string[] Directives =
    [
        "DB", "DW", "DD", "DQ", "DT", "EQU", "ORG", "PROC", "ENDP", "SEGMENT", "ENDS", "END", "ASSUME", "MACRO", "ENDM",
        "LOCAL", "INCLUDE", "OFFSET", "SEG", "PTR", "BYTE", "WORD", "DWORD", "FAR", "NEAR", "SHORT", "DUP", "LABEL",
        "EVEN", "ALIGN", "NAME", "TITLE", "REPT", "IF", "IFDEF", "IFNDEF", "ELSE", "ENDIF", "TYPE", "SIZE", "LENGTH",
        "LOW", "HIGH", "MOD", "SHL", "SHR", "NOT", "PUBLIC", "EXTRN", ".MODEL", ".STACK", ".DATA", ".CODE", ".STARTUP", ".EXIT",
        "SMALL", "TINY", "MEDIUM", "LARGE", "COMPACT", "HUGE",
    ];

    public static readonly string[] RegisterNames =
        Registers.Reg8.Concat(Registers.Reg16).Concat(Registers.Seg).ToArray();

    public static IHighlightingDefinition Create()
    {
        string Color(string key) => ((SolidColorBrush)ThemeService.Resource<Brush>(key)).Color.ToString();
        string Words(IEnumerable<string> words) =>
            string.Join("", words.Distinct(StringComparer.OrdinalIgnoreCase).Select(w => $"<Word>{SecurityElement.Escape(w)}</Word>"));

        var xshd = new StringBuilder();
        xshd.Append("""<SyntaxDefinition name="ASM8086" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">""");
        xshd.Append($"""<Color name="Comment" foreground="{Color("Syntax.Comment")}" fontStyle="italic"/>""");
        xshd.Append($"""<Color name="String" foreground="{Color("Syntax.String")}"/>""");
        xshd.Append($"""<Color name="Number" foreground="{Color("Syntax.Number")}"/>""");
        xshd.Append($"""<Color name="Mnemonic" foreground="{Color("Syntax.Mnemonic")}" fontWeight="bold"/>""");
        xshd.Append($"""<Color name="Register" foreground="{Color("Syntax.Register")}"/>""");
        xshd.Append($"""<Color name="Directive" foreground="{Color("Syntax.Directive")}"/>""");
        xshd.Append($"""<Color name="Label" foreground="{Color("Syntax.Label")}"/>""");
        xshd.Append("""<RuleSet ignoreCase="true">""");
        xshd.Append("""<Span color="Comment"><Begin>;</Begin></Span>""");
        xshd.Append("""<Span color="String"><Begin>'</Begin><End>'</End></Span>""");
        xshd.Append("""<Span color="String"><Begin>"</Begin><End>"</End></Span>""");
        xshd.Append("""<Span color="Directive"><Begin>\#</Begin><End>\#</End></Span>""");
        xshd.Append($"""<Keywords color="Mnemonic">{Words(Assembler8086.Mnemonics)}</Keywords>""");
        xshd.Append($"""<Keywords color="Register">{Words(RegisterNames)}</Keywords>""");
        xshd.Append("""<Rule color="Directive">(?&lt;![\w.])\.(model|stack|data\??|code|startup|exit|const|8086|186|286)\b</Rule>""");
        xshd.Append($"""<Keywords color="Directive">{Words(Directives.Where(d => !d.StartsWith('.')))}</Keywords>""");
        xshd.Append("""<Rule color="Label">^\s*[A-Za-z_@?][\w@?$]*(?=\s*:)</Rule>""");
        xshd.Append("""<Rule color="Number">\b(0x[0-9a-f]+|[0-9][0-9a-f]*h|[01]+b|[0-7]+[oq]|[0-9]+d?)\b</Rule>""");
        xshd.Append("</RuleSet></SyntaxDefinition>");

        using var reader = XmlReader.Create(new StringReader(xshd.ToString()));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
