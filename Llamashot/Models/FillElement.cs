namespace Llamashot.Models;

public enum FillElementType { Text, Check, Signature, Stamp, DateTime }

/// <summary>One placed annotation. Geometry is in PDF points, top-left origin.</summary>
public class FillElement
{
    public int Page { get; set; }
    public FillElementType Type { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Text { get; set; } = "";
    public string FontFamily { get; set; } = "Arial";
    public double FontSize { get; set; } = 12;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public string ColorHex { get; set; } = "#000000";
    public string? ImagePath { get; set; }
}
