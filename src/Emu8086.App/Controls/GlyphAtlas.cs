using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Emu8086.App.Controls;

/// <summary>
/// Alpha masks for the 256 code page 437 characters. Letters come from a monospace font;
/// block and box-drawing characters are drawn procedurally so lines join seamlessly.
/// </summary>
public sealed class GlyphAtlas
{
    public const int CellWidth = 10;
    public const int CellHeight = 20;
    private const double FontSize = 16.5;

    // Box-drawing characters B3..DA: line weight up, down, left, right (0 none, 1 single, 2 double).
    private static readonly string[] BoxLines =
    [
        "1100", "1110", "1120", "2210", "0210", "0120", "2220", "2200", "0220", "2020",
        "2010", "1020", "0110", "1001", "1011", "0111", "1101", "0011", "1111", "1102",
        "2201", "2002", "0202", "2022", "0222", "2202", "0022", "2222", "1022", "2011",
        "0122", "0211", "2001", "1002", "0102", "0201", "2211", "1122", "1010", "0101",
    ];

    private readonly byte[][] _masks = new byte[256][];

    public GlyphAtlas()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp437 = Encoding.GetEncoding(437);
        var typeface = new Typeface(new FontFamily("Cascadia Mono, Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        for (int code = 0; code < 256; code++)
        {
            _masks[code] = code switch
            {
                >= 0xB0 and <= 0xB2 => Shade(code - 0xAF),
                >= 0xB3 and <= 0xDA => Box(BoxLines[code - 0xB3]),
                0xDB => Rect(0, 0, CellWidth, CellHeight),
                0xDC => Rect(0, CellHeight / 2, CellWidth, CellHeight),
                0xDD => Rect(0, 0, CellWidth / 2, CellHeight),
                0xDE => Rect(CellWidth / 2, 0, CellWidth, CellHeight),
                0xDF => Rect(0, 0, CellWidth, CellHeight / 2),
                _ => RenderText(Decode(cp437, code), typeface),
            };
        }
    }

    public byte[] this[int code] => _masks[code & 0xFF];

    private static string Decode(Encoding cp437, int code) => code switch
    {
        0 or 32 or 255 => " ",
        < 32 => ((char)"\u0000☺☻♥♦♣♠•◘○◙♂♀♪♫☼►◄↕‼¶§▬↨↑↓→←∟↔▲▼"[code]).ToString(),
        127 => "⌂",
        _ => cp437.GetString([(byte)code]),
    };

    private static byte[] RenderText(string text, Typeface typeface)
    {
        var mask = new byte[CellWidth * CellHeight];
        if (string.IsNullOrWhiteSpace(text)) return mask;

        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface,
            FontSize, Brushes.White, 1.0);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double x = (CellWidth - formatted.WidthIncludingTrailingWhitespace) / 2;
            double y = (CellHeight - formatted.Height) / 2;
            dc.DrawText(formatted, new Point(x, y));
        }
        var bitmap = new RenderTargetBitmap(CellWidth, CellHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[CellWidth * CellHeight * 4];
        bitmap.CopyPixels(pixels, CellWidth * 4, 0);
        for (int i = 0; i < mask.Length; i++) mask[i] = pixels[i * 4 + 3];
        return mask;
    }

    private static byte[] Rect(int x0, int y0, int x1, int y1)
    {
        var mask = new byte[CellWidth * CellHeight];
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
            mask[y * CellWidth + x] = 255;
        return mask;
    }

    private static byte[] Shade(int level)
    {
        var mask = new byte[CellWidth * CellHeight];
        for (int y = 0; y < CellHeight; y++)
        for (int x = 0; x < CellWidth; x++)
        {
            bool on = level switch
            {
                1 => (x % 2 == 0) && (y % 4 == 0 || y % 4 == 2 && x % 4 == 0),
                2 => (x + y) % 2 == 0,
                _ => !((x % 2 == 0) && (y % 4 == 0 || y % 4 == 2 && x % 4 == 0)),
            };
            if (on) mask[y * CellWidth + x] = 255;
        }
        return mask;
    }

    private static byte[] Box(string lines)
    {
        var mask = new byte[CellWidth * CellHeight];
        int cx = CellWidth / 2, cy = CellHeight / 2;
        const int Gap = 2;

        void Fill(int x0, int y0, int x1, int y1)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(CellHeight, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(CellWidth, x1); x++)
                mask[y * CellWidth + x] = 255;
        }

        int up = lines[0] - '0', down = lines[1] - '0', left = lines[2] - '0', right = lines[3] - '0';
        // For double lines the parallel strokes meet the perpendicular ones at their outer edge.
        int hExtent = up == 2 || down == 2 ? Gap + 1 : 1;
        int vExtent = left == 2 || right == 2 ? Gap + 1 : 1;

        void Vertical(int weight, int y0, int y1)
        {
            if (weight == 1) Fill(cx - 1, y0, cx + 1, y1);
            else if (weight == 2)
            {
                Fill(cx - Gap - 1, y0, cx - Gap + 1, y1);
                Fill(cx + Gap - 1, y0, cx + Gap + 1, y1);
            }
        }

        void Horizontal(int weight, int x0, int x1)
        {
            if (weight == 1) Fill(x0, cy - 1, x1, cy + 1);
            else if (weight == 2)
            {
                Fill(x0, cy - Gap - 1, x1, cy - Gap + 1);
                Fill(x0, cy + Gap - 1, x1, cy + Gap + 1);
            }
        }

        Vertical(up, 0, cy + vExtent);
        Vertical(down, cy - vExtent, CellHeight);
        Horizontal(left, 0, cx + hExtent);
        Horizontal(right, cx - hExtent, CellWidth);
        return mask;
    }
}
