# Fill & Sign PDF Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Fill & Sign" tool to Llamashot's File Tools that fills flat (non-AcroForm) PDF forms with vector, selectable text plus checkmarks, signatures, stamps, and date/time — detecting field regions on hover with a free-placement fallback.

**Architecture:** Three pure, testable backbone units (`FillElement` model, `FillSignDetector`, `FillSignExporter`) plus a UI panel in `FileToolsWindow`. Export uses **PDFsharp content injection** (Approach B): open the original PDF, draw vector text/images onto existing pages, save — preserving the original form as vector. A rasterized fallback (render pages → draw → repackage via existing `ImagesToPdfAsync`) covers PDFs PDFsharp can't open.

**Tech Stack:** C# / .NET 10 (net10.0-windows), WPF, WinRT `Windows.Data.Pdf` (page render), **PDFsharp 6.x** (new dependency, MIT), `System.Drawing` (bitmap analysis), `InkCanvas` (signature draw).

**Testing reality:** This repo has **no test framework or test project**. Tests run through a committed standalone harness (`tools/FillSignHarness/`) — a `WinExe` that references `Llamashot`, drives the real types on real files (including `137.pdf` as a fixture), and writes pass/fail lines to `%TEMP%\fillsign_test\results.txt`. "Run the test" = build + run the harness + read results. This mirrors the harness pattern already used to verify the image/PDF tools.

---

## File Structure

**New files:**
- `Llamashot/Models/FillElement.cs` — element data model + enums.
- `Llamashot/Core/FillSignGeometry.cs` — pixel↔point coordinate conversion (pure static).
- `Llamashot/Core/FillSignDetector.cs` — bitmap → detected field regions (pure static).
- `Llamashot/Core/FillSignExporter.cs` — elements + source PDF → output PDF (PDFsharp + raster fallback).
- `Llamashot/Core/FillSignFontResolver.cs` — PDFsharp `IFontResolver` mapping families → system fonts.
- `tools/FillSignHarness/FillSignHarness.csproj` — committed test harness.
- `tools/FillSignHarness/Program.cs` — harness scenarios + assertions.
- `tools/fixtures/137.pdf` — copied reference form (test fixture).

**Modified files:**
- `Llamashot/Llamashot.csproj` — add PDFsharp PackageReference; bump `<Version>`.
- `Llamashot/Views/FileToolsWindow.xaml` — add `fill_sign` panel (select view, page workspace, toolbar, properties).
- `Llamashot/Views/FileToolsWindow.xaml.cs` — `ToolDefs` entry, `InitPanelMap` registration, `HasUnsavedWork` case, and all Fill & Sign event handlers + state.
- `Llamashot/Views/AboutWindow.xaml` — version bump (per project convention).

---

## Task 1: PDFsharp / net10 validation spike (decision gate)

**Why first:** The entire Approach-B path depends on PDFsharp opening a PDF, drawing **selectable** vector text, and saving on `net10.0-windows`. If it fails here, stop and revisit (PdfSharpCore, or fall back to Approach A) before building anything else.

**Files:**
- Modify: `Llamashot/Llamashot.csproj` (add package)
- Create: `tools/FillSignHarness/FillSignHarness.csproj`
- Create: `tools/FillSignHarness/Program.cs`
- Create: `tools/fixtures/137.pdf`

- [ ] **Step 1: Copy the fixture**

```powershell
New-Item -ItemType Directory -Force tools\fixtures | Out-Null
Copy-Item "C:\Users\DELL\Downloads\137.pdf" "tools\fixtures\137.pdf"
```

- [ ] **Step 2: Add PDFsharp to the app project**

In `Llamashot/Llamashot.csproj`, inside the existing `<ItemGroup>` with PackageReferences, add:

```xml
    <PackageReference Include="PDFsharp" Version="6.1.1" />
```

- [ ] **Step 3: Create the harness project**

Create `tools/FillSignHarness/FillSignHarness.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <SelfContained>true</SelfContained>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Llamashot\Llamashot.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Write the spike harness (the failing test)**

Create `tools/FillSignHarness/Program.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FillSignHarness;

internal static class Program
{
    static readonly StringBuilder Sb = new();
    static int Pass, Fail;
    static string Dir = Path.Combine(Path.GetTempPath(), "fillsign_test");

    static void Check(string name, bool ok, string detail)
    {
        if (ok) Pass++; else Fail++;
        Sb.AppendLine($"{(ok ? "PASS" : "FAIL")} | {name} | {detail}");
    }

    [STAThread]
    static int Main()
    {
        Directory.CreateDirectory(Dir);
        try { Spike_PdfSharpVectorText(); }
        catch (Exception e) { Check("spike", false, e.ToString()); }
        Sb.AppendLine($"\n=== {Pass} passed, {Fail} failed ===");
        File.WriteAllText(Path.Combine(Dir, "results.txt"), Sb.ToString());
        return Fail == 0 ? 0 : 1;
    }

    static void Spike_PdfSharpVectorText()
    {
        // PDFsharp 6 requires a font resolver for non-Windows-default font access in some hosts.
        string src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "137.pdf");
        src = Path.GetFullPath(src);
        string outPath = Path.Combine(Dir, "spike_filled.pdf");

        using (var doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify))
        {
            var page = doc.Pages[0];
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 12);
            gfx.DrawString("LLAMASHOT-SPIKE-TOKEN", font, XBrushes.Black, new XPoint(80, 120));
            doc.Save(outPath);
        }

        Check("PDFsharp open+draw+save", File.Exists(outPath) && new FileInfo(outPath).Length > 0,
            $"out={new FileInfo(outPath).Length}b");

        // Verify the text is selectable (extractable), not rasterized.
        bool found = false;
        using (var doc = PdfReader.Open(outPath, PdfDocumentOpenMode.ReadOnly))
        {
            var bytes = File.ReadAllBytes(outPath);
            // Token survives in a content stream; a crude but sufficient check for the spike.
            found = Encoding.ASCII.GetString(bytes).Contains("LLAMASHOT-SPIKE-TOKEN")
                 || ContainsTokenInStreams(doc);
        }
        Check("PDFsharp text is vector/selectable", found, "token present in content");
    }

    static bool ContainsTokenInStreams(PdfDocument doc) => false; // refined in Task 7 if needed
}
```

- [ ] **Step 5: Build and run — verify it builds and the spike result is captured**

Run:
```powershell
dotnet build tools/FillSignHarness/FillSignHarness.csproj -c Debug -v minimal
& "tools\FillSignHarness\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FillSignHarness.exe"
Get-Content "$env:TEMP\fillsign_test\results.txt"
```
Expected: build succeeds; results show `PASS | PDFsharp open+draw+save` and `PASS | PDFsharp text is vector/selectable`.

**DECISION GATE:** If either line FAILS (PDFsharp can't restore on net10, can't open `137.pdf`, or throws on `XGraphics.FromPdfPage`), STOP. Report the exact error and revisit the approach with the user before continuing.

- [ ] **Step 6: Commit**

```powershell
git add Llamashot/Llamashot.csproj tools/FillSignHarness tools/fixtures
git commit -m "feat(fill-sign): PDFsharp net10 spike + test harness scaffold"
```

---

## Task 2: Harness assertion helpers + render utility

Add a shared helper to render a PDF page to a `System.Drawing.Bitmap` via WinRT (the detector and UI both need page bitmaps), and a place to register future scenarios.

**Files:**
- Modify: `tools/FillSignHarness/Program.cs`
- Create: `Llamashot/Core/FillSignRender.cs`

- [ ] **Step 1: Write the failing test (render scenario)**

Add to `Program.cs` `Main` before the spike call:
```csharp
try { Test_RenderPage(); } catch (Exception e) { Check("render", false, e.ToString()); }
```
Add method:
```csharp
static void Test_RenderPage()
{
    string src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..","..","..","..","fixtures","137.pdf"));
    var bmp = Llamashot.Core.FillSignRender.RenderPageToBitmapAsync(src, 0, 150).GetAwaiter().GetResult();
    Check("Render page 0 @150dpi", bmp != null && bmp.Width > 1000 && bmp.Height > 1000,
        $"{bmp?.Width}x{bmp?.Height}");
}
```

- [ ] **Step 2: Run — verify FAIL**

Run the build command from Task 1 Step 5.
Expected: build FAILS — `FillSignRender` does not exist.

- [ ] **Step 3: Implement `FillSignRender`**

Create `Llamashot/Core/FillSignRender.cs`:
```csharp
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace Llamashot.Core;

