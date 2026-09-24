using System.Text;
using System.Text.RegularExpressions;

namespace Emu8086.Core.Assembler;

/// <summary>A line after include/macro expansion, mapped back to where the user wrote it.</summary>
public sealed record SourceLine(string Text, string File, int Line, bool FromMainFile);

/// <summary>Resolves INCLUDE files and expands MACRO / REPT blocks.</summary>
public sealed partial class Preprocessor
{
    private const int MaxDepth = 64;

    private sealed record Macro(string Name, string[] Params, List<SourceLine> Body);

    private readonly Func<string, string?> _includeResolver;
    private readonly Dictionary<string, Macro> _macros = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AsmDiagnostic> _diagnostics;
    private int _localCounter;

    /// <param name="includeResolver">Returns the text of an include file, or null if not found.</param>
    public Preprocessor(Func<string, string?> includeResolver, List<AsmDiagnostic> diagnostics)
    {
        _includeResolver = includeResolver;
        _diagnostics = diagnostics;
    }

    public List<SourceLine> Process(string source, string fileName)
    {
        var output = new List<SourceLine>();
        var lines = Split(source, fileName, true);
        Expand(lines, output, 0);
        return output;
    }

    private static List<SourceLine> Split(string source, string file, bool main) =>
        source.Replace("\r\n", "\n").Split('\n')
            .Select((text, i) => new SourceLine(text, file, i + 1, main))
            .ToList();

    private void Error(SourceLine line, string code, params string[] args) =>
        _diagnostics.Add(new AsmDiagnostic(DiagnosticSeverity.Error, code, args, line.File, line.Line, line.Text.Trim()));

    private void Expand(List<SourceLine> lines, List<SourceLine> output, int depth)
    {
        if (depth > MaxDepth)
        {
            if (lines.Count > 0) Error(lines[0], AsmErrorCode.MacroRecursion);
            return;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            List<Token> tokens;
            try
            {
                tokens = Lexer.Tokenize(Lexer.StripComment(line.Text));
            }
            catch (AsmException)
            {
                output.Add(line); // the assembler reports lexical errors with full context
                continue;
            }
            if (tokens.Count == 0)
            {
                output.Add(line);
                continue;
            }

            // name MACRO params
            if (tokens.Count >= 2 && tokens[1].Is("MACRO") && tokens[0].Kind == TokenKind.Identifier)
            {
                i = DefineMacro(lines, i, tokens);
                output.Add(line with { Text = "" });
                continue;
            }

            if (tokens[0].Is("INCLUDE"))
            {
                string name = tokens.Count > 1 && tokens[1].Kind == TokenKind.String
                    ? tokens[1].Text
                    : Lexer.StripComment(line.Text).Trim()[7..].Trim();
                string? text = _includeResolver(name);
                if (text == null)
                {
                    Error(line, AsmErrorCode.IncludeNotFound, name);
                    continue;
                }
                output.Add(line with { Text = "" });
                var included = Split(text, name, false)
                    .Select(l => l with { File = line.File, Line = line.Line, FromMainFile = line.FromMainFile, Text = l.Text })
                    .ToList();
                Expand(included, output, depth + 1);
                continue;
            }

            if (tokens[0].Is("REPT") || tokens[0].Is("REPEAT"))
            {
                int end = FindBlockEnd(lines, i);
                if (end < 0)
                {
                    Error(line, AsmErrorCode.MissingEndm);
                    return;
                }
                var body = lines.GetRange(i + 1, end - i - 1);
                long count = 0;
                try
                {
                    count = tokens.Count > 1 ? tokens[1].Number : 0;
                }
                catch (AsmException) { }
                output.Add(line with { Text = "" });
                for (long n = 0; n < count && n < 10000; n++)
                    Expand(body.Select(b => b with { File = line.File, Line = line.Line, FromMainFile = line.FromMainFile }).ToList(), output, depth + 1);
                i = end;
                continue;
            }

            int nameIndex = tokens[0].Kind == TokenKind.Identifier && tokens.Count > 1 && tokens[1].Is(":") ? 2 : 0;
            if (nameIndex < tokens.Count && tokens[nameIndex].Kind == TokenKind.Identifier
                && _macros.TryGetValue(tokens[nameIndex].Text, out var macro))
            {
                if (nameIndex == 2) output.Add(line with { Text = tokens[0].Text + ":" });
                else output.Add(line with { Text = "" });
                string invocation = Lexer.StripComment(line.Text);
                int argStart = invocation.IndexOf(tokens[nameIndex].Text, StringComparison.OrdinalIgnoreCase) + tokens[nameIndex].Text.Length;
                var args = SplitArguments(invocation[argStart..]);
                var expanded = Instantiate(macro, args)
                    .Select(b => b with { File = line.File, Line = line.Line, FromMainFile = line.FromMainFile })
                    .ToList();
                Expand(expanded, output, depth + 1);
                continue;
            }

            output.Add(line);
        }
    }

