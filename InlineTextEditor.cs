// =============================================================================
// EnterprisePdfEditor — UI/Controls/InlineTextEditor.cs
//
// THE SEAMLESS OVERLAY CONTROL — the "Acrobat Illusion"
//
// InlineTextEditor is a transparent, borderless WPF RichTextBox that:
//   1. Is precisely positioned and sized over the paragraph's screen BBox.
//   2. Mirrors the PDF paragraph's font family, size, weight, style, and color
//      so the user cannot distinguish it from the rendered PDF glyphs.
//   3. Fires events when the user commits (Tab/Escape/click-outside) or
//      cancels the edit.
//   4. Integrates with ParagraphReflowEngine to dynamically resize itself
//      as the user types, providing live reflow feedback.
// =============================================================================

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EnterprisePdfEditor.Core.Engine;
using EnterprisePdfEditor.Core.Models;

namespace EnterprisePdfEditor.UI.Controls
{
    // =========================================================================
    // InlineTextEditor
    // =========================================================================
    public sealed class InlineTextEditor : RichTextBox
    {
        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------
        public event EventHandler<TextCommittedEventArgs>? TextCommitted;
        public event EventHandler?                          EditCancelled;

        // ------------------------------------------------------------------
        // Private state
        // ------------------------------------------------------------------
        private ParagraphBlock?        _paragraph;
        private CoordinateTransform?   _transform;
        private ParagraphReflowEngine? _reflowEngine;
        private double                 _columnWidthPts;
        private string                 _originalText = string.Empty;
        private bool                   _suppressEvents;

        // ------------------------------------------------------------------
        // Constructor — configure the RTB to look invisible over the PDF.
        // ------------------------------------------------------------------
        public InlineTextEditor()
        {
            // Make the control itself transparent so only the text is visible.
            Background         = Brushes.Transparent;
            BorderBrush        = Brushes.Transparent;
            BorderThickness    = new Thickness(0);
            Padding            = new Thickness(0);
            Margin             = new Thickness(0);
            IsUndoEnabled      = true;
            AcceptsReturn      = true;
            AcceptsTab         = false; // Tab = commit
            VerticalScrollBarVisibility   = ScrollBarVisibility.Hidden;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            Cursor             = Cursors.IBeam;
            CaretBrush         = new SolidColorBrush(Colors.DodgerBlue);

            // Use exact pixel layout — critical for glyph alignment.
            UseLayoutRounding  = true;
            SnapsToDevicePixels = true;

            TextChanged += OnTextChanged;
            LostFocus   += OnLostFocus;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        // ------------------------------------------------------------------
        // Activate
        // Configure and position the overlay for a specific paragraph.
        // Called by PdfPageView when the user clicks on a paragraph.
        // ------------------------------------------------------------------
        public void Activate(
            ParagraphBlock paragraph,
            CoordinateTransform transform,
            ParagraphReflowEngine reflowEngine,
            UIElement parent)
        {
            _paragraph     = paragraph;
            _transform     = transform;
            _reflowEngine  = reflowEngine;
            _columnWidthPts = paragraph.ColumnWidth;
            _originalText  = paragraph.PlainText;

            // ----------------------------------------------------------------
            // Apply font styling to match the PDF paragraph exactly.
            // ----------------------------------------------------------------
            ApplyFontToDocument(paragraph);

            // ----------------------------------------------------------------
            // Load the paragraph's plain text into the RTB.
            // ----------------------------------------------------------------
            _suppressEvents = true;
            Document.Blocks.Clear();
            var para = new Paragraph();
            para.Inlines.Add(new Run(paragraph.PlainText));
            Document.Blocks.Add(para);
            _suppressEvents = false;

            // ----------------------------------------------------------------
            // Size & position the control over the paragraph's screen BBox.
            // ----------------------------------------------------------------
            PositionOverParagraph(paragraph, transform, parent);

            // ----------------------------------------------------------------
            // Make visible and grab focus.
            // ----------------------------------------------------------------
            Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                new Action(() =>
                {
                    Focus();
                    // Move caret to end.
                    CaretPosition = Document.ContentEnd;
                }));
        }

        // ------------------------------------------------------------------
        // Deactivate
        // Hide the overlay and reset state.
        // ------------------------------------------------------------------
        public void Deactivate()
        {
            Visibility = Visibility.Collapsed;
            _paragraph  = null;
            _transform  = null;
        }

        // ------------------------------------------------------------------
        // GetCurrentText
        // Extract the current plain text from the RTB document.
        // ------------------------------------------------------------------
        public string GetCurrentText()
        {
            var textRange = new TextRange(Document.ContentStart, Document.ContentEnd);
            return textRange.Text.TrimEnd('\r', '\n');
        }

