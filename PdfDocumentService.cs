// =============================================================================
// EnterprisePdfEditor — Core/Services/PdfDocumentService.cs
//
// High-level service that orchestrates:
//   • Loading a PDF with iText7 (for write access) + PDFium (for rendering).
//   • Extracting layouts via LayoutReconstructionEngine (per page, lazy).
//   • Committing edits via ContentStreamWriter.
//   • Saving the modified document.
// =============================================================================

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using EnterprisePdfEditor.Core.Engine;
using EnterprisePdfEditor.Core.Models;
using EnterprisePdfEditor.Core.Rendering;
using iText.Kernel.Pdf;

namespace EnterprisePdfEditor.Core.Services
{
    public sealed class PdfDocumentService : IDisposable
    {
        // ---- iText7 (write + content stream manipulation) ----------------
        private PdfDocument?      _iTextDoc;
        private PdfReader?        _reader;
        private PdfWriter?        _writer;
        private MemoryStream?     _writeStream;   // in-memory edit buffer

        // ---- PDFium (pixel-perfect rendering) ----------------------------
        private readonly PdfiumRenderer _renderer = new();

        // ---- Engines -------------------------------------------------------
        private readonly LayoutReconstructionEngine _layoutEngine = new();
        private readonly ParagraphReflowEngine      _reflowEngine  = new();
        private ContentStreamWriter?                _streamWriter;

        // ---- State ---------------------------------------------------------
        private string _filePath = string.Empty;
        private readonly ConcurrentDictionary<int, PdfPageLayout> _layoutCache = new();

        public bool IsLoaded   => _iTextDoc is not null;
        public int  PageCount  => _renderer.PageCount;
        public string FilePath => _filePath;

        // ====================================================================
        // Load
        // ====================================================================
        public void Load(string filePath)
        {
            Dispose(); // clean up previous doc

            _filePath    = filePath;
            _writeStream = new MemoryStream();

            // iText7: open for reading + writing (in-memory copy).
            _reader  = new PdfReader(filePath);
            _writer  = new PdfWriter(_writeStream);
            _iTextDoc = new PdfDocument(_reader, _writer);
            _iTextDoc.SetCloseWriter(false); // we control the write lifecycle

            _streamWriter = new ContentStreamWriter(_iTextDoc);

            // PDFium: load directly from disk for rendering (independent instance).
            _renderer.Load(filePath);

            _layoutCache.Clear();
        }

        // ====================================================================
        // GetPageLayout  (lazy-cached, can be called from background thread)
        // ====================================================================
        public async Task<PdfPageLayout> GetPageLayoutAsync(int pageIndex)
        {
            if (_layoutCache.TryGetValue(pageIndex, out var cached)) return cached;

            return await Task.Run(() =>
            {
                if (_iTextDoc is null) throw new InvalidOperationException("No document loaded.");
                PdfPage layout = _iTextDoc.GetPage(pageIndex + 1);
                var result = _layoutEngine.ReconstructPage(layout, pageIndex);
                _layoutCache[pageIndex] = result;
                return result;
            });
        }

        // ====================================================================
        // RenderPage
        // ====================================================================
        public System.Windows.Media.Imaging.BitmapSource? RenderPage(
            int pageIndex, double zoom = 1.0, double screenDpi = 96.0)
            => _renderer.RenderPage(pageIndex, zoom, screenDpi);

        // ====================================================================
        // BeginEdit
        // Redacts the original paragraph text (paints white rect) so the
        // WPF overlay RichTextBox can appear seamlessly over it.
        // ====================================================================
        public void BeginEdit(ParagraphBlock paragraph, CoordinateTransform transform)
        {
            if (_streamWriter is null) return;

            // Convert BoundingBox (PDF space) → iText Rect (PDF space, 1-based page).
            var bbox = paragraph.BoundingBox;
            var iRect = new iText.Kernel.Geom.Rectangle(
                (float)bbox.X, (float)bbox.Y,
                (float)bbox.Width, (float)bbox.Height);

            _streamWriter.RedactOriginalText(paragraph.PageIndex, iRect);
            paragraph.IsBeingEdited = true;
        }

        // ====================================================================
        // CommitEdit
        // Accept edited text: reflow it and write the new content stream.
        // ====================================================================
        public ParagraphReflowEngine.ReflowResult CommitEdit(
            ParagraphBlock paragraph, string newText)
        {
            if (_streamWriter is null) throw new InvalidOperationException("No document.");

            var font      = paragraph.DominantFont;
            var fontSize  = paragraph.DominantFontSize;
            var colWidth  = paragraph.ColumnWidth;

            // Reflow.
            var result = _reflowEngine.Reflow(paragraph, newText, colWidth, font, fontSize);

            // Inject.
            _streamWriter.InjectEditedText(
                paragraph.PageIndex, paragraph, result.Lines, font);

            // Update paragraph model.
            paragraph.EditedText     = newText;
            paragraph.BoundingBox    = result.NewBoundingBox;
            paragraph.IsBeingEdited  = false;

            // Invalidate layout cache for this page so next GetPageLayout re-parses.
            _layoutCache.TryRemove(paragraph.PageIndex, out _);

            return result;
        }

        // ====================================================================
        // CancelEdit
        // Discard changes: revert the redaction and restore the original text.
        // ====================================================================
        public void CancelEdit(ParagraphBlock paragraph)
        {
            _streamWriter?.RevertRedaction(paragraph.PageIndex, paragraph);
            paragraph.IsBeingEdited = false;
            paragraph.EditedText    = null;
        }

        // ====================================================================
        // Save / SaveAs
        // ====================================================================
        public void Save()
        {
            if (_iTextDoc is null || _writeStream is null) return;

            // Flush iText7 changes to the memory stream.
            _iTextDoc.Close(); // writes to _writeStream

            // Write memory stream → disk (atomic via temp file).
            string tempPath = _filePath + ".tmp";
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                _writeStream.Position = 0;
                _writeStream.CopyTo(fs);
            }
            File.Replace(tempPath, _filePath, _filePath + ".bak");

            // Reload for further editing.
            Load(_filePath);
        }

        public void SaveAs(string newPath)
        {
            if (_iTextDoc is null || _writeStream is null) return;

            _iTextDoc.Close();

            _writeStream.Position = 0;
            using var fs = new FileStream(newPath, FileMode.Create, FileAccess.Write);
            _writeStream.CopyTo(fs);

            _filePath = newPath;
            Load(newPath);
        }

        // ====================================================================
        // CoordinateTransform factory
        // ====================================================================
        public CoordinateTransform BuildTransform(int pageIndex, double zoom,
            double offsetX = 0, double offsetY = 0, double dpi = 96.0)
            => _renderer.BuildTransform(pageIndex, zoom, offsetX, offsetY, dpi);

        // ====================================================================
        // IDisposable
        // ====================================================================
        public void Dispose()
        {
            try { _iTextDoc?.Close(); } catch { }
            _writeStream?.Dispose();
            _renderer.Dispose();
            _iTextDoc    = null;
            _reader      = null;
            _writer      = null;
            _writeStream = null;
            _streamWriter = null;
        }
    }
}
