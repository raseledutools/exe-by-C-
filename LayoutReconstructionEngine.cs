// =============================================================================
// EnterprisePdfEditor — Core/Engine/LayoutReconstructionEngine.cs
//
// THE CORE ALGORITHM ENGINE (Acrobat-equivalent logic)
//
// Responsibilities:
//   1. Parse a PDF page content stream and extract every glyph with its
//      exact position, font, and metrics (ISO 32000-1 §9.4).
//   2. Cluster glyphs → TextRuns → TextLines → ParagraphBlocks using
//      spatial heuristics that mirror Acrobat Pro's paragraph detection.
//   3. Provide font-metric-aware glyph width calculation for accurate reflow.
//   4. Expose the paragraph reflow algorithm used during inline editing.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using EnterprisePdfEditor.Core.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using ITextColor = iText.Kernel.Colors.Color;
using ITextDeviceGray = iText.Kernel.Colors.DeviceGray;
using ITextVector = iText.Kernel.Geom.Vector;

namespace EnterprisePdfEditor.Core.Engine
{
    // =========================================================================
    // LayoutReconstructionEngine
    // =========================================================================
    public sealed class LayoutReconstructionEngine
    {
        // ------------------------------------------------------------------
        // Tuning constants (mirror Acrobat Pro heuristics)
        // ------------------------------------------------------------------

        /// <summary>
        /// Two glyphs are on the same line if their baseline Y values
        /// differ by less than this fraction of the dominant line height.
        /// </summary>
        private const double SameLineYTolerance = 0.35;

        /// <summary>
        /// Two consecutive lines are in the same paragraph if the gap between
        /// the bottom of the upper line and the top of the lower line is less
        /// than ParagraphGapFactor × lineHeight. Acrobat uses ≈1.5×.
        /// </summary>
        private const double ParagraphGapFactor = 1.5;

        /// <summary>
        /// Threshold: gap between glyphs larger than this fraction of the
        /// space-glyph width is considered an explicit word-space.
        /// </summary>
        private const double WordSpaceThreshold = 0.35;

        // ------------------------------------------------------------------
        // Public entry point
        // ------------------------------------------------------------------

        /// <summary>
        /// Parse the given PDF page and return its complete layout model.
        /// </summary>
        public PdfPageLayout ReconstructPage(PdfPage page, int pageIndex)
        {
            var mediaBox = page.GetMediaBox();
            var layout = new PdfPageLayout
            {
                PageIndex      = pageIndex,
                PageWidthPts   = mediaBox.GetWidth(),
                PageHeightPts  = mediaBox.GetHeight()
            };

            // Step 1: Extract raw glyphs from the content stream.
            var glyphExtractor = new GlyphExtractionStrategy(mediaBox.GetHeight());
            PdfCanvasProcessor processor = new(glyphExtractor);
            processor.ProcessPageContent(page);
            List<PdfGlyph> glyphs = glyphExtractor.GetGlyphs();

            if (glyphs.Count == 0) return layout;

            // Step 2: Cluster glyphs into TextRuns.
            var runs = ClusterGlyphsIntoRuns(glyphs);
            layout.AllRuns.AddRange(runs);

            // Step 3: Cluster runs into TextLines.
            var lines = ClusterRunsIntoLines(runs);

            // Step 4: Sort lines top→bottom in PDF space (descending Y).
            lines.Sort((a, b) => b.BaselineY.CompareTo(a.BaselineY));

            // Step 5: Cluster lines into ParagraphBlocks.
            var paragraphs = ClusterLinesIntoParagraphs(lines, pageIndex);
            layout.Paragraphs.AddRange(paragraphs);

            return layout;
        }

