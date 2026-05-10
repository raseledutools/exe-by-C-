# Enterprise PDF Editor — Architecture & Developer Guide

## Overview

A production-ready WPF / .NET 8 PDF editor with **Acrobat Pro–level inline text editing**.  
True paragraph reflow, glyph-width-based measurement, seamless WPF overlay, and ISO 32000 content stream manipulation.

---

## Solution Structure

```
EnterprisePdfEditor/
│
├── EnterprisePdfEditor.csproj          # .NET 8 WPF project
├── App.xaml / App.xaml.cs             # Application entry point
│
├── Core/
│   ├── Models/
│   │   └── PdfDomainModels.cs         # PdfGlyph, PdfFontMetrics, TextRun,
│   │                                  # TextLine, ParagraphBlock, PdfPageLayout
│   │
│   ├── Engine/
│   │   ├── LayoutReconstructionEngine.cs  # THE CORE ALGORITHM (glyph extraction,
│   │   │                                  # clustering, paragraph detection, reflow)
│   │   └── ContentStreamWriter.cs         # PDF content stream manipulation,
│   │                                      # redaction, and text injection
│   │
│   ├── Rendering/
│   │   └── PdfiumRenderer.cs              # PDFium page rendering + CoordinateTransform
│   │
│   └── Services/
│       └── PdfDocumentService.cs          # High-level facade (load/save/edit/render)
│
└── UI/
    ├── MainWindow.xaml                # Full WPF shell layout
    ├── MainWindow.xaml.cs             # Code-behind
    ├── ViewModels/
    │   └── MainViewModel.cs           # MVVM ViewModel (commands, bindings)
    └── Controls/
        ├── InlineTextEditor.cs        # THE OVERLAY CONTROL — transparent RTB
        └── PdfPageView.cs             # Canvas host: rendering + hit-test + editor
```

---

## Architecture Deep-Dive

### 1. Layout Reconstruction Engine (`LayoutReconstructionEngine.cs`)

This is the algorithmic heart of the editor. It implements a 4-pass pipeline:

#### Pass 1 — Glyph Extraction (`GlyphExtractionStrategy`)

Implements iText7's `IEventListener` and registers for `RENDER_TEXT` events.  
For each `TextRenderInfo`, it:

- Resolves the font via `GetFont()` → extracts `PdfFontMetrics` (ascent, descent, cap height, per-glyph widths from the `Widths` array, italic angle, flags)
- Calls `GetCharacterRenderInfos()` to decompose the text run into **per-glyph** render infos
- For each glyph: records `(X, Y, Width, FontSize, HorizontalScaling, CharSpacing, WordSpacing)` from the PDF text state matrix
- All coordinates remain in **PDF user space** (origin bottom-left, Y-axis upward)

#### Pass 2 — Glyph → TextRun Clustering

Two adjacent glyphs merge into the same `TextRun` when:
- Same font resource name
- Same font size (within 0.01 pt)
- Same fill color (RGB within threshold 5)
- Baseline Y within `0.35 × lineHeight` (handles sub-pixel jitter)
- Horizontal gap < `1.5 × average glyph width` (rules out column breaks)

#### Pass 3 — TextRun → TextLine Clustering

Runs are on the same line when their baselines differ by less than `0.35 × lineHeight`.  
Runs within each line are sorted left→right by X.

#### Pass 4 — TextLine → ParagraphBlock Clustering

Lines join the same paragraph when **all three** hold:
1. **Vertical gap** between bottom of upper line and top of lower line ≤ `1.5 × lineHeight`
2. **Same dominant font** (resource name)
3. **Left-edge alignment** differs by less than `2.5 × fontSize` (rules out new columns)

This mirrors Acrobat Pro's paragraph detection heuristic for body text.

---

### 2. Font Metrics & Glyph Width Calculation (`PdfFontMetrics`)

```
GetGlyphAdvance(charCode, fontSize, horizontalScaling):
    w1000 = GlyphWidths[charCode] ?? DefaultWidth   // in glyph-space (1/1000 text unit)
    return (w1000 / 1000.0) * fontSize * horizontalScaling
```

