namespace Llamashot.Core;

public static class FillSignGeometry
{
    public static double PointsToPixels(double points, int dpi) => points * dpi / 72.0;
    public static double PixelsToPoints(double pixels, int dpi) => pixels * 72.0 / dpi;

    public static (double x, double y, double w, double h) PixelRectToPointRect(double px, double py, double pw, double ph, int dpi) =>
        (PixelsToPoints(px, dpi), PixelsToPoints(py, dpi), PixelsToPoints(pw, dpi), PixelsToPoints(ph, dpi));

    public static (double x, double y, double w, double h) PointRectToPixelRect(double x, double y, double w, double h, int dpi) =>
        (PointsToPixels(x, dpi), PointsToPixels(y, dpi), PointsToPixels(w, dpi), PointsToPixels(h, dpi));
}