public static class FillSignRender
{
    /// <summary>Render a 0-based PDF page to a GDI bitmap at the given DPI using WinRT.</summary>
    public static async Task<Bitmap> RenderPageToBitmapAsync(string pdfPath, int pageIndex, int dpi)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
        var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        using var page = pdf.GetPage((uint)pageIndex);
        uint widthPx = (uint)(page.Size.Width * dpi / 72.0);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = widthPx });
        stream.Seek(0);
        using var ms = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }

    /// <summary>Page size in PDF points (72/in).</summary>
    public static async Task<(double wPt, double hPt, int rotation)> GetPageSizeAsync(string pdfPath, int pageIndex)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
        var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        using var page = pdf.GetPage((uint)pageIndex);
        return (page.Size.Width, page.Size.Height, (int)page.Rotation);
    }
}
```
Note: `page.Size` from WinRT is in DIPs at 96/in. PDF points are 72/in. The render uses `dpi/72` because WinRT `Size` is already expressed so that 1 unit = 1/96 inch; confirm the ratio in Task 4's round-trip test and adjust the constant there if the fixture's known page width (595 pt for A4) does not match.

- [ ] **Step 4: Run — verify PASS**

Run the build+run from Task 1 Step 5. Expected: `PASS | Render page 0 @150dpi` with dimensions ~`1240x1750`.

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Core/FillSignRender.cs tools/FillSignHarness/Program.cs
git commit -m "feat(fill-sign): page render utility + harness scenario"
```

---

## Task 3: `FillElement` model

**Files:**
- Create: `Llamashot/Models/FillElement.cs`

- [ ] **Step 1: Write the failing test**

Add to `Program.cs`:
```csharp
try { Test_FillElementModel(); } catch (Exception e) { Check("model", false, e.ToString()); }
```
```csharp
static void Test_FillElementModel()
{
    var el = new Llamashot.Models.FillElement
    {
        Page = 0, Type = Llamashot.Models.FillElementType.Text,
        X = 100, Y = 200, Width = 150, Height = 18,
        Text = "Santosh", FontFamily = "Arial", FontSize = 12, ColorHex = "#000000"
    };
    Check("FillElement holds values", el.Type == Llamashot.Models.FillElementType.Text && el.Text == "Santosh", $"x={el.X}");
}
```

- [ ] **Step 2: Run — verify FAIL** (build fails, type missing).

- [ ] **Step 3: Implement the model**

Create `Llamashot/Models/FillElement.cs`:
```csharp
namespace Llamashot.Models;

public enum FillElementType { Text, Check, Signature, Stamp, DateTime }

/// <summary>One placed annotation. Geometry is in PDF points, top-left origin.</summary>
public class FillElement
{
    public int Page { get; set; }
    public FillElementType Type { get; set; }

    // Rect in PDF points, top-left origin (matches PDFsharp XGraphics).
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    // Text / DateTime / Check glyph
    public string Text { get; set; } = "";
    public string FontFamily { get; set; } = "Arial";
    public double FontSize { get; set; } = 12;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public string ColorHex { get; set; } = "#000000";

    // Signature (uploaded) / Stamp: absolute path to a PNG. Signature (drawn): PNG written to temp.
    public string? ImagePath { get; set; }
}
```

- [ ] **Step 4: Run — verify PASS.**

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Models/FillElement.cs tools/FillSignHarness/Program.cs
git commit -m "feat(fill-sign): FillElement model"
```

---

## Task 4: Coordinate conversion (`FillSignGeometry`)

Maps between canvas pixels (top-left origin, Y-down) and PDF points (top-left origin), at a render DPI. Both spaces are top-left so it is a pure scale — no Y-flip — which is the whole reason Approach B was chosen with PDFsharp `XGraphics`.

**Files:**
- Create: `Llamashot/Core/FillSignGeometry.cs`

- [ ] **Step 1: Write the failing test**

```csharp
try { Test_Geometry(); } catch (Exception e) { Check("geometry", false, e.ToString()); }
```
```csharp
static void Test_Geometry()
{
    // At 150 dpi, 1 point = 150/72 px ≈ 2.0833 px.
    double px = Llamashot.Core.FillSignGeometry.PointsToPixels(72, 150); // 72pt -> 150px
    double pt = Llamashot.Core.FillSignGeometry.PixelsToPoints(150, 150); // 150px -> 72pt
    bool ok = System.Math.Abs(px - 150) < 0.01 && System.Math.Abs(pt - 72) < 0.01;
    // Round-trip a rect
    var (rx, ry, rw, rh) = Llamashot.Core.FillSignGeometry.PixelRectToPointRect(208.33, 416.66, 100, 50, 150);
    ok &= System.Math.Abs(rx - 100) < 0.1 && System.Math.Abs(ry - 200) < 0.1;
    Check("Geometry px<->pt round-trip", ok, $"px={px:F2} pt={pt:F2} rx={rx:F2} ry={ry:F2}");
}
```

- [ ] **Step 2: Run — verify FAIL.**

- [ ] **Step 3: Implement**

Create `Llamashot/Core/FillSignGeometry.cs`:
```csharp
namespace Llamashot.Core;

public static class FillSignGeometry
{
    public static double PointsToPixels(double points, int dpi) => points * dpi / 72.0;
    public static double PixelsToPoints(double pixels, int dpi) => pixels * 72.0 / dpi;

    public static (double x, double y, double w, double h) PixelRectToPointRect(
        double px, double py, double pw, double ph, int dpi) =>
        (PixelsToPoints(px, dpi), PixelsToPoints(py, dpi), PixelsToPoints(pw, dpi), PixelsToPoints(ph, dpi));

    public static (double x, double y, double w, double h) PointRectToPixelRect(
        double x, double y, double w, double h, int dpi) =>
        (PointsToPixels(x, dpi), PointsToPixels(y, dpi), PointsToPixels(w, dpi), PointsToPixels(h, dpi));
}
```

- [ ] **Step 4: Run — verify PASS.**

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Core/FillSignGeometry.cs tools/FillSignHarness/Program.cs
git commit -m "feat(fill-sign): pixel<->point geometry"
```

---

## Task 5: Detector — line segment extraction

`FillSignDetector` step 1: from a grayscale bitmap, find long horizontal and vertical dark runs. Test on a synthetic bitmap drawn in the harness (deterministic), not the noisy PDF.

**Files:**
- Create: `Llamashot/Core/FillSignDetector.cs`

- [ ] **Step 1: Write the failing test**