This exactly mirrors the ISO 32000-1 §9.4.4 formula for horizontal advance:

```
tx = ((w0 - (Tj / 1000)) * Tfs + Tc + Tw) * Th
```

where `w0` is the glyph's horizontal advance, `Tfs` is font size, `Tc`/`Tw` are character/word spacing, and `Th` is horizontal scaling.

The `MeasureText()` method sums these advances across a string, which is the input to the reflow word-wrap.

---

### 3. Paragraph Reflow Algorithm (`ParagraphReflowEngine`)

The `Reflow()` method implements **greedy word-wrap** identical to TeX / Acrobat's simple mode:

```
for each hard line (split by \n):
    for each word:
        candidate = currentLine + " " + word
        candidateWidth = MeasureText(candidate, font, fontSize)
        
        if candidateWidth ≤ columnWidth:
            append word to currentLine
        else:
            flush currentLine → result.Lines
            start new line with word

flush final partial line
```

**BBox update after reflow:**

```
newHeight  = result.Lines.Count × lineHeight
newBottom  = originalTop − newHeight        // paragraph top is anchored
newBBox    = Rect(originalLeft, newBottom, columnWidth, newHeight)
```

The paragraph top is anchored (just as Acrobat anchors the paragraph's top baseline), so content grows downward. If the paragraph shrinks, the BBox also shrinks upward.

The `GetCursorX()` method computes the caret X position for a given character index by summing glyph advances up to that index — this is used to position the WPF blinking cursor pixel-perfectly over the corresponding PDF glyph position.

---

### 4. The Seamless Overlay Illusion

The editing experience involves four coordinated steps:

```
[Click paragraph]
    │
    ▼
BeginEdit()  →  ContentStreamWriter.RedactOriginalText()
                Appends: q … (white filled rect over BBox) … Q
    │
    ▼
RenderPage() via PDFium
                PDFium re-renders → white rect now covers original glyphs
    │
    ▼
InlineTextEditor.Activate()
                Positioned at paragraph.ScreenBoundingBox
                FontFamily / FontSize / FontWeight / FontStyle mirror PDF exactly
                Background = Transparent → RTB text drawn over the white rect
                User sees: their own RTB text, visually identical to PDF text
    │
    ▼ (user types / deletes / pastes)
InlineTextEditor.OnTextChanged()
                ParagraphReflowEngine.Reflow() → new line count
                RTB height expanded/contracted to match new BBox
    │
    ▼ (Tab / click outside / focus lost)
CommitEdit()
    │
    ├─ ParagraphReflowEngine.Reflow()  →  reflowedLines
    ├─ ContentStreamWriter.InjectEditedText()
    │       Appends BT … Tf Td Tj T* Tj … ET block to page content stream
    └─ RenderPage() via PDFium  →  fresh bitmap showing new text
```

---

### 5. Content Stream Manipulation

**Redaction** (making original text invisible):

```pdf
q
1 1 1 rg                 % fill color: white
x y w h re               % rectangle at paragraph BBox
f                        % fill
Q
```

This is appended as a new content stream object to the page's `Contents` array. Because PDF rendering is painter's model (last drawn = on top), the white rectangle paints over the original glyphs.

**Injection** (writing the new text):

```pdf
q
R G B rg                 % fill color from DominantColor
BT
/EditF12345 12 Tf        % font resource + size
14.4 TL                  % leading = lineHeight
x y Td                   % position at first baseline
(line 1 text) Tj
T*                        % move to next line
(line 2 text) Tj
...
ET
Q
```

The font is registered in the page's `Font` resource dictionary under a deterministic name (`EditF` + hash of BaseFont). For embedded subset fonts, the system falls back to the nearest standard Type1 font.

---

## Coordinate System

Two coordinate systems are in play:

| System         | Origin       | Y direction | Unit |
|---------------|-------------|------------|------|
| PDF user space | bottom-left  | upward     | pt (1/72 in) |
| WPF screen     | top-left     | downward   | DIP (1/96 in) |

`CoordinateTransform` handles the bidirectional conversion:

```
// PDF → Screen
screenX = offsetX + pdfX * scale
screenY = offsetY + (pageHeight − pdfY) * scale

// Screen → PDF  
pdfX = (screenX − offsetX) / scale
pdfY = pageHeight − (screenY − offsetY) / scale

// scale = (screenDpi / 72.0) × zoom
//       = (96.0 / 72.0) × 1.0 = 1.333 at 100% zoom on 96 DPI screen
```

---

## Dependencies

| Package | Version | Role |
|---------|---------|------|
| `itext7` | 8.0.4 | ISO 32000 PDF parsing, content stream events, font extraction, write-back |
| `itext7.bouncy-castle-adapter` | 8.0.4 | Encryption support required by iText7 |
| `PdfiumViewer` | 2.13.0 | Pixel-perfect page rasterization via Google's PDFium engine |
| `PdfiumViewer.Native.x86_64.v8-xfa` | 2018.4.8.256 | Native PDFium x64 binary |
| `Microsoft.Xaml.Behaviors.Wpf` | 1.1.77 | WPF behavior triggers |
| `CommunityToolkit.Mvvm` | 8.2.2 | `IRelayCommand`, `IAsyncRelayCommand`, source generators |
| `System.Drawing.Common` | 8.0.0 | GDI+ → WPF bitmap conversion |

---

## Building & Running

### Prerequisites

- **Visual Studio 2022** (17.8+) or **JetBrains Rider 2024+**
- **.NET 8 SDK** (Windows, x64)
- **Windows 10/11** (WPF + PDFium are Windows-only)

### Build

```bash
cd EnterprisePdfEditor
dotnet restore
dotnet build -c Release -r win-x64
dotnet run -c Release -r win-x64
```

Or open `EnterprisePdfEditor.csproj` in Visual Studio and press F5.

### Publish (standalone EXE)

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=false \
    -o ./publish
```

---

## Extending the Editor

### Supporting CID / Type0 Fonts (CJK)

For CIDFontType2 fonts, the width data is in the `W` array of the CIDFont dictionary, not `Widths`. Extend `ExtractFontMetrics()` in `GlyphExtractionStrategy`:

```csharp
var cidFont = fontDict.GetAsArray(PdfName.DescendantFonts)?[0] as PdfDictionary;
var wArray  = cidFont?.GetAsArray(PdfName.W);
// W format: [c1 [w1 w2 ...] c2 c3 w ...]
```

### Multi-Column Layout

Extend `ClusterLinesIntoParagraphs()` to detect column boundaries: if the left edge of a new line is more than `pageWidth * 0.4` to the right of the current paragraph, start a new paragraph column set.

### Undo/Redo Stack

Wrap `CommitEdit()` in a `Command` pattern:

```csharp
public interface IEditCommand { void Execute(); void Undo(); }
public class TextEditCommand : IEditCommand { ... }
// UndoStack<IEditCommand>
```

### Font Embedding for New Characters

When the user types a character not in the original font's subset, use iText7's `PdfFontFactory.CreateFont(fontPath, PdfEncodings.IDENTITY_H)` and embed the full font program, or use `FontProgram.CreateFont()` with the system font file.

---

## Known Limitations & Roadmap

| Item | Status |
|------|--------|
| Standard Type1 / TrueType paragraph editing | ✅ Implemented |
| CID / Type0 font width extraction | ⚠️ Partial (uses default width fallback) |
| Right-to-left text (Arabic, Hebrew) | 🔲 Not yet |
| Vertical text (CJK) | 🔲 Not yet |
| Multi-page reflow (text overflow to next page) | 🔲 Not yet |
| Tracked changes / compare | 🔲 Planned |
| Annotations editing | 🔲 Planned |
| Form fields editing | 🔲 Planned |
| Digital signature preservation | ⚠️ iText7 handles invalidation warning |
| PDFium partial-region render optimization | 🔲 Planned |

---

## License

Enterprise-internal use. Requires valid iText7 AGPL or commercial license.  
PdfiumViewer is Apache 2.0. PDFium (Google) is BSD.