        // ------------------------------------------------------------------
        // ApplyFontToDocument
        // Set the RTB's default paragraph formatting to match the PDF font.
        // ------------------------------------------------------------------
        private void ApplyFontToDocument(ParagraphBlock paragraph)
        {
            var metrics = paragraph.DominantFont;
            double fontSize = paragraph.DominantFontSize;

            FontFamily = metrics.ResolveWpfFontFamily();
            FontSize   = PointsToDips(fontSize);
            FontWeight = metrics.ResolveWpfFontWeight();
            FontStyle  = metrics.ResolveWpfFontStyle();
            Foreground = new SolidColorBrush(paragraph.DominantColor);

            // Line height: match the PDF leading as closely as WPF allows.
            double lineHeightPts = metrics.GetLineHeight(fontSize);
            double lineHeightDip = PointsToDips(lineHeightPts);

            // Apply via paragraph properties in the FlowDocument.
            foreach (Block block in Document.Blocks)
            {
                if (block is Paragraph wpfPara)
                {
                    wpfPara.LineHeight         = lineHeightDip;
                    wpfPara.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    wpfPara.Margin             = new Thickness(0);
                    wpfPara.Padding            = new Thickness(0);
                }
            }

            Document.LineHeight         = lineHeightDip;
            Document.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            Document.PagePadding        = new Thickness(0);
        }

        // ------------------------------------------------------------------
        // PositionOverParagraph
        // Place the control at exactly the screen coordinates of the BBox.
        // ------------------------------------------------------------------
        private void PositionOverParagraph(
            ParagraphBlock paragraph,
            CoordinateTransform transform,
            UIElement parent)
        {
            // Convert PDF BBox → screen rect in DIPs.
            Rect screenRect = transform.PdfRectToScreen(paragraph.BoundingBox);
            paragraph.ScreenBoundingBox = screenRect;

            // Apply position via the parent Canvas (PdfPageView uses a Canvas).
            Canvas.SetLeft(this, screenRect.Left);
            Canvas.SetTop(this,  screenRect.Top);

            Width  = Math.Max(screenRect.Width,  50);
            Height = Math.Max(screenRect.Height, PointsToDips(paragraph.DominantFontSize * 1.5));
        }

        // ------------------------------------------------------------------
        // OnTextChanged
        // Called on every keystroke. Runs live reflow to resize the control.
        // ------------------------------------------------------------------
        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents || _paragraph is null || _reflowEngine is null
                || _transform is null) return;

            string currentText = GetCurrentText();
            var font    = _paragraph.DominantFont;
            double ptSz = _paragraph.DominantFontSize;

            // Run reflow to get new line count and bounding box.
            var result = _reflowEngine.Reflow(
                _paragraph, currentText, _columnWidthPts, font, ptSz);

            // Resize the RTB height to accommodate new line count.
            double newHeightPts = result.NewBoundingBox.Height;
            double newHeightDip = PointsToDips(newHeightPts) + 4; // 4px breathing room
            if (Math.Abs(Height - newHeightDip) > 1.0)
                Height = Math.Max(newHeightDip, PointsToDips(ptSz * 1.5));
        }

        // ------------------------------------------------------------------
        // OnPreviewKeyDown
        // • Enter: insert newline (default RTB behavior via AcceptsReturn=true).
        // • Tab  : commit edit.
        // • Esc  : cancel edit.
        // ------------------------------------------------------------------
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                EditCancelled?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                CommitEdit();
                return;
            }
        }

        // ------------------------------------------------------------------
        // OnLostFocus
        // When the overlay loses keyboard focus (user clicked elsewhere),
        // commit the edit automatically (Acrobat behavior).
        // ------------------------------------------------------------------
        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (_paragraph is null || Visibility != Visibility.Visible) return;
            CommitEdit();
        }

        // ------------------------------------------------------------------
        // CommitEdit
        // ------------------------------------------------------------------
        private void CommitEdit()
        {
            if (_paragraph is null) return;
            string text = GetCurrentText();
            TextCommitted?.Invoke(this, new TextCommittedEventArgs(_paragraph, text));
        }

        // ------------------------------------------------------------------
        // Utility: convert PDF points to WPF DIPs.
        // 1 point = 1/72 inch. At 96 DPI: 1 DIP = 1/96 inch.
        // ∴ 1 pt = 96/72 DIP = 4/3 DIP.
        // ------------------------------------------------------------------
        private static double PointsToDips(double pts) => pts * (96.0 / 72.0);
    }

    // =========================================================================
    // TextCommittedEventArgs
    // =========================================================================
    public sealed class TextCommittedEventArgs : EventArgs
    {
        public ParagraphBlock Paragraph { get; }
        public string         NewText   { get; }

        public TextCommittedEventArgs(ParagraphBlock paragraph, string newText)
        {
            Paragraph = paragraph;
            NewText   = newText;
        }
    }
}