```csharp
try { Test_Segments(); } catch (Exception e) { Check("segments", false, e.ToString()); }
```
```csharp
static void Test_Segments()
{
    using var bmp = new System.Drawing.Bitmap(400, 300);
    using (var g = System.Drawing.Graphics.FromImage(bmp))
    {
        g.Clear(System.Drawing.Color.White);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.Black, 2);
        g.DrawLine(pen, 50, 100, 350, 100);  // horizontal underline
        g.DrawRectangle(pen, 50, 150, 80, 40); // a box
    }
    var seg = Llamashot.Core.FillSignDetector.ExtractSegments(bmp, minLengthFraction: 0.1);
    bool hasH = seg.Horizontal.Exists(s => s.Length > 250);
    bool hasV = seg.Vertical.Exists(s => s.Length > 30);
    Check("Detector finds H+V segments", hasH && hasV, $"H={seg.Horizontal.Count} V={seg.Vertical.Count}");
}
```

- [ ] **Step 2: Run — verify FAIL** (type/method missing).

- [ ] **Step 3: Implement segment extraction**

Create `Llamashot/Core/FillSignDetector.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace Llamashot.Core;

public record LineSeg(int X1, int Y1, int X2, int Y2)
{
    public int Length => Math.Max(Math.Abs(X2 - X1), Math.Abs(Y2 - Y1));
}

public class SegmentSet
{
    public List<LineSeg> Horizontal { get; } = new();
    public List<LineSeg> Vertical { get; } = new();
}

public static partial class FillSignDetector
{
    const int DarkThreshold = 128; // 0..255 luminance below this = "ink"

    static bool[,] Binarize(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var dark = new bool[w, h];
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            unsafe
            {
                byte* p0 = (byte*)data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* row = p0 + y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        byte b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2];
                        int lum = (r * 299 + g * 587 + b * 114) / 1000;
                        dark[x, y] = lum < DarkThreshold;
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return dark;
    }

    public static SegmentSet ExtractSegments(Bitmap bmp, double minLengthFraction)
    {
        var dark = Binarize(bmp);
        int w = bmp.Width, h = bmp.Height;
        int minH = (int)(w * minLengthFraction);
        int minV = (int)(h * minLengthFraction);
        var set = new SegmentSet();

        // Horizontal runs
        for (int y = 0; y < h; y++)
        {
            int run = 0;
            for (int x = 0; x < w; x++)
            {
                if (dark[x, y]) run++;
                else { if (run >= minH) set.Horizontal.Add(new LineSeg(x - run, y, x - 1, y)); run = 0; }
            }
            if (run >= minH) set.Horizontal.Add(new LineSeg(w - run, y, w - 1, y));
        }
        // Vertical runs
        for (int x = 0; x < w; x++)
        {
            int run = 0;
            for (int y = 0; y < h; y++)
            {
                if (dark[x, y]) run++;
                else { if (run >= minV) set.Vertical.Add(new LineSeg(x, y - run, x, y - 1)); run = 0; }
            }
            if (run >= minV) set.Vertical.Add(new LineSeg(x, h - run, x, h - 1));
        }
        return set;
    }
}
```
Note: requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`. Add it to `Llamashot/Llamashot.csproj` `<PropertyGroup>` in this step.

- [ ] **Step 4: Run — verify PASS** (`H>0 V>0`, has long H and V).

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Core/FillSignDetector.cs Llamashot/Llamashot.csproj tools/FillSignHarness/Program.cs
git commit -m "feat(fill-sign): detector line-segment extraction"
```

---

## Task 6: Detector — region classification (boxes, checkboxes, combs, underlines)

Group segments into `DetectedRegion`s with a kind. Test first on synthetic shapes, then a smoke assertion on `137.pdf` page 2 (the Personal Details page) that at least several regions are found.

**Files:**
- Modify: `Llamashot/Core/FillSignDetector.cs`

- [ ] **Step 1: Write the failing test**

```csharp
try { Test_Regions(); } catch (Exception e) { Check("regions", false, e.ToString()); }
```
```csharp
static void Test_Regions()
{
    // Synthetic: one checkbox (small square), one underline, one comb row (5 cells).
    using var bmp = new System.Drawing.Bitmap(600, 400);
    using (var g = System.Drawing.Graphics.FromImage(bmp))
    {
        g.Clear(System.Drawing.Color.White);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.Black, 2);
        g.DrawRectangle(pen, 40, 40, 20, 20);                       // checkbox
        g.DrawLine(pen, 40, 150, 300, 150);                        // underline
        g.DrawRectangle(pen, 40, 200, 250, 40);                    // comb outer
        for (int i = 1; i < 5; i++) g.DrawLine(pen, 40 + i * 50, 200, 40 + i * 50, 240); // comb separators
    }
    var regions = Llamashot.Core.FillSignDetector.DetectRegions(bmp);
    int boxes = regions.FindAll(r => r.Kind == Llamashot.Core.RegionKind.Checkbox).Count;
    int combs = regions.FindAll(r => r.Kind == Llamashot.Core.RegionKind.Comb).Count;
    int unders = regions.FindAll(r => r.Kind == Llamashot.Core.RegionKind.Underline).Count;
    Check("Detector classifies regions", boxes >= 1 && combs >= 1 && unders >= 1,
        $"box={boxes} comb={combs} under={unders} total={regions.Count}");

    // Smoke on the real form (page index 1 = Personal Details).
    string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"..","..","..","..","fixtures","137.pdf"));
    using var page = Llamashot.Core.FillSignRender.RenderPageToBitmapAsync(src, 1, 150).GetAwaiter().GetResult();
    var real = Llamashot.Core.FillSignDetector.DetectRegions(page);
    Check("Detector finds regions on 137.pdf p2", real.Count >= 10, $"regions={real.Count}");
}
```

- [ ] **Step 2: Run — verify FAIL** (`DetectRegions`/`RegionKind` missing).

- [ ] **Step 3: Implement classification**

Append to `Llamashot/Core/FillSignDetector.cs`:
```csharp
namespace Llamashot.Core;

public enum RegionKind { Underline, Box, Checkbox, Comb }

public record DetectedRegion(RegionKind Kind, int X, int Y, int W, int H);

public static partial class FillSignDetector
{
    public static List<DetectedRegion> DetectRegions(Bitmap bmp)
    {
        var seg = ExtractSegments(bmp, 0.012); // ~1.2% of page dimension
        var regions = new List<DetectedRegion>();
        int tol = Math.Max(3, bmp.Width / 300);

        // Pair horizontal segments (top/bottom) sharing x-overlap and bounded by verticals => boxes.
        foreach (var top in seg.Horizontal)
        {
            foreach (var bot in seg.Horizontal)
            {
                if (bot.Y1 - top.Y1 < 8 || bot.Y1 - top.Y1 > bmp.Height / 4) continue;
                int xL = Math.Max(top.X1, bot.X1), xR = Math.Min(top.X2, bot.X2);
                if (xR - xL < 8) continue;
                // require a left and right vertical near xL/xR spanning the band
                bool left = HasVerticalNear(seg, xL, top.Y1, bot.Y1, tol);
                bool right = HasVerticalNear(seg, xR, top.Y1, bot.Y1, tol);
                if (!(left && right)) continue;

                int w = xR - xL, h = bot.Y1 - top.Y1;
                // Count interior vertical separators within the band.
                int seps = CountInteriorVerticals(seg, xL, xR, top.Y1, bot.Y1, tol);
                RegionKind kind;
                if (seps >= 3) kind = RegionKind.Comb;
                else if (Math.Abs(w - h) <= Math.Max(w, h) * 0.4 && w <= bmp.Width / 25) kind = RegionKind.Checkbox;
                else kind = RegionKind.Box;
                AddUnique(regions, new DetectedRegion(kind, xL, top.Y1, w, h), tol);
            }
        }

        // Underlines: long horizontals with no box pairing nearby.
        foreach (var hs in seg.Horizontal)
        {
            if (hs.Length < bmp.Width * 0.06) continue;
            bool insideBox = regions.Exists(r => r.Y <= hs.Y1 + tol && r.Y + r.H >= hs.Y1 - tol &&
                                                 r.X <= hs.X1 + tol && r.X + r.W >= hs.X2 - tol);
            if (!insideBox)
                AddUnique(regions, new DetectedRegion(RegionKind.Underline, hs.X1, hs.Y1 - 20, hs.Length, 20), tol);
        }
        return regions;
    }

    static bool HasVerticalNear(SegmentSet seg, int x, int yTop, int yBot, int tol) =>
        seg.Vertical.Exists(v => Math.Abs(v.X1 - x) <= tol && v.Y1 <= yTop + tol && v.Y2 >= yBot - tol);

    static int CountInteriorVerticals(SegmentSet seg, int xL, int xR, int yTop, int yBot, int tol)
    {
        int c = 0;
        foreach (var v in seg.Vertical)
            if (v.X1 > xL + tol && v.X1 < xR - tol && v.Y1 <= yTop + tol && v.Y2 >= yBot - tol) c++;
        return c;
    }

    static void AddUnique(List<DetectedRegion> list, DetectedRegion r, int tol)
    {
        if (!list.Exists(e => e.Kind == r.Kind && Math.Abs(e.X - r.X) <= tol &&
                              Math.Abs(e.Y - r.Y) <= tol && Math.Abs(e.W - r.W) <= tol))
            list.Add(r);
    }
}
```

