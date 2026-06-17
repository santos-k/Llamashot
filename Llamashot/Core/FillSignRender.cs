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
        uint widthPx = (uint)(page.Size.Width * dpi / 96.0);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = widthPx });
        stream.Seek(0);
        using var ms = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }

    /// <summary>Page size in PDF points (72/in) and rotation.</summary>
    public static async Task<(double wPt, double hPt, int rotation)> GetPageSizeAsync(string pdfPath, int pageIndex)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
        var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        using var page = pdf.GetPage((uint)pageIndex);
        // WinRT page.Size is in 96-dpi DIPs; multiply by 72/96 to get true PDF points
        double wPt = page.Size.Width * 72.0 / 96.0;
        double hPt = page.Size.Height * 72.0 / 96.0;
        return (wPt, hPt, (int)page.Rotation);
    }
}
