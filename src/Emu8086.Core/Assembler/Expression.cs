namespace Emu8086.Core.Assembler;

public enum JumpDistance { Default, Short, Near, Far }

/// <summary>Result of evaluating an operand expression.</summary>
public struct ExprValue
{
    public long Num;
    /// <summary>Refers to a symbol not defined yet (forward reference in an early pass).</summary>
    public bool Undefined;
    public int Base;
    public int Index;
    public bool Memory;
    public bool Bracketed;
    public int Size;
    public JumpDistance Distance;
    /// <summary>Segment the value is an offset in (relocatable address), or -1.</summary>
    public int SegmentOf;
    /// <summary>Value is the paragraph of this segment (needs a load-time fixup), or -1.</summary>
    public int SegmentValue;
    public int SegOverride;
    public Symbol? Symbol;

    public static ExprValue Const(long n) => new()
        { Num = n, Base = -1, Index = -1, SegmentOf = -1, SegmentValue = -1, SegOverride = -1 };

    public readonly bool HasRegisters => Base >= 0 || Index >= 0;
    public readonly bool IsImmediate => !Memory && !HasRegisters;
}

public interface IExprContext
{
    Symbol? Lookup(string name);
    int CurrentSegment { get; }
    long CurrentLocation { get; }
    /// <summary>Returns the segment index for a segment name used as a value (e.g. @data), or -1.</summary>
    int SegmentIndex(string name);
}

/// <summary>Recursive-descent evaluator for MASM expressions, including [base+index+disp].</summary>
public sealed class ExpressionParser
{
    private static readonly Dictionary<string, int> AddressRegisters = new(StringComparer.OrdinalIgnoreCase)
        { ["BX"] = 3, ["BP"] = 5, ["SI"] = 6, ["DI"] = 7 };

    public static readonly Dictionary<string, int> SegmentRegisters = new(StringComparer.OrdinalIgnoreCase)
        { ["ES"] = 0, ["CS"] = 1, ["SS"] = 2, ["DS"] = 3 };

    private static readonly Dictionary<string, int> SizeKeywords = new(StringComparer.OrdinalIgnoreCase)
        { ["BYTE"] = 1, ["WORD"] = 2, ["DWORD"] = 4, ["QWORD"] = 8, ["TBYTE"] = 10 };

    private readonly List<Token> _t;
    private readonly IExprContext _ctx;
    private int _p;
    private int _bracketDepth;

    public ExpressionParser(List<Token> tokens, int position, IExprContext context)
    {
        _t = tokens;
        _p = position;
        _ctx = context;
    }

    public int Position => _p;

    private Token Peek(int ahead = 0) => _p + ahead < _t.Count ? _t[_p + ahead] : new Token(TokenKind.End, "");
    private Token Next() => _p < _t.Count ? _t[_p++] : new Token(TokenKind.End, "");

    private bool Accept(string text)
    {
        if (!Peek().Is(text)) return false;
        _p++;
        return true;
    }

    private void Expect(string text)
    {
        if (!Accept(text)) throw new AsmException(AsmErrorCode.SyntaxError, Peek().Text);
    }

    public ExprValue Parse() => ParseOr();

    private ExprValue ParseOr()
    {
        var a = ParseAnd();
        while (true)
        {
            if (Accept("OR")) a = Binary(a, ParseAnd(), (x, y) => x | y);
            else if (Accept("XOR")) a = Binary(a, ParseAnd(), (x, y) => x ^ y);
            else return a;
        }
    }

    private ExprValue ParseAnd()
    {
        var a = ParseNot();
        while (Accept("AND")) a = Binary(a, ParseNot(), (x, y) => x & y);
        return a;
    }

    private ExprValue ParseNot()
    {
        if (Accept("NOT"))
        {
            var v = ParseNot();
            RequireConstant(v);
            v.Num = ~v.Num & 0xFFFF;
            return v;
        }
        return ParseRelational();
    }

    private ExprValue ParseRelational()
    {
        var a = ParseAdd();
        string[] ops = ["EQ", "NE", "LT", "LE", "GT", "GE", "==", "!=", "<>", "<", ">", "<=", ">="];
        foreach (var op in ops)
        {
            if (!Peek().Is(op)) continue;
            _p++;
            var b = ParseAdd();
            bool r = op.ToUpperInvariant() switch
            {
                "EQ" or "==" => a.Num == b.Num,
                "NE" or "!=" or "<>" => a.Num != b.Num,
                "LT" or "<" => a.Num < b.Num,
                "LE" or "<=" => a.Num <= b.Num,
                "GT" or ">" => a.Num > b.Num,
                _ => a.Num >= b.Num,
            };
            var result = ExprValue.Const(r ? 0xFFFF : 0);
            result.Undefined = a.Undefined || b.Undefined;
            return result;
        }
        return a;
    }