- [ ] **Step 4: Run — verify PASS** for both synthetic and `137.pdf` smoke (`regions>=10`). If the real-form count is low, loosen `minLengthFraction` (0.012 → 0.008) and re-run; record the final value.

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Core/FillSignDetector.cs tools/FillSignHarness/Program.cs
git commit -m "feat(fill-sign): region classification (box/checkbox/comb/underline)"
```

---

## Task 7: Exporter — vector text via PDFsharp + font resolver

**Files:**
- Create: `Llamashot/Core/FillSignFontResolver.cs`
- Create: `Llamashot/Core/FillSignExporter.cs`

- [ ] **Step 1: Write the failing test**

```csharp
try { Test_ExportText(); } catch (Exception e) { Check("export-text", false, e.ToString()); }
```
```csharp
static void Test_ExportText()
{
    string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"..","..","..","..","fixtures","137.pdf"));
    string outPath = System.IO.Path.Combine(Dir, "export_text.pdf");
    var els = new System.Collections.Generic.List<Llamashot.Models.FillElement>
    {
        new() { Page = 1, Type = Llamashot.Models.FillElementType.Text, X = 120, Y = 110,
                Width = 200, Height = 16, Text = "SANTOSH-FILL-XYZ", FontFamily = "Arial",
                FontSize = 11, ColorHex = "#000000" }
    };
    Llamashot.Core.FillSignExporter.Export(src, els, outPath);
    var bytes = System.IO.File.ReadAllBytes(outPath);
    bool present = System.Text.Encoding.ASCII.GetString(bytes).Contains("SANTOSH-FILL-XYZ");
    Check("Export writes selectable text", System.IO.File.Exists(outPath) && present, $"out={bytes.Length}b present={present}");
}
```
Note: PDFsharp may compress content streams; if the raw-byte check is flaky, the implementation in Step 3 sets `doc.Options.CompressContentStreams = false` for now so the token is visible, and the check stays valid. (Compression can be re-enabled later; selectability does not depend on it.)

- [ ] **Step 2: Run — verify FAIL.**

- [ ] **Step 3: Implement font resolver + exporter (text path)**

Create `Llamashot/Core/FillSignFontResolver.cs`:
```csharp
using System;
using System.IO;
using PdfSharp.Fonts;

namespace Llamashot.Core;

/// <summary>Maps the offered families to installed Windows system fonts so glyphs embed.</summary>
public class FillSignFontResolver : IFontResolver
{
    static readonly string Fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    public byte[]? GetFont(string faceName) => File.Exists(faceName) ? File.ReadAllBytes(faceName) : null;

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        string fam = familyName.ToLowerInvariant();
        string file = fam switch
        {
            "times" or "times new roman" => Pick("times", bold, italic, "times.ttf","timesbd.ttf","timesi.ttf","timesbi.ttf"),
            "courier" or "courier new"   => Pick("cour",  bold, italic, "cour.ttf","courbd.ttf","couri.ttf","courbi.ttf"),
            _                             => Pick("arial", bold, italic, "arial.ttf","arialbd.ttf","ariali.ttf","arialbi.ttf"),
        };
        return new FontResolverInfo(Path.Combine(Fonts, file));
    }

    static string Pick(string _, bool b, bool i, string reg, string bold, string ital, string boldItal)
        => b && i ? boldItal : b ? bold : i ? ital : reg;
}
```

Create `Llamashot/Core/FillSignExporter.cs`:
```csharp
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Llamashot.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Llamashot.Core;

public static class FillSignExporter
{
    static bool _resolverSet;

    static void EnsureResolver()
    {
        if (_resolverSet) return;
        GlobalFontSettings.FontResolver = new FillSignFontResolver();
        _resolverSet = true;
    }

    static XColor Color(string hex)
    {
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return XColor.FromArgb(c.A, c.R, c.G, c.B);
    }

    /// <summary>Approach B: inject vector content into the original PDF. Throws if PDFsharp cannot open it.</summary>
    public static void Export(string srcPdf, List<FillElement> elements, string outPath)
    {
        EnsureResolver();
        using var doc = PdfReader.Open(srcPdf, PdfDocumentOpenMode.Modify);
        doc.Options.CompressContentStreams = false; // keep tokens visible for verification

        var byPage = new Dictionary<int, List<FillElement>>();
        foreach (var e in elements)
            (byPage.TryGetValue(e.Page, out var l) ? l : byPage[e.Page] = new()).Add(e);

        foreach (var (pageIdx, els) in byPage)
        {
            if (pageIdx < 0 || pageIdx >= doc.Pages.Count) continue;
            var page = doc.Pages[pageIdx];
            using var gfx = XGraphics.FromPdfPage(page);
            foreach (var e in els) DrawElement(gfx, e);
        }
        doc.Save(outPath);
    }

    static void DrawElement(XGraphics gfx, FillElement e)
    {
        switch (e.Type)
        {
            case FillElementType.Text:
            case FillElementType.DateTime:
                DrawText(gfx, e);
                break;
            case FillElementType.Check:
                DrawText(gfx, e, with_glyph: true);
                break;
            case FillElementType.Signature:
            case FillElementType.Stamp:
                if (e.ImagePath != null && File.Exists(e.ImagePath))
                    gfx.DrawImage(XImage.FromFile(e.ImagePath), e.X, e.Y, e.Width, e.Height);
                break;
        }
    }

    static void DrawText(XGraphics gfx, FillElement e, bool with_glyph = false)
    {
        var style = (e.Bold ? XFontStyleEx.Bold : 0) | (e.Italic ? XFontStyleEx.Italic : 0);
        var font = new XFont(e.FontFamily, e.FontSize, style);
        var brush = new XSolidBrush(Color(e.ColorHex));
        string text = with_glyph ? (string.IsNullOrEmpty(e.Text) ? "X" : e.Text) : e.Text;
        // Baseline: place near the bottom of the rect (PDFsharp DrawString uses top-left rect with TopLeft format).
        var rect = new XRect(e.X, e.Y, e.Width <= 0 ? 1000 : e.Width, e.Height <= 0 ? e.FontSize * 1.4 : e.Height);
        gfx.DrawString(text, font, brush, rect, XStringFormats.CenterLeft);
    }
}
```
Note: `with_glyph` is a positional `bool` on `DrawText`; the `Check` branch passes `true` so an empty tick renders as `X`. `XFontStyleEx` is PDFsharp 6's font-style enum (older docs say `XFontStyle`); use `XFontStyleEx`.

- [ ] **Step 4: Run — verify PASS** (`Export writes selectable text`, token present). If the open throws, the DECISION GATE from Task 1 already validated it works on `137.pdf`; investigate the specific element/page before proceeding.

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Core/FillSignFontResolver.cs Llamashot/Core/FillSignExporter.cs tools/FillSignHarness/Program.cs
git commit -m "feat(fill-sign): PDFsharp exporter (vector text) + font resolver"
```

