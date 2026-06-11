# Fill & Sign PDF — Design Spec

**Date:** 2026-06-11
**Branch:** feature/file-tools
**Status:** Approved design — pending implementation plan

## Summary

Add a **Fill & Sign** tool to Llamashot's File Tools that lets a user open a PDF,
fill in flat (non-AcroForm) forms by typing into auto-detected field regions, tick
checkboxes/radios, sign (draw or upload), stamp, and insert date/time — then export a
PDF in which the original form stays crisp **vector** and the added content is **vector
and selectable**.

The motivating document (`C:\Users\DELL\Downloads\137.pdf`, NSDL/CRA "Request for
Change/Correction" form) is a **flat PDF**: scanning its bytes shows `AcroForm=0`,
`/Widget=0`, `/Annots=0`, `/FT=0` — the comb cells, checkboxes, and dd/mm/yyyy date
boxes are drawn graphics, not interactive form fields. Therefore field "detection"
cannot read PDF metadata; it must analyze the rendered page geometry.

## Decisions (confirmed with user)

| Decision | Choice |
|---|---|
| Output fidelity | **Vector text overlay** (selectable, crisp) |
| Comb-cell handling | **Single text box** per detected region (not 1-char-per-cell) |
| Rendering approach | **Approach B** — content injection into the original PDF via PDFsharp |
| PDF library | **PDFsharp** (MIT, managed, no native libs) — new dependency, approved |
| v1 element types | **All five**: Text, Check/Radio, Signature, Stamp, Date/Time |
| Signature input | **Draw (freehand) + Upload image** (no typed-cursive font in v1) |

## Architecture

### Tool surface

A new tool card **"Fill & Sign"** (id `fill_sign`) in the *PDF Tools* category. It is a
**separate** workspace from the existing PDF Editor, which is a thumbnail
reorder/rotate/delete grid (`PeLoadPdf` / `PeRenderPageGrid`) — the wrong shape for
single-page annotation.

Workspace layout (consistent with other File Tools panels):

- **Top toolbar:** tool-mode buttons `Select · Text · ✓ Check · Signature · Stamp ·
  Date/Time`, contextual font controls (family, size, **B** / *I*, color picker) shown
  for text-like modes/elements, and `Save PDF`.
- **Left:** vertical page thumbnail strip + page navigation (prev/next, page N of M).
- **Center:** large scrollable single-page canvas (the current page rendered via WinRT),
  with a hover-detection highlight layer and the placed elements (draggable, resize
  handles).
- **Empty state:** "Select PDF file / or drop here" CTA (matches the Resize/other tools).

### Page rendering & coordinate model

- Current page rendered with `Windows.Data.Pdf.PdfPage.RenderToStreamAsync` at a working
  DPI of **150** into a WPF `Image` inside a `Canvas`.
- Page size in **points** comes from `PdfPage.Size` (WinRT reports DIPs at 96/in →
  convert to PDF points at 72/in).
- Every placed element stores its rectangle in **PDF points with a top-left origin**,
  matching PDFsharp `XGraphics` (which is top-left, Y-down). This keeps the
  preview→export mapping a single scale factor with no Y-flip.
- Pixel ↔ point: `points = pixels / (renderDpi / 72)`.
- Page `.Rotation` is read from the source and applied so placement is correct on rotated
  pages.

### Hover auto-detection (heuristic; free-placement fallback)

No CV library is added (keeps the lean self-contained build). A custom lightweight
geometric detector runs **once per page**, cached, on a background thread. For a 150-DPI
A4 page (~1240×1750 px ≈ 2.1M px) the row/column scans are O(pixels), well under ~100 ms.

Algorithm on the grayscale page bitmap:

1. **Binarize** with a dark-pixel threshold.
2. **Horizontal segments:** per row, find runs of dark pixels longer than ~1.5% of page
   width → candidate underlines / box edges. Merge collinear runs.
3. **Vertical segments:** same, per column.
4. **Boxes:** pair top/bottom horizontal segments bounded by left/right vertical
   segments → rectangle regions. Small near-square boxes (side below a threshold) are
   classified as **checkbox/tick targets**.
5. **Comb rows:** a horizontal band bounded by a top and bottom line that contains
   evenly-spaced short vertical separators is a **comb**; the region is the band's
   bounding rectangle (treated as one text field per the "single text box" decision).
6. **Underlines:** long horizontal segments with no matching box → text baseline regions.

Interaction:

- On mouse-move, the detected region containing the cursor is highlighted (translucent
  blue outline).
- Click in the active tool mode snaps the element into the region: text box sized to the
  region height (font auto-sized to fit); tick centered in a square.
- **If no region is under the cursor, a click drops a free element at the cursor** —
  detection never blocks the user.

Detection is explicitly best-effort. The UI must not imply 100% coverage; free placement
is the guaranteed path.

### Element types (v1)

| Type | Behavior | Export rendering |
|---|---|---|
| **Text** | Editable box; font family (Arial/Times/Courier + Bold/Italic via system fonts), size, color, horizontal alignment. | `XGraphics.DrawString` with `XFont` + `XBrush` |
| **Check / Radio** | ✓ or ✗ glyph sized to box. Radio = a tick placed in the chosen box (no group exclusivity logic in v1). | `DrawString` of the glyph, or `DrawLines` |
| **Signature** | Draw freehand (WPF `InkCanvas`) **or** upload PNG (transparency preserved). Stored as image/strokes. | `DrawImage` (rendered strokes → image) |
| **Stamp** | Upload image, placed and resizable. | `DrawImage` |
| **Date/Time** | Insert current or chosen date/time as a text element (single box per comb). | same as Text |

Basic **delete element** and **undo-last-add** are in v1. Multi-step undo/redo and saving
editable state to disk are out of scope for v1.

### Export pipeline (Approach B)

1. `PdfReader.Open(originalPath, PdfDocumentOpenMode.Modify)`.
2. For each page with elements: obtain `XGraphics.FromPdfPage(page)`.
3. Convert each element's stored top-left point rect to PDFsharp's coordinate space (1:1,
   no flip) and draw: text/date via `DrawString`, signature/stamp via `DrawImage`, ink as
   polylines.
4. **Fonts:** use a PDFsharp `IFontResolver` mapping the offered families to system fonts
   (Arial / Times New Roman / Courier New) so glyphs embed (subset) and text stays
   selectable.
5. `document.Save(outputPath)`.

### Error handling & resilience

- If `PdfReader.Open` throws (AES-encrypted, unsupported structure, corrupt), show a clear
  message and offer a **rasterized fallback**: render pages to images, draw the same
  elements onto the bitmaps, repackage via the existing `ImagesToPdfAsync` (the
  Approach-A path). The user always gets output.
- Rotated and non-A4 page sizes handled via per-page size/rotation.
- Detection returning nothing → free placement (already the fallback).
- Unsaved-work guard consistent with the existing `ConfirmDiscardWork()` pattern: a
  Fill & Sign document with ≥1 element counts as unsaved work.

## Components & boundaries

- **`FillSignDetector`** (new, `Core/`): input = page bitmap + page size; output =
  `List<DetectedRegion>` (rect in points + kind: Underline/Box/Checkbox/Comb). Pure,
  testable in isolation.
- **`FillElement`** model (new, `Models/`): `{ Page, Type, RectPoints, Text,
  FontFamily, FontSize, Bold, Italic, ColorHex, ImagePath, InkData }`.
- **`FillSignExporter`** (new, `Core/`): input = original PDF path + `List<FillElement>`
  + output path; performs the PDFsharp injection and the rasterized fallback. Testable by
  exporting then re-reading text.
- **Fill & Sign UI** (in `FileToolsWindow`, following existing tool conventions): page
  view, tool modes, properties, hover highlight, element drag/resize. Talks to the three
  units above through their public interfaces only.

## Testing

Using the same standalone-harness style already used for verification (host real types,
drive real files, inspect outputs):

1. **Detection** — run `FillSignDetector` on the WinRT-rendered pages of `137.pdf`;
   assert it finds the PRAN comb row, the Receipt No. comb, and the Gender/Marital tick
   boxes (counts within tolerance).
2. **Coordinate round-trip** — a known pixel rect maps to the expected point rect and
   back within 1 px.
3. **Export fidelity** — fill a text element, export via PDFsharp, reopen and extract
   page text; assert the typed string is present (i.e. selectable, not rasterized).
4. **Fallback** — force the open-failure path; assert a valid rasterized PDF is produced.

## Risks / first implementation step

- **PDFsharp on net10-windows** must be validated first (restore + draw text + save on a
  sample). If it does not work on net10, revisit (alternative: PdfSharpCore, or fall back
  to Approach A). This is the **first task** in the implementation plan — a spike before
  UI work.
- Detection accuracy is heuristic; acceptance bar is "good enough that most fields snap,
  free placement covers the rest," not perfection.
- Font embedding/selectability depends on the `IFontResolver`; validated by test #3.

## Out of scope (v1)

- Reading/filling real AcroForm fields (this form has none; revisit if needed later).
- 1-character-per-comb-cell auto-distribution.
- Typed cursive-font signatures.
- Saving editable fill state to disk; multi-level undo/redo.
- Radio-group mutual exclusivity logic.
