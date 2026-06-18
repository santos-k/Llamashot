using System;
using System.IO;
using PdfSharp.Fonts;

namespace Llamashot.Core;

/// <summary>Maps offered families to installed Windows system fonts so glyphs embed.</summary>
public class FillSignFontResolver : IFontResolver
{
    static readonly string Fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    public byte[]? GetFont(string faceName) => File.Exists(faceName) ? File.ReadAllBytes(faceName) : null;

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        string fam = familyName.ToLowerInvariant();
        string file = fam switch
        {
            "times" or "times new roman" => Pick(bold, italic, "times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"),
            "courier" or "courier new"   => Pick(bold, italic, "cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"),
            // symbol font carries glyphs Arial lacks (✓ ✗ ● ■ …) so marks embed correctly
            "segoe ui symbol"            => "seguisym.ttf",
            _                             => Pick(bold, italic, "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
        };
        return new FontResolverInfo(Path.Combine(Fonts, file));
    }

    static string Pick(bool b, bool i, string reg, string bold, string ital, string boldItal)
        => b && i ? boldItal : b ? bold : i ? ital : reg;
}
