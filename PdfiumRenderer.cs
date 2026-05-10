// =============================================================================
// EnterprisePdfEditor — Core/Rendering/PdfiumRenderer.cs
//
// High-fidelity PDF page rendering using PDFium (via PdfiumViewer).
// Provides:
//   • RenderPage()  — renders a page to a BitmapSource at any DPI/zoom.
//   • PdfToScreen() — coordinate transform: PDF user-space → WPF screen pixels.
//   • ScreenToPdf() — inverse transform.
//   • HitTest()     — given a screen point, find the paragraph under cursor.
// =============================================================================

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using EnterprisePdfEditor.Core.Models;
using PdfiumViewer;

namespace EnterprisePdfEditor.Core.Rendering
{
    // =========================================================================
    // CoordinateTransform
    // Encapsulates the mapping between PDF user-space (pts, origin bottom-left)
    // and WPF/screen space (dips, origin top-left).
    // =========================================================================
    public sealed class CoordinateTransform
    {
        private readonly double _pageHeightPts;  // PDF page height in points
        private readonly double _scale;          // points-per-dip scale factor
        private readonly double _offsetX;        // left margin in dips
        private readonly double _offsetY;        // top margin in dips

        /// <summary>
        /// Create a transform for a given page rendered at 'scale' dips-per-point
        /// with optional viewport offset.
        /// </summary>
        public CoordinateTransform(double pageHeightPts, double scale,
                                   double offsetX = 0, double offsetY = 0)
        {
            _pageHeightPts = pageHeightPts;
            _scale         = scale;
            _offsetX       = offsetX;
            _offsetY       = offsetY;
        }

        public double Scale => _scale;

        // PDF user-space → WPF screen space
        public System.Windows.Point PdfToScreen(double pdfX, double pdfY)
            => new System.Windows.Point(
                _offsetX + pdfX * _scale,
                _offsetY + (_pageHeightPts - pdfY) * _scale);

        // WPF screen space → PDF user-space
        public System.Windows.Point ScreenToPdf(double screenX, double screenY)
            => new System.Windows.Point(
                (screenX - _offsetX) / _scale,
                _pageHeightPts - (screenY - _offsetY) / _scale);

        // Transform a PDF-space Rect to a WPF-space Rect.
        public Rect PdfRectToScreen(Rect pdfRect)
        {
            // PDF rect: (X=left, Y=bottom, W, H) — origin bottom-left.
            var topLeft  = PdfToScreen(pdfRect.Left,  pdfRect.Bottom + pdfRect.Height);
            var botRight = PdfToScreen(pdfRect.Right, pdfRect.Bottom);
            return new Rect(topLeft, botRight);
        }

        // Transform a WPF-space Rect to a PDF-space Rect.
        public Rect ScreenRectToPdf(Rect screenRect)
        {
            var pdfTopLeft = ScreenToPdf(screenRect.Left,  screenRect.Top);
            var pdfBotRight = ScreenToPdf(screenRect.Right, screenRect.Bottom);
            double left   = Math.Min(pdfTopLeft.X, pdfBotRight.X);
            double bottom = Math.Min(pdfTopLeft.Y, pdfBotRight.Y);
            double width  = Math.Abs(pdfBotRight.X - pdfTopLeft.X);
            double height = Math.Abs(pdfTopLeft.Y  - pdfBotRight.Y);
            return new Rect(left, bottom, width, height);
        }
    }

    // =========================================================================
    // PdfiumRenderer
    // Wraps PdfiumViewer.PdfDocument for rendering.
    // =========================================================================
    public sealed class PdfiumRenderer : IDisposable
    {
        private PdfiumViewer.PdfDocument? _pdfiumDoc;
        private bool _disposed;

        // Standard PDF resolution: 72 DPI = 1 point per screen pixel at 100%.
        public const double PdfBaseDpi = 72.0;

        public void Load(string filePath)
        {
            _pdfiumDoc?.Dispose();
            _pdfiumDoc = PdfiumViewer.PdfDocument.Load(filePath);
        }

        public void Load(Stream stream)
        {
            _pdfiumDoc?.Dispose();
            _pdfiumDoc = PdfiumViewer.PdfDocument.Load(stream);
        }

        public int PageCount => _pdfiumDoc?.PageCount ?? 0;