        // ------------------------------------------------------------------
        // Step 2: Glyph → TextRun clustering
        // Two adjacent glyphs belong to the same run when:
        //   • Same font resource name
        //   • Same font size (within 0.01 pt)
        //   • Same fill color
        //   • Same baseline Y (within SameLineYTolerance × lineHeight)
        //   • Horizontal gap is ≤ 1.5× the average glyph width (not a column break)
        // ------------------------------------------------------------------
        private static List<TextRun> ClusterGlyphsIntoRuns(List<PdfGlyph> glyphs)
        {
            var runs = new List<TextRun>();
            if (glyphs.Count == 0) return runs;

            // Sort by Y descending (top→bottom), then X ascending (left→right).
            glyphs.Sort((a, b) =>
            {
                int cy = b.Y.CompareTo(a.Y);
                return cy != 0 ? cy : a.X.CompareTo(b.X);
            });

            var current = new TextRun { FillColor = glyphs[0].FillColor };
            current.Glyphs.Add(glyphs[0]);

            for (int i = 1; i < glyphs.Count; i++)
            {
                var prev = glyphs[i - 1];
                var curr = glyphs[i];

                bool sameLine  = Math.Abs(curr.Y - prev.Y)
                                  < SameLineYTolerance * (prev.Font?.GetLineHeight(prev.FontSize) ?? prev.FontSize);
                bool sameFont  = curr.Font?.ResourceName == prev.Font?.ResourceName;
                bool sameSize  = Math.Abs(curr.FontSize - prev.FontSize) < 0.01;
                bool sameColor = curr.Font is not null &&
                                 ColorsAreClose(current.FillColor, curr.FillColor);

                // Horizontal gap check: if gap > 1.5 avg glyph width, start new run.
                double gap = curr.X - prev.Right;
                double avgW = prev.Font?.GetGlyphAdvance(prev.CharCode, prev.FontSize) ?? prev.FontSize * 0.5;
                bool noColumnBreak = gap < avgW * 1.5;

                if (sameLine && sameFont && sameSize && sameColor && noColumnBreak)
                {
                    current.Glyphs.Add(curr);
                }
                else
                {
                    runs.Add(current);
                    current = new TextRun { FillColor = curr.FillColor };
                    current.Glyphs.Add(curr);
                }
            }

            runs.Add(current);
            return runs;
        }

        // ------------------------------------------------------------------
        // Step 3: TextRun → TextLine clustering
        // Runs are on the same line if their baselines differ by less than
        // SameLineYTolerance × dominant line height.
        // ------------------------------------------------------------------
        private static List<TextLine> ClusterRunsIntoLines(List<TextRun> runs)
        {
            var lines = new List<TextLine>();
            if (runs.Count == 0) return lines;

            var current = new TextLine();
            current.Runs.Add(runs[0]);

            for (int i = 1; i < runs.Count; i++)
            {
                var prev = runs[i - 1];
                var curr = runs[i];

                double lh = prev.Font?.GetLineHeight(prev.FontSize) ?? prev.FontSize * 1.2;
                bool sameLine = Math.Abs(curr.BaselineY - prev.BaselineY) < SameLineYTolerance * lh;

                if (sameLine)
                {
                    current.Runs.Add(curr);
                }
                else
                {
                    // Sort runs left→right within the line before closing.
                    current.Runs.Sort((a, b) => a.Left.CompareTo(b.Left));
                    lines.Add(current);
                    current = new TextLine();
                    current.Runs.Add(curr);
                }
            }

            current.Runs.Sort((a, b) => a.Left.CompareTo(b.Left));
            lines.Add(current);
            return lines;
        }