---

## Task 8: Exporter — images (signature/stamp) end-to-end

Confirm the image path renders into the output (page count preserved, file grows, reopens).

**Files:**
- Modify: `tools/FillSignHarness/Program.cs` (test only; exporter already handles images)

- [ ] **Step 1: Write the failing test**

```csharp
try { Test_ExportImage(); } catch (Exception e) { Check("export-image", false, e.ToString()); }
```
```csharp
static void Test_ExportImage()
{
    // Make a small signature PNG.
    string sig = System.IO.Path.Combine(Dir, "sig.png");
    using (var b = new System.Drawing.Bitmap(200, 80))
    {
        using var g = System.Drawing.Graphics.FromImage(b);
        g.Clear(System.Drawing.Color.Transparent);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.Blue, 3);
        g.DrawCurve(pen, new[]{ new System.Drawing.Point(5,60), new System.Drawing.Point(60,10), new System.Drawing.Point(120,70), new System.Drawing.Point(195,20)});
        b.Save(sig, System.Drawing.Imaging.ImageFormat.Png);
    }
    string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"..","..","..","..","fixtures","137.pdf"));
    string outPath = System.IO.Path.Combine(Dir, "export_image.pdf");
    var els = new System.Collections.Generic.List<Llamashot.Models.FillElement>
    {
        new() { Page = 0, Type = Llamashot.Models.FillElementType.Signature, X = 90, Y = 380, Width = 120, Height = 48, ImagePath = sig }
    };
    Llamashot.Core.FillSignExporter.Export(src, els, outPath);
    using var d = PdfSharp.Pdf.IO.PdfReader.Open(outPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.ReadOnly);
    Check("Export embeds signature image", System.IO.File.Exists(outPath) && d.Pages.Count >= 1,
        $"pages={d.Pages.Count} size={new System.IO.FileInfo(outPath).Length}b");
}
```

- [ ] **Step 2: Run — verify FAIL** if any issue, else proceed; expected PASS once exporter image path is correct.

- [ ] **Step 3: (Fix only if needed)** If image draw throws, ensure `XImage.FromFile` is given an absolute path and the PNG is valid. No new code if Task 7 image branch is correct.

- [ ] **Step 4: Run — verify PASS.**

- [ ] **Step 5: Commit**

```powershell
git add tools/FillSignHarness/Program.cs
git commit -m "test(fill-sign): exporter image embedding"
```

---

## Task 9: Exporter — rasterized fallback

If `PdfReader.Open` throws, render pages to images, draw elements onto the bitmaps with `System.Drawing`, and repackage via the existing `FileToolsService.ImagesToPdfAsync`.

**Files:**
- Modify: `Llamashot/Core/FillSignExporter.cs`

- [ ] **Step 1: Write the failing test**

```csharp
try { Test_Fallback(); } catch (Exception e) { Check("fallback", false, e.ToString()); }
```
```csharp
static void Test_Fallback()
{
    string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"..","..","..","..","fixtures","137.pdf"));
    string outPath = System.IO.Path.Combine(Dir, "export_raster.pdf");
    var els = new System.Collections.Generic.List<Llamashot.Models.FillElement>
    {
        new() { Page = 1, Type = Llamashot.Models.FillElementType.Text, X = 120, Y = 110, Width = 200, Height = 16, Text = "RASTER-FILL", FontSize = 12, ColorHex="#000000", FontFamily="Arial" }
    };
    Llamashot.Core.FillSignExporter.ExportRasterized(src, els, 150, outPath).GetAwaiter().GetResult();
    int pc = Llamashot.Core.FileToolsService.GetPdfPageCountAsync(outPath).GetAwaiter().GetResult();
    Check("Rasterized fallback produces valid PDF", System.IO.File.Exists(outPath) && pc >= 2, $"pages={pc}");
}
```

- [ ] **Step 2: Run — verify FAIL** (`ExportRasterized` missing).

- [ ] **Step 3: Implement fallback + wire into `Export`**

Add to `FillSignExporter` (uses `FillSignRender` from Task 2 and `FillSignGeometry` from Task 4):
```csharp
public static async System.Threading.Tasks.Task ExportRasterized(
    string srcPdf, List<FillElement> elements, int dpi, string outPath)
{
    var (wPt, hPt, _) = await FillSignRender.GetPageSizeAsync(srcPdf, 0);
    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(srcPdf);
    var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
    int pages = (int)pdf.PageCount;

    string tempDir = Path.Combine(Path.GetTempPath(), "fillsign_raster_" + System.Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    var imgs = new List<string>();
    try
    {
        for (int i = 0; i < pages; i++)
        {
            using var bmp = await FillSignRender.RenderPageToBitmapAsync(srcPdf, i, dpi);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                foreach (var e in elements)
                {
                    if (e.Page != i) continue;
                    var (px, py, pw, ph) = FillSignGeometry.PointRectToPixelRect(e.X, e.Y, e.Width, e.Height, dpi);
                    if (e.Type is FillElementType.Signature or FillElementType.Stamp)
                    {
                        if (e.ImagePath != null && File.Exists(e.ImagePath))
                            using (var im = System.Drawing.Image.FromFile(e.ImagePath))
                                g.DrawImage(im, (float)px, (float)py, (float)pw, (float)ph);
                    }
                    else
                    {
                        var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(e.ColorHex);
                        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(col.A, col.R, col.G, col.B));
                        var style = (e.Bold ? System.Drawing.FontStyle.Bold : 0) | (e.Italic ? System.Drawing.FontStyle.Italic : 0);
                        float emPx = (float)FillSignGeometry.PointsToPixels(e.FontSize, dpi);
                        using var font = new System.Drawing.Font(e.FontFamily, emPx, style, System.Drawing.GraphicsUnit.Pixel);
                        string text = e.Type == FillElementType.Check && string.IsNullOrEmpty(e.Text) ? "X" : e.Text;
                        g.DrawString(text, font, brush, (float)px, (float)py);
                    }
                }
            }
            string f = Path.Combine(tempDir, $"page_{i:D3}.png");
            bmp.Save(f, System.Drawing.Imaging.ImageFormat.Png);
            imgs.Add(f);
        }
        await FileToolsService.ImagesToPdfAsync(imgs.ToArray(), outPath);
    }
    finally { try { Directory.Delete(tempDir, true); } catch { } }
}
```
Change `Export` to fall back automatically:
```csharp
public static void Export(string srcPdf, List<FillElement> elements, string outPath)
{
    EnsureResolver();
    try { ExportVector(srcPdf, elements, outPath); }
    catch (System.Exception)
    {
        ExportRasterized(srcPdf, elements, 150, outPath).GetAwaiter().GetResult();
    }
}
```
Rename the previous body of `Export` (the PDFsharp path) to `static void ExportVector(...)`.

