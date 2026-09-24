using Emu8086.Core.Assembler;

namespace Emu8086.Core.Analysis;

/// <summary>Warning identifiers; the user-facing text lives in the language files ("asm.&lt;code&gt;").</summary>
public static class LintCode
{
    public const string UnreachableCode = "UnreachableCode";
    public const string PushPopMismatch = "PushPopMismatch";
    public const string JumpToNumber = "JumpToNumber";
}

/// <summary>
/// Line-based checks for common beginner mistakes that still assemble: code that can never run,
/// procedures that leave the stack unbalanced and jumps to a raw number instead of a label.
/// Only the given file is checked; macros and includes are skipped to avoid false alarms.
/// </summary>
public static class CodeLinter
{
    private static readonly HashSet<string> Instructions = new(Assembler8086.Mnemonics, StringComparer.OrdinalIgnoreCase);

    /// <summary>After these, the next instruction only runs if something jumps to it (and that needs a label).</summary>
    private static readonly HashSet<string> Terminators = new(StringComparer.OrdinalIgnoreCase)
        { "JMP", "RET", "RETN", "RETF", "IRET", "HLT" };

    private static readonly HashSet<string> Jumps = new(StringComparer.OrdinalIgnoreCase)
    {
        "JMP", "CALL", "LOOP", "LOOPE", "LOOPZ", "LOOPNE", "LOOPNZ", "JCXZ",
        "JA", "JAE", "JB", "JBE", "JC", "JE", "JG", "JGE", "JL", "JLE", "JNA", "JNAE", "JNB", "JNBE", "JNC", "JNE",
        "JNG", "JNGE", "JNL", "JNLE", "JNO", "JNP", "JNS", "JNZ", "JO", "JP", "JPE", "JPO", "JS", "JZ",
    };

    private sealed class ProcState(int line, string text)
    {
        public int Line { get; } = line;
        public string Text { get; } = text;
        public int Pushes { get; set; }
        public int Pops { get; set; }
        public int Returns { get; set; }
    }

    public static List<AsmDiagnostic> Analyze(string source, string fileName)
    {
        var warnings = new List<AsmDiagnostic>();
        void Warn(string code, int line, string text, params string[] args) =>
            warnings.Add(new AsmDiagnostic(DiagnosticSeverity.Warning, code, args, fileName, line, text.Trim()));

        bool inMacro = false;
        bool unreachable = false;
        ProcState? proc = null;
        string[] lines = source.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int number = i + 1;
            string raw = lines[i].TrimEnd('\r');
            var words = Words(Lexer.StripComment(raw), out bool labelled);
            if (labelled) unreachable = false;
            if (words.Count == 0) continue;

            string first = words[0].ToUpperInvariant();
            string second = words.Count > 1 ? words[1].ToUpperInvariant() : "";
            if (inMacro)
            {
                if (first == "ENDM") inMacro = false;
                continue;
            }
            if (second == "MACRO")
            {
                inMacro = true;
                continue;
            }
            if (second == "PROC")
            {
                proc = new ProcState(number, raw);
                unreachable = false;
                continue;
            }
            if (second == "ENDP")
            {
                if (proc is { Returns: 1 } p && p.Pushes != p.Pops)
                    Warn(LintCode.PushPopMismatch, p.Line, p.Text, p.Pushes.ToString(), p.Pops.ToString());
                proc = null;
                unreachable = false;
                continue;
            }
            if (!Instructions.Contains(first))
            {
                // Directives, data definitions and macro calls start a new, reachable block.
                unreachable = false;
                continue;
            }

            if (unreachable) Warn(LintCode.UnreachableCode, number, raw);
            unreachable = Terminators.Contains(first);

            if (proc != null)
            {
                if (first is "PUSH" or "PUSHF") proc.Pushes++;
                else if (first is "POP" or "POPF") proc.Pops++;
                else if (first is "RET" or "RETN" or "RETF") proc.Returns++;
            }
            if (Jumps.Contains(first) && IsRawNumber(words.Skip(1).ToList()))
                Warn(LintCode.JumpToNumber, number, raw, words[^1]);
        }
        return warnings;
    }

    /// <summary>Splits a comment-free line into words, removing leading "label:" prefixes.</summary>
    private static List<string> Words(string line, out bool labelled)
    {
        labelled = false;
        var words = line.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 0)
        {
            string w = words[0];
            int colon = w.IndexOf(':');
            if (colon <= 0 || !Lexer.IsIdentStart(w[0]) || !w[..colon].All(Lexer.IsIdentPart)) break;
            labelled = true;
            string rest = w[(colon + 1)..];
            if (rest.Length == 0) words.RemoveAt(0);
            else words[0] = rest;
        }
        return words;
    }

    /// <summary>A lone numeric target such as "jmp 105h"; far "seg:off" targets are deliberate and skipped.</summary>
    private static bool IsRawNumber(List<string> operands)
    {
        var target = operands.Where(o => !o.Equals("SHORT", StringComparison.OrdinalIgnoreCase)
            && !o.Equals("NEAR", StringComparison.OrdinalIgnoreCase)).ToList();
        if (target.Count != 1 || target[0].Contains(':') || !char.IsDigit(target[0][0])) return false;
        try
        {
            Lexer.ParseNumber(target[0]);
            return true;
        }
        catch (AsmException)
        {
            return false;
        }
    }
}
