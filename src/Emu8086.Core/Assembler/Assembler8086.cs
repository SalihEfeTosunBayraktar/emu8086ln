namespace Emu8086.Core.Assembler;

/// <summary>
/// Multi-pass assembler for emu8086/MASM-style 8086 source. Passes repeat until every
/// label keeps its address, so forward references and short/long jump choices settle.
/// </summary>
public sealed partial class Assembler8086 : IExprContext
{
    private const int MaxPasses = 16;
    /// <summary>From this pass on, instructions never shrink (padding with NOP) to guarantee convergence.</summary>
    private const int NoShrinkPass = 4;

    private static readonly HashSet<string> NamedDirectives = new(StringComparer.OrdinalIgnoreCase)
        { "DB", "DW", "DD", "DQ", "DT", "EQU", "=", "PROC", "ENDP", "SEGMENT", "ENDS", "LABEL", "GROUP", "STRUC", "ENDS" };

    private static readonly Dictionary<string, int> DataSizes = new(StringComparer.OrdinalIgnoreCase)
        { ["DB"] = 1, ["DW"] = 2, ["DD"] = 4, ["DQ"] = 8, ["DT"] = 10 };

    private static readonly HashSet<string> IgnoredDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "ASSUME", "PUBLIC", "EXTRN", "EXTERN", "TITLE", "SUBTTL", "PAGE", "NAME", ".8086", ".8087", ".186",
        ".286", ".386", ".LIST", ".NOLIST", ".XLIST", ".LALL", ".SALL", ".XALL", "OPTION", ".RADIX", "COMMENT",
    };

    private readonly List<AsmSegment> _segments = new();
    private readonly Dictionary<string, int> _segmentIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, int> _lineSizes = new();
    private readonly Stack<int> _segmentStack = new();
    private readonly Stack<Symbol> _procStack = new();
    private readonly Stack<(bool Active, bool Taken)> _conditions = new();

    private List<AsmDiagnostic> _diagnostics = new();
    private List<ListingEntry> _listing = new();
    private List<string> _devices = new();
    private readonly Dictionary<string, ushort> _registerPresets = new();
    private OutputFormat _format;
    private bool _usesSegments;
    private int _current = -1;
    private int _pass;
    private bool _changed;
    private bool _ended;
    private string? _entryName;
    private SourceLine _line = new("", "", 0, true);

    public int CurrentSegment => EnsureSegment();
    public long CurrentLocation => _segments[EnsureSegment()].Location;

    public AssemblyResult Assemble(string source, string fileName, Func<string, string?> includeResolver)
    {
        var prepDiagnostics = new List<AsmDiagnostic>();
        var lines = new Preprocessor(includeResolver, prepDiagnostics).Process(source, fileName);
        PreScan(lines);

        var result = new AssemblyResult { Format = _format };
        for (_pass = 1; _pass <= MaxPasses; _pass++)
        {
            RunPass(lines);
            if (_pass >= 2 && !_changed) break;
        }
        if (_pass > MaxPasses)
            _diagnostics.Add(new AsmDiagnostic(DiagnosticSeverity.Error, AsmErrorCode.TooManyPasses, [], fileName, 0, ""));

        result.Passes = Math.Min(_pass, MaxPasses);
        result.Diagnostics.AddRange(prepDiagnostics);
        result.Diagnostics.AddRange(_diagnostics);
        result.Segments.AddRange(_segments);
        foreach (var s in _symbols) result.Symbols[s.Key] = s.Value;
        result.Listing.AddRange(_listing);
        result.Devices.AddRange(_devices.Distinct(StringComparer.OrdinalIgnoreCase));
        foreach (var preset in _registerPresets) result.RegisterPresets[preset.Key] = preset.Value;

        if (_entryName != null && _symbols.TryGetValue(_entryName, out var entry))
            result.Entry = (entry.Segment, (int)entry.Value);
        if (_segments.All(s => s.Bytes.Count == 0))
            result.Diagnostics.Add(new AsmDiagnostic(DiagnosticSeverity.Warning, AsmErrorCode.NoCode, [], fileName, 0, ""));
        return result;
    }

    /// <summary>Decides the output format before the first pass.</summary>
    private void PreScan(List<SourceLine> lines)
    {
        OutputFormat? explicitFormat = null;
        foreach (var line in lines)
        {
            string t = Lexer.StripComment(line.Text).Trim();
            if (t.StartsWith('#'))
            {
                string u = t.ToUpperInvariant().Replace(" ", "");
                if (u.StartsWith("#MAKE_COM")) explicitFormat = OutputFormat.Com;
                else if (u.StartsWith("#MAKE_EXE")) explicitFormat = OutputFormat.Exe;
                else if (u.StartsWith("#MAKE_BIN")) explicitFormat = OutputFormat.Bin;
                else if (u.StartsWith("#MAKE_BOOT")) explicitFormat = OutputFormat.Boot;
                continue;
            }
            var words = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            if (words[0].StartsWith(".MODEL", StringComparison.OrdinalIgnoreCase)
                || words[0].Equals(".CODE", StringComparison.OrdinalIgnoreCase)
                || words[0].Equals(".DATA", StringComparison.OrdinalIgnoreCase)
                || (words.Length > 1 && words[1].Equals("SEGMENT", StringComparison.OrdinalIgnoreCase)))
                _usesSegments = true;
        }
        _format = explicitFormat ?? (_usesSegments ? OutputFormat.Exe : OutputFormat.Com);
    }

    private void RunPass(List<SourceLine> lines)
    {
        _diagnostics = new List<AsmDiagnostic>();
        _listing = new List<ListingEntry>();
        _devices = new List<string>();
        _changed = false;
        _ended = false;
        _current = -1;
        _segmentStack.Clear();
        _procStack.Clear();
        _conditions.Clear();
        foreach (var seg in _segments)
        {
            seg.Bytes.Clear();
            seg.Fixups.Clear();
            seg.Location = 0;
        }

        if (!_usesSegments) EnsureSegment();

        for (int i = 0; i < lines.Count && !_ended; i++)
        {
            _line = lines[i];
            int startSeg = _current;
            int startLoc = startSeg >= 0 ? _segments[startSeg].Location : 0;
            var kind = LineKind.Other;
            try
            {
                kind = ProcessLine(_line.Text);
            }
            catch (AsmException e)
            {
                Report(e.Code, e.Args);
            }

            if (kind == LineKind.Other || startSeg < 0 || _current != startSeg) continue;
            var seg = _segments[_current];
            int size = seg.Location - startLoc;

            if (kind == LineKind.Instruction)
            {
                _lineSizes.TryGetValue(i, out int previous);
                if (_pass >= NoShrinkPass && size < previous)
                {
                    while (size < previous) { seg.Emit(0x90); size++; }
                }
                _lineSizes[i] = size;
            }
            if (size > 0)
                _listing.Add(new ListingEntry(_line.Line, _line.FromMainFile, _current, startLoc, size, kind == LineKind.Instruction, _line.Text.Trim()));
        }

        if (_procStack.Count > 0) Report(AsmErrorCode.MissingEndp, _procStack.Peek().Name);
        if (_conditions.Count > 0) Report(AsmErrorCode.ConditionalMismatch);
    }

    private void Report(string code, params string[] args) =>
        _diagnostics.Add(new AsmDiagnostic(DiagnosticSeverity.Error, code, args, _line.File, _line.Line, _line.Text.Trim()));

    #region IExprContext

    public Symbol? Lookup(string name)
    {
        if (_symbols.TryGetValue(name, out var s)) return s;
        if (_pass > 1) throw new AsmException(AsmErrorCode.UndefinedSymbol, name);
        return null;
    }

    public int SegmentIndex(string name)
    {
        string key = name.ToUpperInvariant() switch
        {
            "@DATA" => "DATA",
            "@CODE" => "CODE",
            "@STACK" => "STACK",
            _ => name,
        };
        return _segmentIndex.TryGetValue(key, out int i) ? i : -1;
    }

    #endregion

    #region Segments and symbols

    private int EnsureSegment()
    {
        if (_current >= 0) return _current;
        _current = GetOrCreateSegment("CODE", SegmentKind.Code);
        var seg = _segments[_current];
        if (seg.Location == 0 && seg.Bytes.Count == 0)
            seg.Location = _format == OutputFormat.Com ? 0x100 : 0;
        return _current;
    }

    private int GetOrCreateSegment(string name, SegmentKind kind)
    {
        if (_segmentIndex.TryGetValue(name, out int index)) return index;
        _segments.Add(new AsmSegment { Name = name.ToUpperInvariant(), Kind = kind });
        index = _segments.Count - 1;
        _segmentIndex[name] = index;
        _changed = true;
        return index;
    }

    private AsmSegment Seg => _segments[EnsureSegment()];

    private Symbol Define(string name, SymbolKind kind, long value, int segment, int elementSize = 0, bool far = false)
    {
        if (Registers.IsRegister(name) || IsKnownMnemonic(name))
            throw new AsmException(AsmErrorCode.SyntaxError, name);

        if (_symbols.TryGetValue(name, out var sym))
        {
            if (sym.DefinedInPass == _pass && !(kind == SymbolKind.Constant && sym.Kind == SymbolKind.Constant))
                throw new AsmException(AsmErrorCode.DuplicateSymbol, name);
            if (sym.Value != value || sym.Segment != segment) _changed = true;
        }
        else
        {
            sym = new Symbol { Name = name };
            _symbols[name] = sym;
            _changed = true;
        }
        sym.Kind = kind;
        sym.Value = value;
        sym.Segment = segment;
        sym.ElementSize = elementSize;
        sym.Far = far;
        sym.DefinedInPass = _pass;
        sym.Line = _line.Line;
        return sym;
    }

    private Symbol DefineLabelHere(string name, SymbolKind kind, int elementSize = 0, bool far = false)
    {
        int seg = EnsureSegment();
        return Define(name, kind, _segments[seg].Location, seg, elementSize, far);
    }

    #endregion

    #region Line processing

    private ExprValue Evaluate(List<Token> tokens, int start = 0)
    {
        var parser = new ExpressionParser(tokens, start, this);
        var v = parser.Parse();
        if (parser.Position < tokens.Count) throw new AsmException(AsmErrorCode.SyntaxError, tokens[parser.Position].Text);
        return v;
    }

    private long EvaluateConstant(List<Token> tokens, int start = 0)
    {
        var v = Evaluate(tokens, start);
        if (v.HasRegisters || v.Memory && v.Bracketed) throw new AsmException(AsmErrorCode.NotConstant);
        return v.Num;
    }

    private enum LineKind { Other, Instruction, Data }

    private LineKind ProcessLine(string rawText)
    {
        string text = Lexer.StripComment(rawText).Trim();
        if (text.Length == 0) return LineKind.Other;

        if (HandleConditional(text)) return LineKind.Other;
        if (_conditions.Count > 0 && !_conditions.Peek().Active) return LineKind.Other;

        if (text.StartsWith('#'))
        {
            HandleHashDirective(text);
            return LineKind.Other;
        }

        var tokens = Lexer.Tokenize(text);
        int p = 0;

        if (tokens.Count >= 2 && tokens[0].Kind == TokenKind.Identifier && tokens[1].Is(":")
            && !ExpressionParser.SegmentRegisters.ContainsKey(tokens[0].Text))
        {
            DefineLabelHere(tokens[0].Text, SymbolKind.Label);
            p = 2;
            if (p == tokens.Count) return LineKind.Other;
        }

        if (tokens.Count - p >= 2 && tokens[p].Kind == TokenKind.Identifier
            && (NamedDirectives.Contains(tokens[p + 1].Text) || tokens[p + 1].Is("=")))
        {
            HandleNamedDirective(tokens[p].Text, tokens[p + 1].Upper, tokens, p + 2);
            return DataSizes.ContainsKey(tokens[p + 1].Text) ? LineKind.Data : LineKind.Other;
        }

        if (tokens[p].Kind == TokenKind.Identifier && HandleDirective(tokens[p].Upper, tokens, p + 1))
            return DataSizes.ContainsKey(tokens[p].Text) ? LineKind.Data
                : tokens[p].Upper is ".STARTUP" or ".EXIT" ? LineKind.Instruction : LineKind.Other;

        EncodeInstruction(tokens, p);
        return LineKind.Instruction;
    }

    private bool HandleConditional(string text)
    {
        var words = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        string first = words[0].ToUpperInvariant();
        bool parentActive = _conditions.Count == 0 || _conditions.Peek().Active;
        string rest = words.Length > 1 ? words[1] : "";

        switch (first)
        {
            case "IF":
            case "IFE":
            case "IFDEF":
            case "IFNDEF":
            {
                bool cond = false;
                if (parentActive)
                {
                    if (first is "IFDEF" or "IFNDEF")
                    {
                        bool defined = _symbols.TryGetValue(rest.Trim(), out var s) && s.DefinedInPass == _pass;
                        cond = first == "IFDEF" ? defined : !defined;
                    }
                    else
                    {
                        long v = 0;
                        try { v = EvaluateConstant(Lexer.Tokenize(rest)); }
                        catch (AsmException e) { Report(e.Code, e.Args); }
                        cond = first == "IF" ? v != 0 : v == 0;
                    }
                }
                _conditions.Push((parentActive && cond, cond));
                return true;
            }
            case "ELSE":
            {
                if (_conditions.Count == 0) { Report(AsmErrorCode.ConditionalMismatch); return true; }
                var top = _conditions.Pop();
                bool outer = _conditions.Count == 0 || _conditions.Peek().Active;
                _conditions.Push((outer && !top.Taken, true));
                return true;
            }
            case "ENDIF":
                if (_conditions.Count == 0) Report(AsmErrorCode.ConditionalMismatch);
                else _conditions.Pop();
                return true;
        }
        return false;
    }

    /// <summary>#start=device#, #AX=1234h# style register presets; #make_xxx# is handled in PreScan.</summary>
    private void HandleHashDirective(string text)
    {
        string body = text.Trim('#').Trim();
        int eq = body.IndexOf('=');
        if (eq <= 0) return;
        string name = body[..eq].Trim().ToUpperInvariant();
        string value = body[(eq + 1)..].Trim();
        if (name == "START")
        {
            _devices.Add(Path.GetFileNameWithoutExtension(value));
            return;
        }
        if (!RegisterPreset.Names.Contains(name)) throw new AsmException(AsmErrorCode.UnexpectedDirective, "#" + name + "#");
        long number = EvaluateConstant(Lexer.Tokenize(value));
        if (number is < -32768 or > 0xFFFF) throw new AsmException(AsmErrorCode.ValueOutOfRange, value);
        _registerPresets[name] = (ushort)number;
    }

    private void HandleNamedDirective(string name, string directive, List<Token> tokens, int p)
    {
        switch (directive)
        {
            case "DB":
            case "DW":
            case "DD":
            case "DQ":
            case "DT":
            {
                int size = DataSizes[directive];
                var sym = DefineLabelHere(name, SymbolKind.Variable, size);
                sym.Length = EmitData(tokens, p, size);
                return;
            }
            case "EQU":
            case "=":
            {
                var v = Evaluate(tokens, p);
                if (v.Memory || v.HasRegisters || v.SegmentOf >= 0)
                {
                    // Address alias: behaves like the label it names.
                    var alias = Define(name, v.Memory ? SymbolKind.Variable : SymbolKind.Label, v.Num, v.SegmentOf, v.Size);
                    alias.Length = v.Symbol?.Length ?? 1;
                }
                else Define(name, SymbolKind.Constant, v.Num, -1);
                return;
            }
            case "LABEL":
            {
                string type = p < tokens.Count ? tokens[p].Upper : "NEAR";
                int size = DataSizes.TryGetValue(type == "BYTE" ? "DB" : type == "WORD" ? "DW" : type == "DWORD" ? "DD" : "", out int s) ? s : 0;
                DefineLabelHere(name, size > 0 ? SymbolKind.Variable : SymbolKind.Label, size, type == "FAR");
                return;
            }
            case "PROC":
            {
                bool far = p < tokens.Count && tokens[p].Is("FAR");
                var sym = DefineLabelHere(name, SymbolKind.Procedure, 0, far);
                _procStack.Push(sym);
                return;
            }
            case "ENDP":
                if (_procStack.Count == 0) throw new AsmException(AsmErrorCode.UnexpectedDirective, "ENDP");
                _procStack.Pop();
                return;
            case "SEGMENT":
                OpenSegment(name, tokens, p);
                return;
            case "ENDS":
                CloseSegment();
                return;
            case "GROUP":
                return;
            default:
                throw new AsmException(AsmErrorCode.UnexpectedDirective, directive);
        }
    }

    private void OpenSegment(string name, List<Token> tokens, int p)
    {
        var kind = SegmentKind.Other;
        for (int i = p; i < tokens.Count; i++)
        {
            if (tokens[i].Is("STACK") || tokens[i].Kind == TokenKind.String && tokens[i].Text.Equals("STACK", StringComparison.OrdinalIgnoreCase))
                kind = SegmentKind.Stack;
            else if (tokens[i].Kind == TokenKind.String && tokens[i].Text.Equals("CODE", StringComparison.OrdinalIgnoreCase))
                kind = SegmentKind.Code;
            else if (tokens[i].Kind == TokenKind.String && tokens[i].Text.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                kind = SegmentKind.Data;
        }
        if (kind == SegmentKind.Other)
        {
            string n = name.ToUpperInvariant();
            if (n.Contains("STACK") || n == "SSEG") kind = SegmentKind.Stack;
            else if (n.Contains("CODE") || n.Contains("TEXT") || n == "CSEG") kind = SegmentKind.Code;
            else if (n.Contains("DATA") || n == "DSEG") kind = SegmentKind.Data;
        }
        if (_current >= 0) _segmentStack.Push(_current);
        _current = GetOrCreateSegment(name, kind);
        _segments[_current].Kind = kind;
    }

    private void CloseSegment() => _current = _segmentStack.Count > 0 ? _segmentStack.Pop() : -1;

    private bool HandleDirective(string directive, List<Token> tokens, int p)
    {
        if (IgnoredDirectives.Contains(directive)) return true;

        switch (directive)
        {
            case "DB":
            case "DW":
            case "DD":
            case "DQ":
            case "DT":
                EmitData(tokens, p, DataSizes[directive]);
                return true;
            case "ORG":
            {
                int origin = (int)EvaluateConstant(tokens, p);
                if (Seg.Bytes.Count == 0) Seg.Origin = origin;
                Seg.Location = origin;
                return true;
            }
            case "EVEN":
                if ((Seg.Location & 1) != 0) Seg.Emit(0x90);
                return true;
            case "ALIGN":
            {
                long n = p < tokens.Count ? EvaluateConstant(tokens, p) : 2;
                while (n > 0 && Seg.Location % n != 0) Seg.Emit(0x90);
                return true;
            }
            case "END":
                if (p < tokens.Count) _entryName = tokens[p].Text;
                _ended = true;
                return true;
            case "ENDS":
                CloseSegment();
                return true;
            case "ENDP":
                if (_procStack.Count > 0) _procStack.Pop();
                return true;
            case ".MODEL":
                return true;
            case ".CODE":
                SwitchSimplifiedSegment("CODE", SegmentKind.Code);
                return true;
            case ".DATA":
            case ".DATA?":
            case ".CONST":
            case ".FARDATA":
                SwitchSimplifiedSegment("DATA", SegmentKind.Data);
                return true;
            case ".STACK":
            {
                long size = p < tokens.Count ? EvaluateConstant(tokens, p) : 0x400;
                int saved = _current;
                int stack = GetOrCreateSegment("STACK", SegmentKind.Stack);
                _segments[stack].Location = 0;
                _current = stack;
                for (long i = 0; i < size; i++) _segments[stack].Emit(0);
                _current = saved;
                return true;
            }
            case ".STARTUP":
                DefineLabelHere("@Startup", SymbolKind.Label);
                _entryName = "@Startup";
                if (_format == OutputFormat.Exe)
                    EncodeText("MOV AX, @DATA", "MOV DS, AX");
                return true;
            case ".EXIT":
                EncodeText(p < tokens.Count ? $"MOV AL, {string.Join(" ", tokens.Skip(p))}" : null, "MOV AH, 4Ch", "INT 21h");
                return true;
        }
        return false;
    }

    private void SwitchSimplifiedSegment(string name, SegmentKind kind)
    {
        _segmentStack.Clear();
        _current = GetOrCreateSegment(name, kind);
    }

    private void EncodeText(params string?[] lines)
    {
        foreach (var l in lines)
            if (l != null) EncodeInstruction(Lexer.Tokenize(l), 0);
    }

    #endregion

    #region Data

    private int EmitData(List<Token> tokens, int p, int size)
    {
        int count = 0;
        foreach (var item in Operand.Split(tokens, p))
            count += EmitDataItem(item, size);
        return count;
    }

    private int EmitDataItem(List<Token> item, int size)
    {
        if (item.Count == 0) throw new AsmException(AsmErrorCode.ExpectedExpression);

        int dup = item.FindIndex(t => t.Is("DUP"));
        if (dup > 0)
        {
            long n = EvaluateConstant(item.GetRange(0, dup));
            var inner = item.Skip(dup + 1).ToList();
            if (inner.Count < 2 || !inner[0].Is("(") || !inner[^1].Is(")"))
                throw new AsmException(AsmErrorCode.SyntaxError, "DUP");
            inner = inner.GetRange(1, inner.Count - 2);
            if (n < 0 || n > 0x10000) throw new AsmException(AsmErrorCode.ValueOutOfRange, n.ToString());
            int total = 0;
            for (long i = 0; i < n; i++)
                foreach (var sub in Operand.Split(inner, 0))
                    total += EmitDataItem(sub, size);
            return total;
        }

        if (item.Count == 1 && item[0].Is("?"))
        {
            for (int i = 0; i < size; i++) Seg.Emit(0);
            return 1;
        }

        if (item.Count == 1 && item[0].Kind == TokenKind.String && (size == 1 || item[0].Text.Length > size))
        {
            foreach (char c in item[0].Text)
            {
                Seg.Emit((byte)c);
                for (int i = 1; i < size; i++) Seg.Emit(0);
            }
            return item[0].Text.Length;
        }

        EmitValue(Evaluate(item), size);
        return 1;
    }

    private void EmitValue(ExprValue v, int size)
    {
        if (v.HasRegisters) throw new AsmException(AsmErrorCode.NotConstant);
        switch (size)
        {
            case 1:
                CheckRange(v, 1);
                Seg.Emit((byte)v.Num);
                break;
            case 2:
                EmitWord(v);
                break;
            case 4 when v.SegmentOf >= 0 && v.Symbol != null && v.Symbol.Kind != SymbolKind.Constant:
                EmitWord(v with { SegmentOf = -1 });
                AddFixup(v.SegmentOf);
                break;
            default:
                for (int i = 0; i < size; i++) Seg.Emit(i < 8 ? (byte)(v.Num >> (8 * i)) : (byte)0);
                break;
        }
    }

    #endregion
}