        public System.Windows.Size GetPageSize(int pageIndex)
        {
            if (_pdfiumDoc is null) return default;
            var size = _pdfiumDoc.PageSizes[pageIndex];
            return new System.Windows.Size(size.Width, size.Height);
        }

        // ------------------------------------------------------------------
        // RenderPage
        // Renders a page to a WPF BitmapSource at the given zoom factor.
        // zoom = 1.0 → 96 DPI (standard WPF DIP); 2.0 → 192 DPI (Retina).
        //
        // The rendered bitmap is in screen (top-left origin) coordinates.
        // ------------------------------------------------------------------
        public BitmapSource? RenderPage(int pageIndex, double zoom = 1.0,
                                        double screenDpi = 96.0)
        {
            if (_pdfiumDoc is null) return null;

            double renderDpi = screenDpi * zoom;
            var    pageSize  = _pdfiumDoc.PageSizes[pageIndex]; // in points (72 DPI units)

            int widthPx  = (int)Math.Ceiling(pageSize.Width  * renderDpi / PdfBaseDpi);
            int heightPx = (int)Math.Ceiling(pageSize.Height * renderDpi / PdfBaseDpi);

            using Bitmap bmp = _pdfiumDoc.Render(pageIndex, widthPx, heightPx,
                                                  (float)renderDpi, (float)renderDpi,
                                                  PdfRenderFlags.Annotations |
                                                  PdfRenderFlags.CorrectFromDpi);
            return BitmapFromGdiBitmap(bmp);
        }

        // ------------------------------------------------------------------
        // RenderRegion
        // Renders only a rectangular region of a page (in screen pixels after
        // zoom). Used to refresh just the paragraph area after editing.
        // ------------------------------------------------------------------
        public BitmapSource? RenderRegion(int pageIndex, Rect screenRectPx, double zoom,
                                          double screenDpi = 96.0)
        {
            // Render the full page then crop — simpler than PDFium's partial API.
            BitmapSource? full = RenderPage(pageIndex, zoom, screenDpi);
            if (full is null) return null;

            var crop = new CroppedBitmap(full, new Int32Rect(
                (int)screenRectPx.X,
                (int)screenRectPx.Y,
                Math.Max(1, (int)screenRectPx.Width),
                Math.Max(1, (int)screenRectPx.Height)));
            crop.Freeze();
            return crop;
        }

        // ------------------------------------------------------------------
        // HitTestParagraph
        // Given a point in PDF user-space, return the ParagraphBlock whose
        // BBox contains that point, or null.
        // ------------------------------------------------------------------
        public static ParagraphBlock? HitTestParagraph(
            PdfPageLayout layout, System.Windows.Point pdfPoint,
            double hitPaddingPts = 2.0)
        {
            foreach (ParagraphBlock para in layout.Paragraphs)
            {
                Rect inflated = Rect.Inflate(para.BoundingBox, hitPaddingPts, hitPaddingPts);
                if (inflated.Contains(pdfPoint))
                    return para;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // BuildTransform
        // Construct a CoordinateTransform for a given page, zoom, and viewport.
        // ------------------------------------------------------------------
        public CoordinateTransform BuildTransform(int pageIndex, double zoom,
                                                   double viewportOffsetX = 0,
                                                   double viewportOffsetY = 0,
                                                   double screenDpi = 96.0)
        {
            double pageHeightPts = GetPageSize(pageIndex).Height;
            // scale: screen DIPs per PDF point.
            // At 72 DPI (PDF native), 1 pt = 1 px. At 96 DPI, scale = 96/72.
            double scale = (screenDpi / PdfBaseDpi) * zoom;
            return new CoordinateTransform(pageHeightPts, scale,
                                           viewportOffsetX, viewportOffsetY);
        }

        // ------------------------------------------------------------------
        // Bitmap conversion helpers
        // ------------------------------------------------------------------
        private static BitmapSource BitmapFromGdiBitmap(Bitmap bmp)
        {
            var data = bmp.LockBits(
                new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var src = System.Windows.Media.Imaging.BitmapSource.Create(
                    data.Width, data.Height,
                    bmp.HorizontalResolution, bmp.VerticalResolution,
                    System.Windows.Media.PixelFormats.Bgra32, null,
                    data.Scan0, data.Stride * data.Height, data.Stride);
                src.Freeze();
                return src;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _pdfiumDoc?.Dispose();
            _disposed = true;
        }
    }
}