        // ------------------------------------------------------------------
        // Step 4: TextLine → ParagraphBlock clustering
        // Lines belong to the same paragraph when:
        //   (a) vertical gap ≤ ParagraphGapFactor × line height
        //   (b) dominant font/size is the same (heuristic for body text)
        //   (c) X alignment is consistent (no large horizontal jump = new column)
        // ------------------------------------------------------------------
        private static List<ParagraphBlock> ClusterLinesIntoParagraphs(
            List<TextLine> sortedLines, int pageIndex)
        {
            var paragraphs = new List<ParagraphBlock>();
            if (sortedLines.Count == 0) return paragraphs;

            var current = new ParagraphBlock { PageIndex = pageIndex };
            current.Lines.Add(sortedLines[0]);

            for (int i = 1; i < sortedLines.Count; i++)
            {
                var prevLine = sortedLines[i - 1];
                var currLine = sortedLines[i];

                double lh = prevLine.LineHeight;

                // Vertical gap between bottom of previous line and top of current.
                double prevBottom = prevLine.BaselineY + (prevLine.Font?.GetDescent(prevLine.FontSize) ?? 0);
                double currTop    = currLine.BaselineY + (currLine.Font?.GetAscent(currLine.FontSize) ?? currLine.FontSize);
                double gap        = prevBottom - currTop; // in PDF space (Y grows up)

                bool sameFont    = prevLine.Font?.ResourceName == currLine.Font?.ResourceName;
                bool sameSize    = Math.Abs(prevLine.FontSize - currLine.FontSize) < 0.5;
                bool gapOk       = Math.Abs(gap) < ParagraphGapFactor * lh;

                // Horizontal alignment: left edge shouldn't jump by more than
                // 2× the font size (otherwise it's a new column / text box).
                bool alignOk = Math.Abs(currLine.Left - prevLine.Left) < prevLine.FontSize * 2.5;

                if (gapOk && sameFont && sameSize && alignOk)
                {
                    current.Lines.Add(currLine);
                }
                else
                {
                    FinalizeParagraph(current);
                    paragraphs.Add(current);
                    current = new ParagraphBlock { PageIndex = pageIndex };
                    current.Lines.Add(currLine);
                }
            }

            FinalizeParagraph(current);
            paragraphs.Add(current);
            return paragraphs;
        }

        private static void FinalizeParagraph(ParagraphBlock para)
        {
            if (para.Lines.Count == 0) return;

            // Compute bounding box in PDF user-space from constituent glyph bounds.
            double left   = para.Lines.Min(l => l.Left);
            double right  = para.Lines.Max(l => l.Right);
            double bottom = para.Lines.Min(l =>
                l.BaselineY + (l.Font?.GetDescent(l.FontSize) ?? 0));
            double top    = para.Lines.Max(l =>
                l.BaselineY + (l.Font?.GetAscent(l.FontSize) ?? l.FontSize));

            para.BoundingBox = new System.Windows.Rect(left, bottom, right - left, top - bottom);
        }

        // ------------------------------------------------------------------
        // Color comparison helper
        // ------------------------------------------------------------------
        private static bool ColorsAreClose(Color a, Color b, byte threshold = 5)
            => Math.Abs(a.R - b.R) < threshold
            && Math.Abs(a.G - b.G) < threshold
            && Math.Abs(a.B - b.B) < threshold;
    }

    // =========================================================================
    // GlyphExtractionStrategy
    // Implements iText7's IEventListener to capture every text rendering event
    // and convert it into a PdfGlyph with full font metrics.
    // =========================================================================
    internal sealed class GlyphExtractionStrategy : IEventListener
    {
        private readonly double _pageHeightPts;
        private readonly List<PdfGlyph> _glyphs = new();
        private readonly Dictionary<string, PdfFontMetrics> _fontCache = new();

        // iText7 event types we handle.
        private static readonly EventType[] AcceptedEvents =
            { EventType.RENDER_TEXT };

        public GlyphExtractionStrategy(double pageHeightPts)
        {
            _pageHeightPts = pageHeightPts;
        }

        public ICollection<EventType> GetSupportedEvents() => AcceptedEvents;

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;
            if (data is not TextRenderInfo tri) return;

            // ----------------------------------------------------------------
            // Extract font metrics (cached per resource name).
            // ----------------------------------------------------------------
            PdfFont iTextFont = tri.GetFont();
            string fontKey    = iTextFont?.GetFontProgram()?.GetFontNames()?.GetFontName()
                                ?? tri.GetFont()?.ToString() ?? "Unknown";

