// =============================================================================
// EnterprisePdfEditor — Core/Models/PdfDomainModels.cs
// ISO 32000 domain objects that represent the internal PDF text layout model.
// =============================================================================

using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace EnterprisePdfEditor.Core.Models
{
    // -------------------------------------------------------------------------
    // Represents ONE glyph (character) extracted from a PDF content stream.
    // All coordinates are in PDF user-space (origin at bottom-left, Y grows up).
    // -------------------------------------------------------------------------
    public sealed class PdfGlyph
    {
        /// <summary>Unicode scalar value of this glyph.</summary>
        public int Unicode { get; init; }

        /// <summary>Character as a string (may be multi-char for surrogates).</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>Glyph origin X in PDF user space (points).</summary>
        public double X { get; init; }

        /// <summary>Glyph baseline Y in PDF user space (points).</summary>
        public double Y { get; init; }

        /// <summary>Horizontal advance width in PDF user space (points).</summary>
        public double Width { get; init; }

        /// <summary>Font size at point of use (including text matrix scaling).</summary>
        public double FontSize { get; init; }

        /// <summary>
        /// Reference to the font descriptor this glyph was rendered with.
        /// Shared across all glyphs from the same font/size combination.
        /// </summary>
        public PdfFontMetrics Font { get; init; } = null!;

        /// <summary>Raw character code as it appears in the content stream.</summary>
        public byte CharCode { get; init; }

        /// <summary>Horizontal scaling factor (Th) from the text state.</summary>
        public double HorizontalScaling { get; init; } = 1.0;

        /// <summary>Character spacing (Tc) from the text state at time of extraction.</summary>
        public double CharSpacing { get; init; }

        /// <summary>Word spacing (Tw) from the text state (applied to ASCII 0x20).</summary>
        public double WordSpacing { get; init; }

        // ---- Derived convenience helpers ------------------------------------

        public double Right => X + Width;
        public double Top  => Y + (Font?.Ascent  * FontSize / 1000.0 ?? FontSize);
        public double Bottom => Y + (Font?.Descent * FontSize / 1000.0 ?? 0);

        public Rect PdfBounds => new Rect(
            X,
            Bottom,
            Width,
            Top - Bottom);
    }

    // -------------------------------------------------------------------------
    // Font descriptor extracted from the PDF Font dictionary.
    // Units: glyph-space (1/1000 of a text unit unless noted).
    // -------------------------------------------------------------------------
    public sealed class PdfFontMetrics
    {
        /// <summary>Internal PDF font resource name (e.g. "/F1").</summary>
        public string ResourceName { get; init; } = string.Empty;

        /// <summary>PostScript / BaseFont name (e.g. "Helvetica-Bold").</summary>
        public string BaseFont { get; init; } = string.Empty;

        /// <summary>Font subtype: Type1, TrueType, CIDFontType2, etc.</summary>
        public string Subtype { get; init; } = string.Empty;

        /// <summary>Ascent in glyph-space units (typically positive).</summary>
        public double Ascent { get; init; } = 800;

        /// <summary>Descent in glyph-space units (typically negative).</summary>
        public double Descent { get; init; } = -200;

        /// <summary>Cap height in glyph-space units.</summary>
        public double CapHeight { get; init; } = 700;

        /// <summary>Italic angle in degrees.</summary>
        public double ItalicAngle { get; init; }

        /// <summary>Font bounding box [llx lly urx ury] in glyph-space.</summary>
        public Rect FontBBox { get; init; }

        /// <summary>
        /// Per-glyph horizontal advance widths keyed by character code.
        /// Values are in glyph-space units (divide by 1000 and multiply by font size for points).
        /// </summary>
        public Dictionary<byte, double> GlyphWidths { get; init; } = new();

        /// <summary>Default width used when a code is not in GlyphWidths.</summary>
        public double DefaultWidth { get; init; } = 1000;

        /// <summary>Flags bit field from the FontDescriptor.</summary>
        public int Flags { get; init; }

        // ---- Derived flag helpers ------------------------------------------
        public bool IsFixedPitch => (Flags & 0x01) != 0;
        public bool IsSerif      => (Flags & 0x02) != 0;
        public bool IsSymbolic   => (Flags & 0x04) != 0;
        public bool IsBold       => BaseFont.Contains("Bold", System.StringComparison.OrdinalIgnoreCase)
                                 || (Flags & 0x40000) != 0;
        public bool IsItalic     => ItalicAngle != 0
                                 || BaseFont.Contains("Italic", System.StringComparison.OrdinalIgnoreCase)
                                 || BaseFont.Contains("Oblique", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Resolve the horizontal advance for a given character code in PDF user-space points
        /// at the specified font size.
        /// </summary>
        public double GetGlyphAdvance(byte charCode, double fontSize, double horizontalScaling = 1.0)
        {
            double w1000 = GlyphWidths.TryGetValue(charCode, out var w) ? w : DefaultWidth;
            return (w1000 / 1000.0) * fontSize * horizontalScaling;
        }

        /// <summary>
        /// Compute the total advance for a string (without Tc/Tw spacing).
        /// </summary>
        public double MeasureString(string text, double fontSize, double horizontalScaling = 1.0)
        {
            double total = 0;
            foreach (char c in text)
            {
                total += GetGlyphAdvance((byte)c, fontSize, horizontalScaling);
            }
            return total;
        }

        /// <summary>Line height = (Ascent - Descent) / 1000 * fontSize.</summary>
        public double GetLineHeight(double fontSize) => (Ascent - Descent) / 1000.0 * fontSize;

        /// <summary>Ascent in PDF user-space points at a given font size.</summary>
        public double GetAscent(double fontSize) => Ascent / 1000.0 * fontSize;

        /// <summary>Descent in PDF user-space points at a given font size (negative value).</summary>
        public double GetDescent(double fontSize) => Descent / 1000.0 * fontSize;

        // ---- WPF font resolution -------------------------------------------
        /// <summary>
        /// Best-effort WPF FontFamily resolved from the BaseFont name.
        /// Falls back to Arial when no match.
        /// </summary>
        public FontFamily ResolveWpfFontFamily()
        {
            string name = BaseFont.Replace("-", " ").Split('+')[^1]; // strip subset prefix
            try { return new FontFamily(name); }
            catch { return new FontFamily("Arial"); }
        }

        public FontWeight ResolveWpfFontWeight() => IsBold ? FontWeights.Bold : FontWeights.Normal;
        public FontStyle  ResolveWpfFontStyle()  => IsItalic ? FontStyles.Italic : FontStyles.Normal;
    }

    // -------------------------------------------------------------------------
    // A logical TEXT RUN: a contiguous sequence of glyphs sharing the same
    // font, size, color, and baseline. Corresponds to a single Tj / TJ operand.
    // -------------------------------------------------------------------------
    public sealed class TextRun
    {
        public List<PdfGlyph> Glyphs { get; } = new();
        public PdfFontMetrics Font    => Glyphs.Count > 0 ? Glyphs[0].Font : null!;
        public double FontSize        => Glyphs.Count > 0 ? Glyphs[0].FontSize : 0;
        public double BaselineY       => Glyphs.Count > 0 ? Glyphs[0].Y : 0;
        public Color FillColor        { get; init; } = Colors.Black;

        public string Text => string.Concat(Glyphs.Select(g => g.Text));

        public double Left   => Glyphs.Count > 0 ? Glyphs[0].X : 0;
        public double Right  => Glyphs.Count > 0 ? Glyphs[^1].Right : 0;
        public double Width  => Right - Left;
    }

    // -------------------------------------------------------------------------
    // A TEXT LINE: one or more TextRuns sharing the same (±tolerance) baseline.
    // -------------------------------------------------------------------------
    public sealed class TextLine
    {
        public List<TextRun> Runs { get; } = new();

        public double BaselineY    => Runs.Count > 0 ? Runs[0].BaselineY : 0;
        public double Left         => Runs.Count > 0 ? Runs.Min(r => r.Left)  : 0;
        public double Right        => Runs.Count > 0 ? Runs.Max(r => r.Right) : 0;
        public double Width        => Right - Left;
        public double FontSize     => Runs.Count > 0 ? Runs[0].FontSize : 12;
        public PdfFontMetrics Font => Runs.Count > 0 ? Runs[0].Font : null!;

        /// <summary>Dominant line height in PDF user-space.</summary>
        public double LineHeight => Font?.GetLineHeight(FontSize) ?? FontSize * 1.2;

        public string Text => string.Concat(Runs.Select(r => r.Text));

        public List<PdfGlyph> AllGlyphs => Runs.SelectMany(r => r.Glyphs).ToList();
    }

    // -------------------------------------------------------------------------
    // A PARAGRAPH BLOCK: the central editing unit.
    // Groups logically contiguous TextLines into one reflow-able paragraph.
    // -------------------------------------------------------------------------
    public sealed class ParagraphBlock
    {
        public int PageIndex { get; init; }

        public List<TextLine> Lines { get; } = new();

        /// <summary>
        /// Bounding box in PDF user-space (points, origin bottom-left).
        /// Updated by the Layout Reconstruction Engine after each edit.
        /// </summary>
        public Rect BoundingBox { get; set; }

        /// <summary>
        /// Bounding box in WPF screen-space (origin top-left, after transform).
        /// Recomputed by PdfPageView whenever zoom/scroll changes.
        /// </summary>
        public Rect ScreenBoundingBox { get; set; }

        // ---- Dominant font attributes (used for overlay RTB styling) --------
        public PdfFontMetrics DominantFont  => Lines.FirstOrDefault()?.Font ?? new PdfFontMetrics();
        public double DominantFontSize      => Lines.FirstOrDefault()?.FontSize ?? 12;
        public Color DominantColor          => Lines.SelectMany(l => l.Runs).FirstOrDefault()?.FillColor ?? Colors.Black;

        // ---- Full plain text -----------------------------------------------
        public string PlainText => string.Join("\n", Lines.Select(l => l.Text));

        public List<PdfGlyph> AllGlyphs => Lines.SelectMany(l => l.AllGlyphs).ToList();

        // ---- Geometry helpers ----------------------------------------------

        /// <summary>Top of the paragraph in PDF user-space (max Y of top edge).</summary>
        public double PdfTop    => BoundingBox.Y + BoundingBox.Height;
        /// <summary>Bottom of the paragraph in PDF user-space.</summary>
        public double PdfBottom => BoundingBox.Y;
        public double PdfLeft   => BoundingBox.X;
        public double PdfRight  => BoundingBox.X + BoundingBox.Width;

        /// <summary>Left indent estimated from glyph positions.</summary>
        public double IndentX   => Lines.Count > 0 ? Lines.Min(l => l.Left) : 0;

        /// <summary>Maximum right edge (column width).</summary>
        public double ColumnRight => Lines.Count > 0 ? Lines.Max(l => l.Right) : 0;

        /// <summary>Column width = ColumnRight - IndentX.</summary>
        public double ColumnWidth => ColumnRight - IndentX;

        // ---- Content stream location ---------------------------------------

        /// <summary>
        /// Byte offset in the decompressed page content stream where this
        /// paragraph's first text operator begins. Used for targeted replacement.
        /// </summary>
        public long ContentStreamOffset { get; set; }

        /// <summary>Length in bytes of the original content stream region.</summary>
        public long ContentStreamLength { get; set; }

        /// <summary>Index of the content stream (page may have multiple streams).</summary>
        public int ContentStreamIndex { get; set; }

        // ---- Edit state ----------------------------------------------------
        public bool IsBeingEdited { get; set; }
        public string? EditedText  { get; set; }

        /// <summary>True if the user has modified this paragraph since last save.</summary>
        public bool IsDirty => EditedText is not null && EditedText != PlainText;
    }

    // -------------------------------------------------------------------------
    // Encapsulates one page's fully-parsed layout.
    // -------------------------------------------------------------------------
    public sealed class PdfPageLayout
    {
        public int PageIndex { get; init; }
        public double PageWidthPts  { get; init; }
        public double PageHeightPts { get; init; }
        public List<ParagraphBlock> Paragraphs { get; } = new();
        public List<TextRun> AllRuns { get; } = new();
    }
}
