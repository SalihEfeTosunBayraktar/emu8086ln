using System.Text;

namespace Emu8086.Core.Assembler;

public enum TokenKind { Identifier, Number, String, Punct, End }

public readonly record struct Token(TokenKind Kind, string Text, long Number = 0)
{
    public bool Is(string text) => Kind != TokenKind.String && string.Equals(Text, text, StringComparison.OrdinalIgnoreCase);
    public string Upper => Text.ToUpperInvariant();
    public override string ToString() => Kind == TokenKind.String ? $"'{Text}'" : Text;
}

/// <summary>Splits one source line into tokens (comments already removed).</summary>
public static class Lexer
{
    public static List<Token> Tokenize(string line)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '\'' || c == '"')
            {
                int end = line.IndexOf(c, i + 1);
                var sb = new StringBuilder();
                // Doubled quote inside a string is a literal quote.
                while (end >= 0 && end + 1 < line.Length && line[end + 1] == c)
                {
                    sb.Append(line, i + 1, end - i - 1).Append(c);
                    i = end + 1;
                    end = line.IndexOf(c, i + 1);
                }
                if (end < 0) throw new AsmException(AsmErrorCode.UnterminatedString);
                sb.Append(line, i + 1, end - i - 1);
                tokens.Add(new Token(TokenKind.String, sb.ToString()));
                i = end + 1;
                continue;
            }

            if (char.IsDigit(c))
            {
                int start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                string text = line[start..i];
                tokens.Add(new Token(TokenKind.Number, text, ParseNumber(text)));
                continue;
            }

            if (IsIdentStart(c))
            {
                int start = i;
                while (i < line.Length && IsIdentPart(line[i])) i++;
                tokens.Add(new Token(TokenKind.Identifier, line[start..i]));
                continue;
            }

            if (i + 1 < line.Length)
            {
                string two = line.Substring(i, 2);
                if (two is "<=" or ">=" or "==" or "!=" or "<>")
                {
                    tokens.Add(new Token(TokenKind.Punct, two));
                    i += 2;
                    continue;
                }
            }
            tokens.Add(new Token(TokenKind.Punct, c.ToString()));
            i++;
        }
        return tokens;
    }

    public static bool IsIdentStart(char c) => char.IsLetter(c) || c is '_' or '@' or '?' or '.' or '$';
    public static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '@' or '?' or '$' or '.';

    public static long ParseNumber(string text)
    {
        string t = text.Replace("_", "").ToLowerInvariant();
        try
        {
            if (t.StartsWith("0x")) return Convert.ToInt64(t[2..], 16);
            char last = t[^1];
            string body = t[..^1];
            switch (last)
            {
                case 'h': return Convert.ToInt64(body, 16);
                case 'o':
                case 'q': return Convert.ToInt64(body, 8);
                case 'b' when body.Length > 0 && body.All(ch => ch is '0' or '1'): return Convert.ToInt64(body, 2);
                case 'd' when body.Length > 0 && body.All(char.IsDigit): return long.Parse(body);
            }
            if (t.All(char.IsDigit)) return long.Parse(t);
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
        }
        throw new AsmException(AsmErrorCode.InvalidNumber, text);
    }

    /// <summary>Removes a ';' comment, ignoring semicolons inside string literals.</summary>
    public static string StripComment(string line)
    {
        char quote = '\0';
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '\'' or '"') quote = c;
            else if (c == ';') return line[..i];
        }
        return line;
    }
}