            if (!_fontCache.TryGetValue(fontKey, out PdfFontMetrics? metrics))
            {
                metrics = ExtractFontMetrics(iTextFont, fontKey);
                _fontCache[fontKey] = metrics;
            }

            // ----------------------------------------------------------------
            // Decompose the text render info into individual glyphs.
            // iText7 provides per-glyph render info via GetCharacterRenderInfos().
            // ----------------------------------------------------------------
            var charInfos = tri.GetCharacterRenderInfos();
            if (charInfos is null || charInfos.Count == 0) return;

            // Extract fill color from graphics state.
            Color fillColor = ExtractFillColor(tri);

            double fontSize    = tri.GetFontSize();
            double hScaling    = tri.GetHorizontalScaling() / 100.0;
            double charSpacing = tri.GetCharSpacing();
            double wordSpacing = tri.GetWordSpacing();

            foreach (TextRenderInfo charInfo in charInfos)
            {
                string charText = charInfo.GetText();
                if (string.IsNullOrEmpty(charText)) continue;

                // iText7 gives us the baseline start/end points in PDF user space.
                var startPt  = charInfo.GetBaseline().GetStartPoint();
                var endPt    = charInfo.GetBaseline().GetEndPoint();

                double x     = startPt.Get(ITextVector.I1);
                // PDF Y=0 is at page bottom. We keep PDF space here;
                // conversion to screen space is done in the view layer.
                double y     = startPt.Get(ITextVector.I2);

                double advW  = endPt.Get(ITextVector.I1) - x;
                // For negative advances (RTL) take absolute value.
                if (advW < 0) advW = -advW;

                byte charCode = charText.Length > 0 ? (byte)charText[0] : (byte)0;

                _glyphs.Add(new PdfGlyph
                {
                    Unicode           = char.ConvertToUtf32(charText, 0),
                    Text              = charText,
                    X                 = x,
                    Y                 = y,
                    Width             = advW,
                    FontSize          = fontSize,
                    Font              = metrics,
                    CharCode          = charCode,
                    HorizontalScaling = hScaling,
                    CharSpacing       = charSpacing,
                    WordSpacing       = wordSpacing
                });
            }
        }

        public List<PdfGlyph> GetGlyphs() => _glyphs;

        // ----------------------------------------------------------------
        // Font metric extraction from iText7 PdfFont object.
        // ----------------------------------------------------------------
        private static PdfFontMetrics ExtractFontMetrics(PdfFont? font, string fontKey)
        {
            if (font is null) return new PdfFontMetrics { ResourceName = fontKey, BaseFont = fontKey };

            var fp = font.GetFontProgram();
            var fi = fp?.GetFontNames();
            var fd = font.GetPdfObject()?.GetAsDictionary(PdfName.FontDescriptor);

            double ascent  = fd?.GetAsNumber(PdfName.Ascent)?.DoubleValue()  ?? 800;
            double descent = fd?.GetAsNumber(PdfName.Descent)?.DoubleValue() ?? -200;
            double capH    = fd?.GetAsNumber(PdfName.CapHeight)?.DoubleValue() ?? 700;
            double italA   = fd?.GetAsNumber(PdfName.ItalicAngle)?.DoubleValue() ?? 0;
            int    flags   = (int)(fd?.GetAsNumber(PdfName.Flags)?.LongValue() ?? 0);

            // BBox
            var bboxArr = fd?.GetAsArray(PdfName.FontBBox);
            System.Windows.Rect fontBBox = bboxArr != null && bboxArr.Size() >= 4
                ? new System.Windows.Rect(
                    bboxArr.GetAsNumber(0)?.DoubleValue() ?? 0,
                    bboxArr.GetAsNumber(1)?.DoubleValue() ?? 0,
                    (bboxArr.GetAsNumber(2)?.DoubleValue() ?? 1000) - (bboxArr.GetAsNumber(0)?.DoubleValue() ?? 0),
                    (bboxArr.GetAsNumber(3)?.DoubleValue() ?? 1000) - (bboxArr.GetAsNumber(1)?.DoubleValue() ?? 0))
                : new System.Windows.Rect(0, 0, 1000, 1000);

            // Per-glyph widths from the Widths array or W dictionary (CID fonts).
            var glyphWidths = new Dictionary<byte, double>();
            var widthsArr   = font.GetPdfObject()?.GetAsArray(PdfName.Widths);
            int firstChar   = font.GetPdfObject()?.GetAsNumber(PdfName.FirstChar)?.IntValue() ?? 0;

            if (widthsArr != null)
            {
                for (int i = 0; i < widthsArr.Size(); i++)
                {
                    int code = firstChar + i;
                    if (code is >= 0 and <= 255)
                    {
                        glyphWidths[(byte)code] =
                            widthsArr.GetAsNumber(i)?.DoubleValue() ?? 1000;
                    }
                }
            }

            double defaultW = font.GetPdfObject()?.GetAsNumber(PdfName.DW)?.DoubleValue() ?? 1000;

            return new PdfFontMetrics
            {
                ResourceName = fontKey,
                BaseFont     = fi?.GetFontName() ?? fontKey,
                Subtype      = font.GetPdfObject()?.GetAsName(PdfName.Subtype)?.GetValue() ?? "Type1",
                Ascent       = ascent,
                Descent      = descent,
                CapHeight    = capH,
                ItalicAngle  = italA,
                Flags        = flags,
                FontBBox     = fontBBox,
                GlyphWidths  = glyphWidths,
                DefaultWidth = defaultW
            };
        }

