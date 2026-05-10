// =============================================================================
// EnterprisePdfEditor — UI/Controls/PdfPageView.cs
//
// PdfPageView is the primary WPF rendering surface for a single PDF page.
//
// It is a Canvas that hosts:
//   [1] An Image control showing the PDFium-rendered bitmap.
//   [2] Semi-transparent paragraph highlight adorners (selection halos).
//   [3] The InlineTextEditor overlay, positioned precisely over the clicked
//       paragraph's screen BBox.
//
// Coordinate system:
//   • The Image fills the Canvas at (0,0).
//   • All screen coordinates are in WPF DIPs with origin at top-left.
//   • PDF user-space (origin bottom-left, Y up) is converted via CoordinateTransform.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EnterprisePdfEditor.Core.Engine;
using EnterprisePdfEditor.Core.Models;
using EnterprisePdfEditor.Core.Rendering;
using EnterprisePdfEditor.Core.Services;
using EnterprisePdfEditor.UI.Controls;

namespace EnterprisePdfEditor.UI.Controls
{
    // =========================================================================
    // PdfPageView
    // =========================================================================
    public sealed class PdfPageView : Canvas
    {
        // ------------------------------------------------------------------
        // Private fields
        // ------------------------------------------------------------------
        private readonly Image             _pageImage    = new() { Stretch = Stretch.None };
        private readonly InlineTextEditor  _editor       = new();
        private readonly List<Rectangle>   _paraHighlights = new();

        private PdfDocumentService?        _docService;
        private PdfPageLayout?             _pageLayout;
        private CoordinateTransform?       _transform;
        private ParagraphReflowEngine      _reflowEngine = new();

        private ParagraphBlock?            _activeParaBlock;
        private int                        _pageIndex;
        private double                     _zoom = 1.0;
        private double                     _screenDpi = 96.0;

        // Paragraph hover/selection highlight color.
        private static readonly SolidColorBrush HighlightBrush =
            new(Color.FromArgb(30, 0, 120, 215));
        private static readonly SolidColorBrush HoverBrush =
            new(Color.FromArgb(20, 0, 120, 215));
        private static readonly Pen SelectionBorderPen =
            new(new SolidColorBrush(Color.FromArgb(160, 0, 120, 215)), 1.0);

        // ------------------------------------------------------------------
        // Constructor
        // ------------------------------------------------------------------
        public PdfPageView()
        {
            Children.Add(_pageImage);
            Canvas.SetLeft(_pageImage, 0);
            Canvas.SetTop(_pageImage, 0);

            // The InlineTextEditor lives in this Canvas; hidden until activated.
            _editor.Visibility = Visibility.Collapsed;
            Children.Add(_editor);
            Panel.SetZIndex(_editor, 100);

            // Wire editor events.
            _editor.TextCommitted += OnEditorTextCommitted;
            _editor.EditCancelled += OnEditorCancelled;

            // Mouse events for hit-testing and editing.
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove           += OnMouseMove;
            Cursor               = Cursors.Arrow;

            Background = Brushes.White; // page background
            ClipToBounds = true;
        }

        // ------------------------------------------------------------------
        // Initialize
        // Called once by the parent ScrollViewer/host after the service loads.
        // ------------------------------------------------------------------
        public async Task InitializeAsync(
            PdfDocumentService docService,
            int pageIndex,
            double zoom,
            double screenDpi = 96.0)
        {
            _docService = docService;
            _pageIndex  = pageIndex;
            _zoom       = zoom;
            _screenDpi  = screenDpi;

            await RefreshAsync();
        }

        // ------------------------------------------------------------------
        // RefreshAsync
        // Re-render the page bitmap and update layout model + highlights.
        // ------------------------------------------------------------------
        public async Task RefreshAsync()
        {
            if (_docService is null) return;

            // Render bitmap.
            var bitmap = _docService.RenderPage(_pageIndex, _zoom, _screenDpi);
            if (bitmap is not null)
            {
                _pageImage.Source = bitmap;
                Width  = bitmap.PixelWidth  / (_screenDpi / 96.0);
                Height = bitmap.PixelHeight / (_screenDpi / 96.0);
            }

            // Build coordinate transform.
            _transform = _docService.BuildTransform(
                _pageIndex, _zoom, 0, 0, _screenDpi);

            // Load (or reload) the layout model.
            _pageLayout = await _docService.GetPageLayoutAsync(_pageIndex);

            // Refresh paragraph highlight adorners.
            RebuildHighlights();
        }

        // ------------------------------------------------------------------
        // Zoom support
        // ------------------------------------------------------------------
        public async Task SetZoomAsync(double zoom)
        {
            _zoom = zoom;
            await RefreshAsync();
        }

        // ------------------------------------------------------------------
        // Hit-testing and mouse handling
        // ------------------------------------------------------------------
        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_pageLayout is null || _transform is null) return;

            System.Windows.Point screenPt = e.GetPosition(this);
            System.Windows.Point pdfPt    = _transform.ScreenToPdf(screenPt.X, screenPt.Y);