    private ExprValue ParseAdd()
    {
        var a = ParseMul();
        while (true)
        {
            if (Accept("+")) a = AddValues(a, ParseMul(), false);
            else if (Accept("-")) a = AddValues(a, ParseMul(), true);
            else if (Peek().Is("[")) a = AddValues(a, ParseUnary(), false); // var[bx]
            else if (Peek().Is(".") ) { _p++; a = AddValues(a, ParseMul(), false); } // [bx].field
            else return a;
        }
    }

    private ExprValue ParseMul()
    {
        var a = ParseUnary();
        while (true)
        {
            if (Accept("*")) a = Binary(a, ParseUnary(), (x, y) => x * y);
            else if (Accept("/")) a = Binary(a, ParseUnary(), (x, y) => y == 0 ? throw new AsmException(AsmErrorCode.DivisionByZero) : x / y);
            else if (Accept("MOD")) a = Binary(a, ParseUnary(), (x, y) => y == 0 ? throw new AsmException(AsmErrorCode.DivisionByZero) : x % y);
            else if (Accept("SHL")) a = Binary(a, ParseUnary(), (x, y) => x << (int)y);
            else if (Accept("SHR")) a = Binary(a, ParseUnary(), (x, y) => x >> (int)y);
            else return a;
        }
    }

    private ExprValue ParseUnary()
    {
        var tok = Peek();
        if (Accept("+")) return ParseUnary();
        if (Accept("-"))
        {
            var v = ParseUnary();
            RequireConstant(v);
            v.Num = -v.Num;
            return v;
        }

        if (tok.Kind == TokenKind.Identifier)
        {
            string up = tok.Upper;
            if (SizeKeywords.TryGetValue(up, out int size) && Peek(1).Is("PTR"))
            {
                _p += 2;
                var v = ParseUnary();
                v.Size = size;
                if (size == 4) v.Distance = JumpDistance.Far;
                return v;
            }
            if ((up is "FAR" or "NEAR") && Peek(1).Is("PTR"))
            {
                _p += 2;
                var v = ParseUnary();
                v.Distance = up == "FAR" ? JumpDistance.Far : JumpDistance.Near;
                return v;
            }
            if (up == "SHORT")
            {
                _p++;
                var v = ParseUnary();
                v.Distance = JumpDistance.Short;
                return v;
            }
            if (up == "OFFSET")
            {
                _p++;
                var v = ParseUnary();
                v.Memory = false;
                v.Size = 0;
                return v;
            }
            if (up == "SEG")
            {
                _p++;
                var v = ParseUnary();
                var r = ExprValue.Const(0);
                r.Undefined = v.Undefined;
                r.SegmentValue = v.SegmentOf >= 0 ? v.SegmentOf : v.SegmentValue;
                return r;
            }
            if (up is "TYPE" or "SIZE" or "LENGTH" or "SIZEOF" or "LENGTHOF")
            {
                _p++;
                var v = ParseUnary();
                var sym = v.Symbol;
                int elem = v.Size != 0 ? v.Size : sym?.ElementSize ?? 0;
                if (elem == 0 && sym != null && sym.Kind is SymbolKind.Label or SymbolKind.Procedure)
                    elem = sym.Far ? 0xFFFE : 0xFFFF;
                int len = sym?.Length ?? 1;
                var r = ExprValue.Const(up switch
                {
                    "TYPE" => elem,
                    "LENGTH" or "LENGTHOF" => len,
                    _ => elem * len,
                });
                r.Undefined = v.Undefined;
                return r;
            }
            if (up is "LOW" or "HIGH" or "LOWWORD" or "HIGHWORD")
            {
                _p++;
                var v = ParseUnary();
                v.Num = up switch
                {
                    "LOW" => v.Num & 0xFF,
                    "HIGH" => (v.Num >> 8) & 0xFF,
                    "LOWWORD" => v.Num & 0xFFFF,
                    _ => (v.Num >> 16) & 0xFFFF,
                };
                v.Memory = false;
                v.SegmentOf = -1;
                return v;
            }
        }
        return ParsePrimary();
    }