    private static int FindBlockEnd(List<SourceLine> lines, int start)
    {
        int nesting = 0;
        for (int j = start + 1; j < lines.Count; j++)
        {
            string t = Lexer.StripComment(lines[j].Text).Trim();
            var words = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            bool opens = words[0].Equals("REPT", StringComparison.OrdinalIgnoreCase)
                         || words[0].Equals("REPEAT", StringComparison.OrdinalIgnoreCase)
                         || (words.Length > 1 && words[1].Equals("MACRO", StringComparison.OrdinalIgnoreCase));
            if (opens) nesting++;
            else if (words[0].Equals("ENDM", StringComparison.OrdinalIgnoreCase))
            {
                if (nesting == 0) return j;
                nesting--;
            }
        }
        return -1;
    }

    private int DefineMacro(List<SourceLine> lines, int start, List<Token> tokens)
    {
        int end = FindBlockEnd(lines, start);
        if (end < 0)
        {
            Error(lines[start], AsmErrorCode.MissingEndm);
            end = lines.Count;
        }
        var parameters = tokens.Skip(2).Where(t => t.Kind == TokenKind.Identifier).Select(t => t.Text).ToArray();
        var body = lines.GetRange(start + 1, Math.Max(0, end - start - 1));
        _macros[tokens[0].Text] = new Macro(tokens[0].Text, parameters, body);
        return end;
    }

    /// <summary>Splits macro arguments on commas, honouring quotes and &lt;...&gt; groups.</summary>
    private static List<string> SplitArguments(string text)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        int angle = 0;
        foreach (char c in text)
        {
            if (quote != '\0')
            {
                current.Append(c);
                if (c == quote) quote = '\0';
                continue;
            }
            switch (c)
            {
                case '\'' or '"': quote = c; current.Append(c); break;
                case '<': if (angle++ > 0) current.Append(c); break;
                case '>': if (--angle > 0) current.Append(c); break;
                case ',' when angle == 0:
                    args.Add(current.ToString().Trim());
                    current.Clear();
                    break;
                default: current.Append(c); break;
            }
        }
        if (current.ToString().Trim().Length > 0 || args.Count > 0) args.Add(current.ToString().Trim());
        return args;
    }

    [GeneratedRegex(@"[A-Za-z_@?.$][A-Za-z0-9_@?.$]*")]
    private static partial Regex IdentifierRegex();

    private List<SourceLine> Instantiate(Macro macro, List<string> args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int p = 0; p < macro.Params.Length; p++)
            map[macro.Params[p]] = p < args.Count ? args[p] : "";

        var result = new List<SourceLine>();
        foreach (var bodyLine in macro.Body)
        {
            var words = Lexer.StripComment(bodyLine.Text).Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0 && words[0].Equals("LOCAL", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var local in (words.Length > 1 ? words[1] : "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    map[local] = $"??{++_localCounter:X4}";
                continue;
            }
            result.Add(bodyLine with { Text = Substitute(bodyLine.Text, map) });
        }
        return result;
    }

    /// <summary>Replaces parameter names outside string literals, except inside quoted text where the
    /// parameter is written as a whole word (MASM behaviour for &amp;param&amp;).</summary>
    private static string Substitute(string text, Dictionary<string, string> map)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == ';') { sb.Append(text, i, text.Length - i); break; }
            if (Lexer.IsIdentStart(c))
            {
                var m = IdentifierRegex().Match(text, i);
                string word = m.Value;
                sb.Append(map.TryGetValue(word, out var rep) ? rep : word);
                i += word.Length;
                continue;
            }
            if (c == '&') { i++; continue; }
            if (c is '\'' or '"')
            {
                int end = text.IndexOf(c, i + 1);
                if (end < 0) end = text.Length - 1;
                string inner = text.Substring(i + 1, Math.Max(0, end - i - 1));
                inner = Regex.Replace(inner, @"&([A-Za-z_@?][A-Za-z0-9_@?]*)&?",
                    m => map.TryGetValue(m.Groups[1].Value, out var r) ? r : m.Value);
                sb.Append(c).Append(inner).Append(c);
                i = end + 1;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}