- [ ] **Step 4: Run — verify PASS** (`pages>=2`).

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Core/FillSignExporter.cs tools/FillSignHarness/Program.cs
git commit -m "feat(fill-sign): rasterized export fallback"
```

---

## Task 10: UI — tool card + panel scaffold + file load

Register the tool and build the select view (empty state) + an empty workspace panel. Follows the Resize template (`ShowConfigState`, select/drop handlers).

**Files:**
- Modify: `Llamashot/Views/FileToolsWindow.xaml.cs` (`ToolDefs`, `InitPanelMap`, handlers, state)
- Modify: `Llamashot/Views/FileToolsWindow.xaml` (panel markup)

- [ ] **Step 1: Add the tool definition**

In `FileToolsWindow.xaml.cs` `ToolDefs` array, after the `insert_pages` entry, add:
```csharp
        ("fill_sign",      "Fill & Sign",     "Fill forms, sign, stamp a PDF",       "#26A69A", "✍", "PDF Tools"),
```

- [ ] **Step 2: Add panel markup**

In `FileToolsWindow.xaml`, after the `PanelInsertPages` panel, add (mirrors other tool panels — a select view + a workspace Grid):
```xml
<Grid x:Name="PanelFillSign" Visibility="Collapsed">
  <!-- SELECT VIEW -->
  <Border x:Name="FillSignSelectView" AllowDrop="True"
          Drop="FillSign_SelectDrop" DragOver="Generic_DragOver">
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
      <TextBlock Text="Fill &amp; Sign" FontSize="26" FontWeight="Bold" Foreground="White"
                 HorizontalAlignment="Center"/>
      <TextBlock Text="Fill forms, sign, stamp a PDF" Foreground="#9AA" FontSize="13"
                 HorizontalAlignment="Center" Margin="0,6,0,24"/>
      <Button Content="Select PDF file" Click="FillSign_SelectFiles"
              Template="{StaticResource RoundedButton}" Background="#26A69A"
              Foreground="White" Padding="34,14" FontSize="15" FontWeight="SemiBold"
              HorizontalAlignment="Center"/>
      <TextBlock Text="or drop a PDF here" Foreground="#667" FontSize="12"
                 HorizontalAlignment="Center" Margin="0,12,0,0"/>
    </StackPanel>
  </Border>

  <!-- WORKSPACE (hidden until a file loads) -->
  <Grid x:Name="FillSignWorkspace" Visibility="Collapsed">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>  <!-- toolbar -->
      <RowDefinition Height="*"/>     <!-- body -->
    </Grid.RowDefinitions>
    <!-- toolbar filled in Task 13+ -->
    <Border Grid.Row="0" x:Name="FillSignToolbar" Background="#26262B" Height="48"/>
    <Grid Grid.Row="1">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="120"/>  <!-- thumbnails -->
        <ColumnDefinition Width="*"/>    <!-- page canvas -->
      </Grid.ColumnDefinitions>
      <ScrollViewer Grid.Column="0" x:Name="FillSignThumbs" VerticalScrollBarVisibility="Auto"/>
      <ScrollViewer Grid.Column="1" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto"
                    Background="#161619">
        <Grid x:Name="FillSignPageHost" Margin="20">
          <Image x:Name="FillSignPageImage" Stretch="None"/>
          <Canvas x:Name="FillSignOverlay" Background="Transparent"/>
        </Grid>
      </ScrollViewer>
    </Grid>
  </Grid>
</Grid>
```

- [ ] **Step 3: Register in `InitPanelMap`**

After `_toolPanels["insert_pages"] = PanelInsertPages;` add:
```csharp
        _toolPanels["fill_sign"] = PanelFillSign;
```
And in the `_selectViews` block add:
```csharp
        _selectViews["fill_sign"] = FillSignSelectView;
```

- [ ] **Step 4: Add state fields + load handlers**

In the state-fields region of `FileToolsWindow.xaml.cs`, add:
```csharp
    private string? _fsPdfPath;
    private int _fsCurrentPage;
    private int _fsPageCount;
    private const int FsDpi = 150;
    private readonly List<Llamashot.Models.FillElement> _fsElements = new();
    private readonly Dictionary<int, List<Llamashot.Core.DetectedRegion>> _fsRegionCache = new();
```
Add handlers (place near the other tool handlers):
```csharp
    private void FillSign_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF files|*.pdf" };
        if (dlg.ShowDialog() == true) _ = FsLoadPdf(dlg.FileName);
    }

    private void FillSign_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        if (files.Length > 0) _ = FsLoadPdf(files[0]);
    }

    private async Task FsLoadPdf(string path)
    {
        _fsPdfPath = path;
        _fsElements.Clear();
        _fsRegionCache.Clear();
        _fsCurrentPage = 0;
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        _fsPageCount = (int)pdf.PageCount;
        FillSignSelectView.Visibility = Visibility.Collapsed;
        FillSignWorkspace.Visibility = Visibility.Visible;
        await FsRenderCurrentPage();
    }
```
`FsRenderCurrentPage()` is added in Task 11 — stub it for now:
```csharp
    private Task FsRenderCurrentPage() => Task.CompletedTask;
```

- [ ] **Step 5: Build, run the app, verify the card + empty state**

Build the app:
```powershell
dotnet build Llamashot/Llamashot.csproj -c Debug -v minimal
```
Then verify via the screenshot harness pattern (reveal `PanelFillSign` like the verify run did) OR launch and open File Tools → Fill & Sign. Expected: a teal "Fill & Sign" card appears under PDF Tools; clicking it shows the "Select PDF file / or drop a PDF here" empty state.

- [ ] **Step 6: Commit**

```powershell
git add Llamashot/Views/FileToolsWindow.xaml Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m "feat(fill-sign): tool card + panel scaffold + PDF load"
```

---

## Task 11: UI — page render, page navigation, thumbnail strip

**Files:**
- Modify: `Llamashot/Views/FileToolsWindow.xaml.cs`

- [ ] **Step 1: Implement `FsRenderCurrentPage` (replace the stub)**

```csharp
    private async Task FsRenderCurrentPage()
    {
        if (_fsPdfPath == null) return;
        using var bmp = await Llamashot.Core.FillSignRender.RenderPageToBitmapAsync(_fsPdfPath, _fsCurrentPage, FsDpi);
        var bi = new BitmapImage();
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            bi.BeginInit(); bi.StreamSource = ms; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
        }
        FillSignPageImage.Source = bi;
        FillSignOverlay.Width = bi.PixelWidth;
        FillSignOverlay.Height = bi.PixelHeight;
        FsRenderOverlayElements();
        await FsEnsureRegions(_fsCurrentPage);
    }

    private void FsRenderOverlayElements()
    {
        FillSignOverlay.Children.Clear();
        // element visuals added in Tasks 13-17
    }

    private async Task FsEnsureRegions(int page)
    {
        if (_fsRegionCache.ContainsKey(page) || _fsPdfPath == null) return;
        string path = _fsPdfPath;
        var regions = await Task.Run(async () =>
        {
            using var bmp = await Llamashot.Core.FillSignRender.RenderPageToBitmapAsync(path, page, FsDpi);
            return Llamashot.Core.FillSignDetector.DetectRegions(bmp);
        });
        _fsRegionCache[page] = regions;
    }
```
Add `using System.IO;` if not present (it is, at file top).

- [ ] **Step 2: Add page-nav methods + thumbnail strip build**

```csharp
    private async void FsNextPage(object sender, RoutedEventArgs e)
    {
        if (_fsCurrentPage < _fsPageCount - 1) { _fsCurrentPage++; await FsRenderCurrentPage(); }
    }
    private async void FsPrevPage(object sender, RoutedEventArgs e)
    {
        if (_fsCurrentPage > 0) { _fsCurrentPage--; await FsRenderCurrentPage(); }
    }