    private ExprValue ParsePrimary()
    {
        var tok = Next();
        switch (tok.Kind)
        {
            case TokenKind.Number:
                return ExprValue.Const(tok.Number);

            case TokenKind.String:
            {
                long n = 0;
                foreach (char c in tok.Text) n = (n << 8) | (byte)c;
                return ExprValue.Const(n);
            }

            case TokenKind.Punct when tok.Text == "(":
            {
                var v = Parse();
                Expect(")");
                return v;
            }

            case TokenKind.Punct when tok.Text == "[":
            {
                _bracketDepth++;
                var v = Parse();
                Expect("]");
                _bracketDepth--;
                v.Memory = true;
                v.Bracketed = true;
                return v;
            }

            case TokenKind.Identifier:
                return Identifier(tok);

            case TokenKind.End:
                throw new AsmException(AsmErrorCode.ExpectedExpression);

            default:
                throw new AsmException(AsmErrorCode.SyntaxError, tok.Text);
        }
    }

    private ExprValue Identifier(Token tok)
    {
        if (tok.Text == "$")
        {
            var here = ExprValue.Const(_ctx.CurrentLocation);
            here.SegmentOf = _ctx.CurrentSegment;
            return here;
        }

        if (SegmentRegisters.TryGetValue(tok.Text, out int sreg) && Peek().Is(":"))
        {
            _p++;
            var v = ParseUnary();
            v.SegOverride = sreg;
            v.Memory = true;
            return v;
        }

        if (AddressRegisters.TryGetValue(tok.Text, out int reg))
        {
            if (_bracketDepth == 0) throw new AsmException(AsmErrorCode.InvalidAddressing, tok.Text);
            var v = ExprValue.Const(0);
            if (reg is 3 or 5) v.Base = reg; else v.Index = reg;
            v.Memory = true;
            return v;
        }

        if (Registers.IsRegister(tok.Text)) throw new AsmException(AsmErrorCode.InvalidAddressing, tok.Text);

        int segIndex = _ctx.SegmentIndex(tok.Text);
        if (segIndex >= 0)
        {
            var v = ExprValue.Const(0);
            v.SegmentValue = segIndex;
            return v;
        }

        var sym = _ctx.Lookup(tok.Text);
        if (sym == null)
        {
            var v = ExprValue.Const(0);
            v.Undefined = true;
            v.Symbol = new Symbol { Name = tok.Text };
            return v;
        }

        var r = ExprValue.Const(sym.Value);
        r.Symbol = sym;
        switch (sym.Kind)
        {
            case SymbolKind.Constant:
                break;
            case SymbolKind.Variable:
                r.Memory = true;
                r.Size = sym.ElementSize;
                r.SegmentOf = sym.Segment;
                break;
            default:
                r.SegmentOf = sym.Segment;
                if (sym.Far) r.Distance = JumpDistance.Far;
                break;
        }
        return r;
    }

    private static void RequireConstant(ExprValue v)
    {
        if (v.HasRegisters) throw new AsmException(AsmErrorCode.NotConstant);
    }

    private static ExprValue Binary(ExprValue a, ExprValue b, Func<long, long, long> op)
    {
        RequireConstant(a);
        RequireConstant(b);
        var r = ExprValue.Const(a.Undefined || b.Undefined ? 0 : op(a.Num, b.Num));
        r.Undefined = a.Undefined || b.Undefined;
        r.Symbol = a.Symbol ?? b.Symbol;
        return r;
    }

    private static ExprValue AddValues(ExprValue a, ExprValue b, bool subtract)
    {
        var r = a;
        r.Undefined = a.Undefined || b.Undefined;
        r.Memory = a.Memory || b.Memory;
        r.Bracketed = a.Bracketed || b.Bracketed;
        r.Size = a.Size != 0 ? a.Size : b.Size;
        r.Symbol = a.Symbol ?? b.Symbol;
        r.SegOverride = a.SegOverride >= 0 ? a.SegOverride : b.SegOverride;
        if (r.Distance == JumpDistance.Default) r.Distance = b.Distance;

        if (subtract)
        {
            if (b.HasRegisters) throw new AsmException(AsmErrorCode.InvalidAddressing, "-");
            r.Num = a.Num - b.Num;
            // label - label in the same segment is a plain number
            if (a.SegmentOf >= 0 && a.SegmentOf == b.SegmentOf)
            {
                r.SegmentOf = -1;
                r.Memory = a.Bracketed;
            }
            return r;
        }

        r.Num = a.Num + b.Num;
        if (r.SegmentOf < 0) r.SegmentOf = b.SegmentOf;
        if (b.Base >= 0)
        {
            if (r.Base >= 0) throw new AsmException(AsmErrorCode.InvalidAddressing, "BX/BP");
            r.Base = b.Base;
        }
        if (b.Index >= 0)
        {
            if (r.Index >= 0) throw new AsmException(AsmErrorCode.InvalidAddressing, "SI/DI");
            r.Index = b.Index;
        }
        return r;
    }
}