            ParagraphBlock? hit = PdfiumRenderer.HitTestParagraph(_pageLayout, pdfPt);

            if (hit is null)
            {
                // Clicked outside any paragraph — commit & deactivate if editing.
                if (_activeParaBlock is not null) _editor.CommitIfActive();
                return;
            }

            if (_activeParaBlock == hit) return; // already editing this paragraph

            // If already editing another paragraph, commit first.
            if (_activeParaBlock is not null) _editor.CommitIfActive();

            ActivateEditor(hit);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_pageLayout is null || _transform is null) return;

            System.Windows.Point screenPt = e.GetPosition(this);
            System.Windows.Point pdfPt    = _transform.ScreenToPdf(screenPt.X, screenPt.Y);

            ParagraphBlock? hover = PdfiumRenderer.HitTestParagraph(_pageLayout, pdfPt);
            Cursor = hover is not null ? Cursors.IBeam : Cursors.Arrow;
        }

        // ------------------------------------------------------------------
        // ActivateEditor
        // Called when the user clicks a paragraph. Implements the illusion:
        //   1. Invoke BeginEdit → paints white rect over original glyphs.
        //   2. Re-render the page so the redaction is visible.
        //   3. Position & activate the InlineTextEditor over the BBox.
        // ------------------------------------------------------------------
        private async void ActivateEditor(ParagraphBlock paragraph)
        {
            if (_docService is null || _transform is null) return;

            _activeParaBlock = paragraph;

            // Step 1: Redact (white-out) original text in the PDF canvas.
            _docService.BeginEdit(paragraph, _transform);

            // Step 2: Re-render so the redaction shows in the bitmap.
            await RefreshAsync();

            // Step 3: Activate the overlay editor.
            _editor.Activate(paragraph, _transform, _reflowEngine, this);

            // Dim the paragraph highlight while editing.
            HighlightParagraph(paragraph, isEditing: true);
        }

        // ------------------------------------------------------------------
        // Editor event handlers
        // ------------------------------------------------------------------
        private async void OnEditorTextCommitted(object? sender, TextCommittedEventArgs e)
        {
            if (_docService is null) return;

            // Commit to the PDF content stream and get reflow result.
            var result = _docService.CommitEdit(e.Paragraph, e.NewText);

            _editor.Deactivate();
            _activeParaBlock = null;

            // Re-render the page with the new text.
            await RefreshAsync();
        }

        private async void OnEditorCancelled(object? sender, EventArgs e)
        {
            if (_docService is null || _activeParaBlock is null) return;

            _docService.CancelEdit(_activeParaBlock);
            _editor.Deactivate();
            _activeParaBlock = null;

            await RefreshAsync();
        }

        // ------------------------------------------------------------------
        // Paragraph highlight adorners
        // ------------------------------------------------------------------
        private void RebuildHighlights()
        {
            // Remove old highlight rectangles.
            foreach (var rect in _paraHighlights) Children.Remove(rect);
            _paraHighlights.Clear();

            if (_pageLayout is null || _transform is null) return;

            foreach (ParagraphBlock para in _pageLayout.Paragraphs)
            {
                Rect screenRect = _transform.PdfRectToScreen(para.BoundingBox);
                para.ScreenBoundingBox = screenRect;

                var rect = new Rectangle
                {
                    Width           = screenRect.Width,
                    Height          = screenRect.Height,
                    Fill            = HoverBrush,
                    Stroke          = null,
                    Visibility      = Visibility.Collapsed, // show on hover
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(rect, screenRect.Left);
                Canvas.SetTop(rect,  screenRect.Top);
                Panel.SetZIndex(rect, 10);

                Children.Add(rect);
                _paraHighlights.Add(rect);
            }
        }

        private void HighlightParagraph(ParagraphBlock para, bool isEditing)
        {
            // Find the highlight rect for this para (matched by screen position).
            Rect sr = para.ScreenBoundingBox;
            foreach (Rectangle r in _paraHighlights)
            {
                double rLeft = Canvas.GetLeft(r);
                double rTop  = Canvas.GetTop(r);
                if (Math.Abs(rLeft - sr.Left) < 1 && Math.Abs(rTop - sr.Top) < 1)
                {
                    r.Fill       = isEditing ? HighlightBrush : HoverBrush;
                    r.Stroke     = isEditing ? SelectionBorderPen.Brush : null;
                    r.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
                    break;
                }
            }
        }
    }

    // =========================================================================
    // Extension — expose CommitIfActive without breaking encapsulation.
    // =========================================================================
    internal static class InlineTextEditorExtensions
    {
        internal static void CommitIfActive(this InlineTextEditor editor)
        {
            if (editor.Visibility == Visibility.Visible)
                editor.RaiseCommit();
        }

        internal static void RaiseCommit(this InlineTextEditor editor)
        {
            // InlineTextEditor.CommitEdit() is private; we trigger it via LostFocus.
            editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }
}