        private static Color ExtractFillColor(TextRenderInfo tri)
        {
            try
            {
                ITextColor? iColor = tri.GetFillColor();
                if (iColor is null) return Colors.Black;

                float[] comps = iColor.GetColorValue();

                if (iColor is ITextDeviceGray && comps.Length >= 1)
                {
                    byte v = (byte)(comps[0] * 255);
                    return Color.FromRgb(v, v, v);
                }
                if (comps.Length >= 3)
                {
                    return Color.FromRgb(
                        (byte)(comps[0] * 255),
                        (byte)(comps[1] * 255),
                        (byte)(comps[2] * 255));
                }
            }
            catch { /* fallback */ }

            return Colors.Black;
        }
    }

    // =========================================================================
    // ParagraphReflowEngine
    // Performs true word-wrap / paragraph reflow when the user edits text.
    // This is the math-heavy core of the "Acrobat-level" editing experience.
    // =========================================================================
    public sealed class ParagraphReflowEngine
    {
        // ------------------------------------------------------------------
        // ReflowResult: the output of a reflow pass.
        // ------------------------------------------------------------------
        public sealed class ReflowResult
        {
            /// <summary>Re-wrapped lines of text.</summary>
            public List<string> Lines { get; } = new();

            /// <summary>New bounding box of the paragraph in PDF user-space.</summary>
            public System.Windows.Rect NewBoundingBox { get; set; }

            /// <summary>Whether the paragraph grew or shrank vertically.</summary>
            public bool HeightChanged { get; set; }
        }

        // ------------------------------------------------------------------
        // Reflow the edited plain text to fit within the original column width.
        //
        // Algorithm:
        //   1. Tokenize editedText into words (preserving manual newlines).
        //   2. For each line, greedily accumulate words until the measured
        //      width exceeds columnWidth.
        //   3. When a word causes overflow, it starts a new line.
        //   4. Re-compute the paragraph bounding box from the new line count.
        //
        // The returned ReflowResult contains the wrapped lines and the new BBox.
        // ------------------------------------------------------------------
        public ReflowResult Reflow(
            ParagraphBlock originalParagraph,
            string editedText,
            double columnWidth,       // PDF user-space points
            PdfFontMetrics font,
            double fontSize)
        {
            var result = new ReflowResult();

            // Split on explicit newlines first (user-pressed Enter).
            string[] hardLines = editedText.Split('\n');

            foreach (string hardLine in hardLines)
            {
                string[] words = hardLine.Split(' ', StringSplitOptions.None);
                var currentLine = new System.Text.StringBuilder();
                double currentWidth = 0;

                foreach (string word in words)
                {
                    string candidate   = currentLine.Length == 0 ? word : " " + word;
                    double candidateW  = MeasureText(candidate, font, fontSize);

                    if (currentLine.Length == 0)
                    {
                        // First word on line: always fits (even if too wide — can't break a word).
                        currentLine.Append(word);
                        currentWidth = MeasureText(word, font, fontSize);
                    }
                    else if (currentWidth + candidateW <= columnWidth)
                    {
                        // Word fits — append.
                        currentLine.Append(candidate);
                        currentWidth += candidateW;
                    }
                    else
                    {
                        // Word does NOT fit — flush current line, start new.
                        result.Lines.Add(currentLine.ToString());
                        currentLine.Clear();
                        currentLine.Append(word);
                        currentWidth = MeasureText(word, font, fontSize);
                    }
                }

                // Flush last partial line.
                if (currentLine.Length > 0)
                    result.Lines.Add(currentLine.ToString());
                else
                    result.Lines.Add(string.Empty); // preserve empty hard line
            }

            // ----------------------------------------------------------------
            // Compute new bounding box.
            // ----------------------------------------------------------------
            int    newLineCount = result.Lines.Count;
            double lineHeight   = font.GetLineHeight(fontSize);
            double newHeight    = newLineCount * lineHeight;

            // The top of the paragraph stays anchored (as Acrobat does);
            // the bottom moves down if text grew, up if text shrank.
            double origTop = originalParagraph.PdfTop;

            result.NewBoundingBox = new System.Windows.Rect(
                originalParagraph.PdfLeft,
                origTop - newHeight,        // new bottom = top − newHeight
                originalParagraph.ColumnWidth,
                newHeight);

            result.HeightChanged =
                Math.Abs(newHeight - originalParagraph.BoundingBox.Height) > 0.5;

            return result;
        }

        // ------------------------------------------------------------------
        // Measure the horizontal advance of a string in PDF user-space points.
        // Includes character spacing (Tc=0 default for clean measurement).
        // ------------------------------------------------------------------
        public double MeasureText(string text, PdfFontMetrics font, double fontSize,
            double tc = 0, double tw = 0, double th = 1.0)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double total = 0;
            foreach (char c in text)
            {
                byte code = (byte)c;
                double glyphW = font.GetGlyphAdvance(code, fontSize, th);
                double spacing = tc + (c == ' ' ? tw : 0);
                total += glyphW + spacing;
            }
            return total;
        }

        // ------------------------------------------------------------------
        // Given a glyph's character code, return its exact width in PDF
        // user-space points (the quantity used for BBox expansion/shrinkage).
        // This is the function called on every keystroke in the editor.
        // ------------------------------------------------------------------
        public double GetExactGlyphWidth(byte charCode, PdfFontMetrics font, double fontSize,
            double horizontalScaling = 1.0)
            => font.GetGlyphAdvance(charCode, fontSize, horizontalScaling);

        // ------------------------------------------------------------------
        // Compute the cursor X position within a line after 'charIndex' chars.
        // Used to position the WPF caret over the PDF content correctly.
        // ------------------------------------------------------------------
        public double GetCursorX(string lineText, int charIndex,
            PdfFontMetrics font, double fontSize,
            double lineStartX, double tc = 0, double tw = 0, double th = 1.0)
        {
            double x = lineStartX;
            int limit = Math.Min(charIndex, lineText.Length);
            for (int i = 0; i < limit; i++)
            {
                char c = lineText[i];
                x += font.GetGlyphAdvance((byte)c, fontSize, th)
                     + tc
                     + (c == ' ' ? tw : 0);
            }
            return x;
        }
    }
}