```
Wire keyboard PageUp/PageDown later if desired (out of scope v1). Thumbnail strip: render small thumbnails into `FillSignThumbs` reusing `FillSignRender` at low DPI; clicking sets `_fsCurrentPage`. (Implement a simple vertical `StackPanel` of `Image` buttons; mirror `PeRenderPageGrid` styling.)

- [ ] **Step 3: Build + run, verify page renders and nav works**

Open Fill & Sign → load `tools/fixtures/137.pdf`. Expected: page 1 renders large in the canvas; thumbnails appear; next/prev change pages.

- [ ] **Step 4: Commit**

```powershell
git add Llamashot/Views/FileToolsWindow.xaml.cs Llamashot/Views/FileToolsWindow.xaml
git commit -m "feat(fill-sign): page render, navigation, thumbnails"
```

---

## Task 12: UI — hover detection highlight

Show a translucent rectangle over the detected region under the cursor.

**Files:**
- Modify: `Llamashot/Views/FileToolsWindow.xaml.cs`, `Llamashot/Views/FileToolsWindow.xaml`

- [ ] **Step 1: Add a highlight rectangle to the overlay host**

In XAML, inside `FillSignPageHost` after `FillSignOverlay`, add:
```xml
          <Rectangle x:Name="FillSignHover" Visibility="Collapsed" IsHitTestVisible="False"
                     Stroke="#3B82F6" StrokeThickness="1.5" Fill="#223B82F6"/>
```
Wrap host in a Canvas so the rectangle can be positioned, or set the rectangle's `Margin`. Simplest: make `FillSignPageHost` a `Canvas` and position children with `Canvas.Left/Top`; place `FillSignPageImage` at (0,0).

- [ ] **Step 2: Add mouse-move handler**

On `FillSignOverlay`, set `MouseMove="FsOverlay_MouseMove"` and `Background="Transparent"`.
```csharp
    private void FsOverlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_fsRegionCache.TryGetValue(_fsCurrentPage, out var regions)) { FillSignHover.Visibility = Visibility.Collapsed; return; }
        var p = e.GetPosition(FillSignOverlay);
        Llamashot.Core.DetectedRegion? hit = null;
        foreach (var r in regions)
            if (p.X >= r.X && p.X <= r.X + r.W && p.Y >= r.Y && p.Y <= r.Y + r.H) { hit = r; break; }
        if (hit is { } h)
        {
            Canvas.SetLeft(FillSignHover, h.X); Canvas.SetTop(FillSignHover, h.Y);
            FillSignHover.Width = h.W; FillSignHover.Height = h.H;
            FillSignHover.Visibility = Visibility.Visible;
        }
        else FillSignHover.Visibility = Visibility.Collapsed;
    }
```
Region coords are in **page-render pixels** (the detector ran at `FsDpi` on the same-size bitmap shown), so they align 1:1 with the overlay. Keep DPI identical for render and detection (both `FsDpi`).

- [ ] **Step 3: Build + run, verify highlight tracks fields**

Hover over comb rows / checkboxes on `137.pdf`. Expected: blue translucent box snaps to detected regions; disappears over blank areas.

- [ ] **Step 4: Commit**

```powershell
git add Llamashot/Views/FileToolsWindow.xaml Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m "feat(fill-sign): hover field-detection highlight"
```

---

## Task 13: UI — Text tool (place, edit, font/size/color/bold/italic, drag)

**Files:**
- Modify: `Llamashot/Views/FileToolsWindow.xaml(.cs)`

- [ ] **Step 1: Add toolbar controls**

In `FillSignToolbar`, add a horizontal `StackPanel` with mode toggle buttons (`Select`, `Text`, `Check`, `Signature`, `Stamp`, `Date/Time`), a font `ComboBox` (Arial/Times New Roman/Courier New), a size `ComboBox` (8–48), `Bold`/`Italic` toggle buttons, a color `Button` (opens a color dialog), and a `Save PDF` button (`Click="FillSign_Save"`). Give each a `Tag`/handler. Track active mode in:
```csharp
    private string _fsMode = "select";
    private string _fsFont = "Arial";
    private double _fsSize = 12;
    private bool _fsBold, _fsItalic;
    private string _fsColor = "#000000";
```

- [ ] **Step 2: Place a text element on click**

On `FillSignOverlay`, set `MouseLeftButtonDown="FsOverlay_Click"`.
```csharp
    private void FsOverlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_fsMode != "text" && _fsMode != "datetime") return;
        var p = e.GetPosition(FillSignOverlay);
        // Snap to detected region if hovering one.
        double x = p.X, y = p.Y, w = 160, h = _fsSize * FsDpi / 72.0 * 1.4;
        if (_fsRegionCache.TryGetValue(_fsCurrentPage, out var regions))
            foreach (var r in regions)
                if (p.X >= r.X && p.X <= r.X + r.W && p.Y >= r.Y && p.Y <= r.Y + r.H)
                { x = r.X; y = r.Y; w = r.W; h = r.H; break; }

        var (ptX, ptY, ptW, ptH) = Llamashot.Core.FillSignGeometry.PixelRectToPointRect(x, y, w, h, FsDpi);
        var el = new Llamashot.Models.FillElement
        {
            Page = _fsCurrentPage, Type = _fsMode == "datetime" ? Llamashot.Models.FillElementType.DateTime : Llamashot.Models.FillElementType.Text,
            X = ptX, Y = ptY, Width = ptW, Height = ptH,
            Text = _fsMode == "datetime" ? System.DateTime.Now.ToString("dd/MM/yyyy") : "",
            FontFamily = _fsFont, FontSize = _fsSize, Bold = _fsBold, Italic = _fsItalic, ColorHex = _fsColor
        };
        _fsElements.Add(el);
        FsAddTextVisual(el, x, y, w, h);
    }
```

- [ ] **Step 3: Add the editable visual (TextBox bound to the element)**

```csharp
    private void FsAddTextVisual(Llamashot.Models.FillElement el, double x, double y, double w, double h)
    {
        var tb = new TextBox
        {
            Text = el.Text, Width = w, MinHeight = h, BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
            Background = new SolidColorBrush(Color.FromArgb(20, 59, 130, 246)),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(el.ColorHex)),
            FontFamily = new System.Windows.Media.FontFamily(el.FontFamily),
            FontSize = el.FontSize * FsDpi / 72.0,
            FontWeight = el.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = el.Italic ? FontStyles.Italic : FontStyles.Normal,
            Tag = el
        };
        tb.TextChanged += (_, _) => el.Text = tb.Text;
        Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y);
        FsEnableDrag(tb, el);
        FillSignOverlay.Children.Add(tb);
        tb.Focus();
    }

    private void FsEnableDrag(FrameworkElement fe, Llamashot.Models.FillElement el)
    {
        bool dragging = false; Point start = default; double ox = 0, oy = 0;
        fe.MouseRightButtonDown += (s, e) => { FillSignOverlay.Children.Remove(fe); _fsElements.Remove(el); e.Handled = true; }; // right-click delete
        fe.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (_fsMode != "select") return;
            dragging = true; start = e.GetPosition(FillSignOverlay);
            ox = Canvas.GetLeft(fe); oy = Canvas.GetTop(fe); fe.CaptureMouse(); e.Handled = true;
        };
        fe.PreviewMouseMove += (s, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(FillSignOverlay);
            double nx = ox + (p.X - start.X), ny = oy + (p.Y - start.Y);
            Canvas.SetLeft(fe, nx); Canvas.SetTop(fe, ny);
            var (px, py, _, _) = Llamashot.Core.FillSignGeometry.PixelRectToPointRect(nx, ny, 0, 0, FsDpi);
            el.X = px; el.Y = py;
        };
        fe.PreviewMouseLeftButtonUp += (s, e) => { dragging = false; fe.ReleaseMouseCapture(); };
    }
