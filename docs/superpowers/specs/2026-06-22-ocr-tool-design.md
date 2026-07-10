# OCR Tool (PDF & Image) — Design

**Date:** 2026-06-22
**Branch:** `feature/ocr-tool`

## Goal

Add a dedicated **OCR tool** to Llamashot's file-tools hub that extracts text from
**image files and PDFs** as accurately as possible. Produces a **searchable PDF**
(invisible text layer over the original) and **per-page / structured text**
(viewable, copyable, savable as `.txt`).

This is distinct from the existing screenshot OCR (`OcrHelper` over a captured
`BitmapSource`). The existing path is refactored to share the new engine layer.

## Decisions

- **Engine:** Tesseract 5 (LSTM) as the accurate **default**, plus the built-in
  **Windows.Media.Ocr** as a zero-dependency **fallback** engine the user can pick.
- **Outputs:** Searchable PDF + per-page/structured text (result viewer also offers
  Copy All and Save `.txt`).
- **PDF rendering for OCR:** 300 DPI.
- **Privacy:** fully offline. No cloud engine in this iteration.

## Architecture

### Engine abstraction — `Core/Ocr/`
- `IOcrEngine`
  - `Task<OcrPage> RecognizeAsync(BitmapSource image, string lang, CancellationToken ct)`
  - `bool IsAvailable { get; }`, `string Id`, `string DisplayName`
- `OcrWord` = `{ string Text; Rect Box; float Confidence; }` (box in image pixels)
- `OcrLine` = `{ string Text; List<OcrWord> Words; Rect Box; }`
- `OcrPage` = `{ string Text; List<OcrLine> Lines; int PixelWidth; int PixelHeight; }`
- `TesseractOcrEngine` — wraps `Tesseract` NuGet. `EngineMode.LstmOnly`, `PageSegMode.Auto`.
  Native libs + `tessdata/eng.traineddata` (best model) resolved from
  `AppContext.BaseDirectory`. `IsAvailable` = tessdata + native libs present. **Default.**
- `WindowsOcrEngine` — wraps `Windows.Media.Ocr`; maps `OcrResult` lines/words +
  `BoundingRect` into `OcrPage`. Always available on Win10+.
- `OcrEngines` registry: returns available engines; default = Tesseract if available
  else Windows.

### Input layer
- **Image** (`png/jpg/jpeg/bmp/tif/tiff/gif/webp`) → 1 page via
  `FileToolsService.LoadBitmapSourceFromFile`.
- **PDF** → render each page to a `BitmapSource` at 300 DPI (reuse
  `RenderPdfPageAsync`; password via existing `LoadPdfAsync`/encryption helpers) → N pages.
- **Preprocess** before recognition: convert to grayscale; upscale when the page is
  small / low-DPI (cap ~3×). Reuse ideas from existing `OcrHelper.PreprocessForOcr`.

### Orchestration — `Core/OcrService`
- `Task<OcrDocument> RecognizeAsync(string inputPath, OcrOptions opts, IProgress<int>?, CancellationToken)`
  - `OcrOptions` = `{ string EngineId; string Lang; int Dpi=300; bool MakeSearchablePdf; string? SearchablePdfPath; }`
  - `OcrDocument` = `{ List<OcrPage> Pages; string CombinedText; }`
- Renders/loads pages → runs the chosen engine per page (progress, cancellation) →
  assembles combined + per-page text → if requested, writes the searchable PDF.

### Searchable PDF — extend `FileToolsService` PDF writer
- New `WriteSearchablePdfAsync(IReadOnlyList<(BitmapSource pageImage, OcrPage ocr)> pages, string outputPath)`.
- Per page: draw the original page image (JPEG XObject, as today) then an **invisible
  text layer** (`Tr 3` render mode) — one positioned `BT…ET` block per word, font size
  scaled to the word box height, using base-14 **Helvetica** + WinAnsi encoding.
- Latin/WinAnsi first (covers English). Non-Latin searchable text layer = noted follow-up
  (full text output is still correct for non-Latin; only the *embedded* layer is limited).

### UI
- **New tile** in `FileToolsWindow` → **"OCR — Extract Text"** (image + PDF input).
- **New `OcrToolWindow`** (follows `DocumentScanWindow` pattern, theme tokens teal
  `#2DD4BF→#34D399`):
  - File picker (image or PDF), engine dropdown (Tesseract ▸ default / Windows),
    language, output checkboxes (Searchable PDF / Text).
  - **Run** → per-page progress → result pane: per-page text with page headers,
    **Copy All**, **Save .txt**, **Save Searchable PDF**. Cancel supported.
- Refactor `OcrHelper.ExtractTextAsync(BitmapSource)` to delegate to `WindowsOcrEngine`
  (or chosen engine) so there is a single recognition code path.

### Packaging
- Add `Tesseract` NuGet package (pulls native x64 runtime).
- Bundle `tessdata/eng.traineddata` (best) as content copied to output; ensure it ships
  in the Inno Setup installer alongside the self-contained single-file exe.
- Graceful degradation: if Tesseract assets are missing at runtime, the engine reports
  unavailable and the UI falls back to Windows OCR.

## Error handling
- Encrypted PDF → reuse existing password prompt flow.
- Engine unavailable → disable in dropdown with reason; auto-select fallback.
- Page render / recognition failure → record per-page error, continue other pages.
- Cancellation → return partial results already produced.

## Testing
- Harness routine (env-gated, like existing `LLAMASHOT_*`): OCR a known fixture image
  and a generated text PDF; assert extracted text contains expected strings and that the
  searchable PDF contains a text layer. Never touches real user data (per project rule).

## Out of scope (this iteration)
- Cloud OCR engine.
- Non-Latin embedded searchable-PDF text layer (full-text output still works).
- Handwriting-optimized models.
- Batch/folder OCR.
