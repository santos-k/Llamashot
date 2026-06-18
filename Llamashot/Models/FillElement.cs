namespace Llamashot.Models;

public enum FillElementType { Text, Check, Signature, Stamp, DateTime, Redact, Blur }

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
    public bool Underline { get; set; }
    /// <summary>"left" | "center" | "right" — horizontal text alignment within the element box.</summary>
    public string Align { get; set; } = "left";
    public string ColorHex { get; set; } = "#000000";
    public string? ImagePath { get; set; }
    /// <summary>Clockwise rotation in degrees (0–360), about the element's center.</summary>
    public double Rotation { get; set; }
    /// <summary>Opacity 0–100 (%). 100 = fully opaque.</summary>
    public double Opacity { get; set; } = 100;
}
