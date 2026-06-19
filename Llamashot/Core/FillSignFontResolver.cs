using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Fonts;

namespace Llamashot.Core;

/// <summary>Maps offered families to installed Windows system fonts so glyphs embed.</summary>
public class FillSignFontResolver : IFontResolver
{
    static readonly string FontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    // family (lowercase) -> (regular, bold, italic, boldItalic) file names in the system Fonts folder.
    // Missing variants reuse the closest available file; unknown families fall back to Arial.
    static readonly Dictionary<string, (string reg, string bold, string ital, string boldItal)> Map = new()
    {
        ["segoe ui"]               = ("segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
        ["arial"]                  = ("arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
        ["calibri"]                = ("calibri.ttf", "calibrib.ttf", "calibrii.ttf", "calibriz.ttf"),
        ["candara"]                = ("Candara.ttf", "Candarab.ttf", "Candarai.ttf", "Candaraz.ttf"),
        ["consolas"]               = ("consola.ttf", "consolab.ttf", "consolai.ttf", "consolaz.ttf"),
        ["constantia"]             = ("constan.ttf", "constanb.ttf", "constani.ttf", "constanz.ttf"),
        ["corbel"]                 = ("corbel.ttf", "corbelb.ttf", "corbeli.ttf", "corbelz.ttf"),
        ["courier new"]            = ("cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"),
        ["courier"]                = ("cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"),
        ["ebrima"]                 = ("ebrima.ttf", "ebrimabd.ttf", "ebrima.ttf", "ebrimabd.ttf"),
        ["franklin gothic medium"] = ("framd.ttf", "framd.ttf", "framdit.ttf", "framdit.ttf"),
        ["gabriola"]               = ("Gabriola.ttf", "Gabriola.ttf", "Gabriola.ttf", "Gabriola.ttf"),
        ["georgia"]                = ("georgia.ttf", "georgiab.ttf", "georgiai.ttf", "georgiaz.ttf"),
        ["impact"]                 = ("impact.ttf", "impact.ttf", "impact.ttf", "impact.ttf"),
        ["ink free"]               = ("Inkfree.ttf", "Inkfree.ttf", "Inkfree.ttf", "Inkfree.ttf"),
        ["lucida console"]         = ("lucon.ttf", "lucon.ttf", "lucon.ttf", "lucon.ttf"),
        ["lucida handwriting"]     = ("LHANDW.TTF", "LHANDW.TTF", "LHANDW.TTF", "LHANDW.TTF"),
        ["palatino linotype"]      = ("pala.ttf", "palab.ttf", "palai.ttf", "palabi.ttf"),
        ["segoe print"]            = ("segoepr.ttf", "segoeprb.ttf", "segoepr.ttf", "segoeprb.ttf"),
        ["segoe script"]           = ("segoesc.ttf", "segoescb.ttf", "segoesc.ttf", "segoescb.ttf"),
        ["tahoma"]                 = ("tahoma.ttf", "tahomabd.ttf", "tahoma.ttf", "tahomabd.ttf"),
        ["times"]                  = ("times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"),
        ["times new roman"]        = ("times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"),
        ["trebuchet ms"]           = ("trebuc.ttf", "trebucbd.ttf", "trebucit.ttf", "trebucbi.ttf"),
        ["verdana"]                = ("verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf"),
        ["comic sans ms"]          = ("comic.ttf", "comicbd.ttf", "comici.ttf", "comicz.ttf"),
        ["bahnschrift"]            = ("bahnschrift.ttf", "bahnschrift.ttf", "bahnschrift.ttf", "bahnschrift.ttf"),
        // symbol font carries glyphs Arial lacks (✓ ✗ ● ■ …) so marks embed correctly
        ["segoe ui symbol"]        = ("seguisym.ttf", "seguisym.ttf", "seguisym.ttf", "seguisym.ttf"),
    };

    static readonly (string reg, string bold, string ital, string boldItal) Fallback =
        ("arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf");

    public byte[]? GetFont(string faceName) => File.Exists(faceName) ? File.ReadAllBytes(faceName) : null;

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var entry = Map.TryGetValue(familyName.ToLowerInvariant(), out var m) ? m : Fallback;
        string file = Pick(bold, italic, entry);
        string full = Path.Combine(FontsDir, file);
        if (!File.Exists(full)) // mapped face not installed on this machine → safe fallback
            full = Path.Combine(FontsDir, Pick(bold, italic, Fallback));
        return new FontResolverInfo(full);
    }

    static string Pick(bool b, bool i, (string reg, string bold, string ital, string boldItal) e)
        => b && i ? e.boldItal : b ? e.bold : i ? e.ital : e.reg;
}