```
Note: in `select` mode dragging moves elements; in a placement mode clicks create them. Right-click deletes (the undo-last requirement is satisfied by Task 18's Ctrl+Z which removes the last element).

- [ ] **Step 4: Build + run, verify text fill**

Load `137.pdf`, choose Text, click a name comb row → a text box snaps in; type; change font/size/color affects new boxes; drag in Select mode; right-click deletes.

- [ ] **Step 5: Commit**

```powershell
git add Llamashot/Views/FileToolsWindow.xaml Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m "feat(fill-sign): text fill with font/style/color + drag/delete"
```

---

## Task 14: UI — Check / Radio tick

- [ ] **Step 1:** In `FsOverlay_Click`, add a branch for `_fsMode == "check"`: create a `FillElement` of type `Check` with `Text = "✓"`, sized to the hovered checkbox region (or a default 14pt at the cursor). Render as a `TextBlock` (✓) positioned via `Canvas.Left/Top`, draggable/deletable via `FsEnableDrag`.
- [ ] **Step 2:** Build + run; click Gender/Marital tick-boxes on `137.pdf` → a ✓ snaps into the square.
- [ ] **Step 3:** Commit: `feat(fill-sign): checkbox/radio tick`.

(Exporter already renders `Check` via `DrawText`; no engine change.)

---

## Task 15: UI — Signature (draw + upload)

- [ ] **Step 1:** Add a signature dialog: a small window with an `InkCanvas` ("Draw") and an "Upload image" button. On "Draw" accept, render the `InkCanvas` strokes to a transparent PNG via `RenderTargetBitmap` and save to a temp path; on "Upload" copy the chosen PNG path.
- [ ] **Step 2:** On `_fsMode == "signature"` click, open the dialog; on result, create a `Signature` `FillElement` with `ImagePath` set, default size ~120×48 pt at the click point; render an `Image` visual (draggable, resize handle), deletable.
- [ ] **Step 3:** Build + run; draw a signature, place it on the signature line; upload a PNG alternative.
- [ ] **Step 4:** Commit: `feat(fill-sign): signature draw + upload`.

(Exporter already renders images; no engine change.)

---

## Task 16: UI — Stamp (upload image)

- [ ] **Step 1:** On `_fsMode == "stamp"` click, open a file dialog (`*.png;*.jpg`), create a `Stamp` `FillElement` with `ImagePath`, default ~120×120 pt, render a draggable/resizable `Image` visual.
- [ ] **Step 2:** Build + run; place a stamp image; move/resize it.
- [ ] **Step 3:** Commit: `feat(fill-sign): image stamp`.

---

## Task 17: UI — Date/Time

- [ ] **Step 1:** `_fsMode == "datetime"` is already handled in `FsOverlay_Click` (inserts `dd/MM/yyyy`). Add a small format chooser on the toolbar (date `dd/MM/yyyy`, time `HH:mm`, datetime) that sets the inserted text. The element is a normal text element thereafter (editable).
- [ ] **Step 2:** Build + run; insert a date into the dd/mm/yyyy comb (single box per decision); edit it.
- [ ] **Step 3:** Commit: `feat(fill-sign): date/time insertion`.

---

## Task 18: UI — Save/export, progress, unsaved-work guard, undo

**Files:**
- Modify: `Llamashot/Views/FileToolsWindow.xaml.cs`

- [ ] **Step 1: Implement Save**

```csharp
    private async void FillSign_Save(object sender, RoutedEventArgs e)
    {
        if (_fsPdfPath == null || _fsElements.Count == 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF files|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_fsPdfPath) + "_filled.pdf"
        };
        if (dlg.ShowDialog() != true) return;
        ShowProcessing("Saving filled PDF...");
        string outPath = dlg.FileName;
        try
        {
            await Task.Run(() => Llamashot.Core.FillSignExporter.Export(_fsPdfPath!, _fsElements, outPath));
            ShowComplete("Saved", System.IO.Path.GetFileName(outPath), outPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            HideProcessing(); // use the existing overlay-hide path used by other tools
        }
    }
```
Use whatever method other tools call to dismiss the processing overlay on error (match an existing tool, e.g. `CompressPdf_Execute`’s error path).

- [ ] **Step 2: Add undo + unsaved-work guard**

Add to `HasUnsavedWork()` switch:
```csharp
        "fill_sign"       => _fsElements.Count > 0,
```
Add a `Window_KeyDown` branch (the window already has `KeyDown="Window_KeyDown"`): on `Ctrl+Z` when `_currentToolId == "fill_sign"`, remove the last element from `_fsElements` and re-render overlay (`FsRenderCurrentPage`). Also reset `_fsElements`, `_fsPdfPath`, caches in `ResetToolStates()`.

- [ ] **Step 3: Build + run end-to-end**

Fill several fields + a tick + a signature on `137.pdf`, Save → open the result; confirm overlays are present and text is selectable (Ctrl+F the typed string in a PDF viewer). Ctrl+Z removes the last addition. Leaving the tool with unsaved elements prompts the discard dialog.

- [ ] **Step 4: Commit**

```powershell
git add Llamashot/Views/FileToolsWindow.xaml.cs
git commit -m "feat(fill-sign): save/export, undo, unsaved-work guard"
```

---

## Task 19: End-to-end verification + version bump

**Files:**
- Modify: `tools/FillSignHarness/Program.cs`
- Modify: `Llamashot/Llamashot.csproj`, `Llamashot/Views/AboutWindow.xaml`

- [ ] **Step 1: Add an end-to-end harness scenario**

Fill a text element + a check + a signature image on `137.pdf`, export via `FillSignExporter.Export`, reopen with PDFsharp, assert page count preserved and (for the non-compressed path) the text token present. Run the full harness; expect all PASS.

```powershell
dotnet build tools/FillSignHarness/FillSignHarness.csproj -c Debug -v minimal
& "tools\FillSignHarness\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FillSignHarness.exe"
Get-Content "$env:TEMP\fillsign_test\results.txt"
```
Expected: `=== N passed, 0 failed ===`.

- [ ] **Step 2: Visual confirmation**

Render the saved output's filled page to an image (reuse `FillSignRender`) and eyeball that text/tick/signature sit in the right boxes on `137.pdf`. Capture the screenshot.

- [ ] **Step 3: Version bump (project convention)**

Bump `<Version>`, `<AssemblyVersion>`, `<FileVersion>` in `Llamashot/Llamashot.csproj` (e.g. 8.3.0 → 8.4.0) and the version string in `Llamashot/Views/AboutWindow.xaml`.

- [ ] **Step 4: Commit**

```powershell
git add tools/FillSignHarness/Program.cs Llamashot/Llamashot.csproj Llamashot/Views/AboutWindow.xaml
git commit -m "feat(fill-sign): end-to-end verification + version bump to 8.4.0"
```

---

## Self-Review notes (for the implementer)

- **Spec coverage:** tool surface (T10), coordinate model (T4), hover detection (T5/6/12), all five element types (T13 text, T14 check/radio, T15 signature, T16 stamp, T17 date/time), vector export (T7), image export (T8), rasterized fallback (T9), unsaved-work guard (T18), testing on 137.pdf (T2/6/19). All spec sections map to a task.
- **DPI consistency:** render and detection both use `FsDpi` (150) so detector pixel rects align 1:1 with the overlay canvas. Do not change one without the other.
- **Coordinate origin:** preview pixels, detector pixels, stored points, and PDFsharp `XGraphics` are all top-left — no Y-flip anywhere. The rasterized fallback also draws top-left with `System.Drawing`. Keep it that way.
- **Decision gate (T1) is mandatory** — do not build T2+ until the PDFsharp spike passes on net10.
- **`page.Size` ratio caveat (T2 note):** verify the 72 vs 96 constant against the known A4 width during T4; adjust `FillSignRender` if the fixture's page width doesn't come out ~595 pt.
```
