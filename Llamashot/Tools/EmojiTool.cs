using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Llamashot.Core;
using Llamashot.Models;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice;
using Vortice.Mathematics;
using DCommon = Vortice.DCommon;

namespace Llamashot.Tools;

public class EmojiTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Emoji;
    public override Cursor Cursor => CursorHelper.Get("Emoji");
    public string Emoji { get; set; }

    // Shared data for all emoji pickers
    public static readonly (string Icon, string Label)[] Categories = {
        ("⭐","All"), ("😀","Smileys"), ("👍","Gestures"), ("❤️","Hearts"),
        ("✨","Symbols"), ("🎉","Celebrate"), ("💬","Objects"), ("🐱","Animals"),
        ("🍕","Food"), ("🌸","Nature"), ("🚗","Travel")
    };

    public static readonly (string E, string N, int C)[] AllEmojis = {
        ("😀","grinning",0),("😃","smiley",0),("😄","smile",0),("😁","grin",0),
        ("😂","joy tears",0),("🤣","rofl",0),("😊","blush",0),("😇","angel",0),
        ("🙂","slight smile",0),("😉","wink",0),("😍","heart eyes",0),("🥰","love face",0),
        ("😘","kiss",0),("😋","yummy",0),("😎","cool sunglasses",0),("🤔","thinking",0),
        ("🤗","hug",0),("🤫","shush quiet",0),("🤭","oops giggle",0),("😏","smirk",0),
        ("😐","neutral",0),("🙄","eye roll",0),("😮","surprised",0),("😱","scream",0),
        ("😨","fearful",0),("😢","cry",0),("😭","sob crying",0),("😤","angry huff",0),
        ("😡","angry red",0),("🥺","pleading",0),("😈","devil",0),("💀","skull dead",0),
        ("👻","ghost",0),("🤡","clown",0),("💩","poop",0),("🤖","robot",0),("👽","alien",0),
        ("👍","thumbs up like good",1),("👎","thumbs down dislike bad",1),("👏","clap applause",1),
        ("🙌","raised hands hooray",1),("🤝","handshake deal",1),("👊","fist bump",1),
        ("✊","fist power",1),("🤞","crossed fingers luck",1),("🤟","love you gesture",1),
        ("🤘","rock on",1),("👌","ok okay",1),("🤌","pinch italian",1),
        ("👈","point left",1),("👉","point right",1),("👆","point up",1),("👇","point down",1),
        ("☝️","index up",1),("👋","wave hello bye",1),("✋","high five stop",1),
        ("💪","muscle strong flex",1),("🙏","pray please thanks",1),("✌️","peace victory",1),
        ("❤️","red heart love",2),("🧡","orange heart",2),("💛","yellow heart",2),
        ("💚","green heart",2),("💙","blue heart",2),("💜","purple heart",2),
        ("🖤","black heart",2),("🤍","white heart",2),("🤎","brown heart",2),
        ("💔","broken heart",2),("💕","two hearts",2),("💞","revolving hearts",2),
        ("💓","beating heart",2),("💗","growing heart",2),("💖","sparkling heart",2),
        ("💘","cupid arrow heart",2),("💝","heart ribbon gift",2),
        ("⭐","star",3),("🌟","glowing star",3),("✨","sparkles magic",3),
        ("⚡","lightning bolt zap",3),("🔥","fire hot",3),("💥","boom explosion",3),
        ("💯","hundred perfect score",3),("✅","check mark done yes",3),("❌","cross wrong no",3),
        ("⚠️","warning caution",3),("🚫","prohibited no",3),("⛔","stop no entry",3),
        ("❓","question",3),("❗","exclamation important",3),("💡","lightbulb idea",3),
        ("🔔","bell notification",3),("📌","pin",3),("🔗","link chain url",3),
        ("🔴","red circle",3),("🟢","green circle",3),("🔵","blue circle",3),("🟡","yellow circle",3),
        ("➕","plus add",3),("➖","minus",3),("➡️","right arrow",3),
        ("⬆️","up arrow",3),("⬇️","down arrow",3),("⬅️","left arrow",3),
        ("🎉","party tada celebrate",4),("🎊","confetti ball",4),("🎈","balloon",4),
        ("🎁","gift present",4),("🎂","birthday cake",4),("🎄","christmas tree",4),
        ("🎃","halloween pumpkin",4),("🎆","fireworks",4),
        ("🏆","trophy winner champion",4),("🥇","gold medal first",4),("🥈","silver medal second",4),
        ("🥉","bronze medal third",4),("🎯","target bullseye goal",4),
        ("🎵","music note",4),("🎶","music notes",4),("🎸","guitar",4),
        ("🎮","game controller",4),("🎲","dice random",4),("🎨","art palette paint",4),
        ("💬","speech bubble chat message",5),("💭","thought bubble",5),("💰","money bag rich",5),
        ("💎","gem diamond",5),("🔑","key",5),("🔒","lock locked secure",5),("🔓","unlock open",5),
        ("📱","phone mobile",5),("💻","laptop computer",5),("🖥️","desktop monitor",5),
        ("📷","camera photo",5),("📹","video camera record",5),("🔍","search magnify find",5),
        ("📝","memo note write edit",5),("📋","clipboard",5),("📁","folder file",5),
        ("🗑️","trash delete remove",5),("🔧","wrench tool fix",5),("🔨","hammer build",5),
        ("⚙️","gear settings config",5),("📎","paperclip attach",5),("✏️","pencil edit",5),
        ("📊","chart graph bar",5),("📈","chart up trending",5),("⏰","alarm clock time",5),
        ("⏳","hourglass timer wait",5),("🔋","battery power",5),
        ("🐱","cat",6),("🐶","dog",6),("🐭","mouse",6),("🐹","hamster",6),
        ("🐰","rabbit bunny",6),("🦊","fox",6),("🐻","bear",6),("🐼","panda",6),
        ("🐨","koala",6),("🐯","tiger",6),("🦁","lion",6),("🐮","cow",6),
        ("🐷","pig",6),("🐸","frog",6),("🐵","monkey",6),("🐔","chicken",6),
        ("🐧","penguin",6),("🐦","bird",6),("🦅","eagle",6),("🦉","owl",6),
        ("🐝","bee",6),("🦋","butterfly",6),("🐞","ladybug",6),("🐙","octopus",6),
        ("🦈","shark",6),("🦄","unicorn",6),("🐉","dragon",6),
        ("🍕","pizza",7),("🍔","burger hamburger",7),("🍟","fries",7),("🌭","hot dog",7),
        ("🍿","popcorn",7),("🧁","cupcake",7),("🍩","donut",7),("🍪","cookie",7),
        ("🍰","cake",7),("🍫","chocolate",7),("🍬","candy",7),("🍭","lollipop",7),
        ("🍎","apple",7),("🍊","orange",7),("🍋","lemon",7),("🍌","banana",7),
        ("🍉","watermelon",7),("🍇","grapes",7),("🍓","strawberry",7),("🍑","peach",7),
        ("🥑","avocado",7),("🌶️","chili pepper hot",7),("☕","coffee",7),("🍵","tea",7),
        ("🍺","beer",7),("🍷","wine",7),
        ("🌸","cherry blossom flower",8),("🌹","rose",8),("🌻","sunflower",8),
        ("🌺","hibiscus",8),("🌷","tulip",8),("🌼","daisy flower",8),
        ("🍀","four leaf clover luck",8),("🍁","maple leaf fall",8),("🍂","fallen leaf autumn",8),
        ("🌊","wave ocean sea",8),("🌈","rainbow",8),("☀️","sun sunny",8),
        ("🌙","moon crescent night",8),("☁️","cloud",8),("🌧️","rain",8),
        ("❄️","snow snowflake cold",8),("🌪️","tornado",8),
        ("🌍","earth globe world",8),("⛰️","mountain",8),("🌋","volcano",8),
        ("🏖️","beach",8),("🌅","sunrise",8),
        ("🚗","car",9),("🚕","taxi cab",9),("🚌","bus",9),("🏎️","race car",9),
        ("🚑","ambulance",9),("🚒","fire truck",9),("🚲","bicycle bike",9),
        ("✈️","airplane plane fly",9),("🚀","rocket launch space",9),("🛸","ufo",9),
        ("🚁","helicopter",9),("⛵","sailboat",9),("🚢","ship boat",9),
        ("🏠","house home",9),("🏢","office building",9),("🏰","castle",9),
        ("🗼","tower",9),("🗽","statue liberty",9),
    };

    public static readonly List<string> RecentEmojis = new();

    public static void AddRecent(string emoji)
    {
        RecentEmojis.Remove(emoji);
        RecentEmojis.Insert(0, emoji);
        if (RecentEmojis.Count > 8) RecentEmojis.RemoveAt(8);
    }

    public EmojiTool(string emoji = "👍")
    {
        Emoji = emoji;
    }

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        double fontSize = 12 + Thickness * 4;
        var image = RenderEmoji(Emoji, (float)fontSize);
        if (image == null) return;

        double offset = image.Width / 2;
        Canvas.SetLeft(image, position.X - offset);
        Canvas.SetTop(image, position.Y - offset);
        canvas.Children.Add(image);

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Emoji,
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Points = new List<Point> { position },
            Text = Emoji,
            FontSize = fontSize,
            RenderedElement = image
        };
    }

    public override void OnMouseMove(Point position, Canvas canvas) { }
    public override void OnMouseUp(Point position, Canvas canvas) { }

    // ============ Direct2D Color Emoji Renderer ============
    // Direct2D + DirectWrite with DrawTextOptions.EnableColorFont
    // renders COLR/CPAL color emoji properly.

    private static readonly Dictionary<(string, int), BitmapSource> _cache = new();
    private static ID2D1Factory? _d2dFactory;
    private static IDWriteFactory? _dwFactory;

    public static Image? RenderEmoji(string emoji, float fontSize)
    {
        int sizeKey = (int)fontSize;
        if (_cache.TryGetValue((emoji, sizeKey), out var cached))
            return new Image { Source = cached, Width = cached.PixelWidth, Height = cached.PixelHeight };

        try
        {
            var source = RenderViaD2D(emoji, fontSize);
            if (source == null) return null;
            _cache[(emoji, sizeKey)] = source;
            return new Image { Source = source, Width = source.PixelWidth, Height = source.PixelHeight };
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? RenderViaD2D(string emoji, float fontSize)
    {
        // Lazy-init factories (reused across calls)
        _d2dFactory ??= D2D1.D2D1CreateFactory<ID2D1Factory>();
        _dwFactory ??= DWrite.DWriteCreateFactory<IDWriteFactory>();

        // Create text format + layout for measuring
        using var textFormat = _dwFactory.CreateTextFormat("Segoe UI Emoji", fontSize);
        using var textLayout = _dwFactory.CreateTextLayout(emoji, textFormat, 500, 500);
        var metrics = textLayout.Metrics;

        int w = (int)Math.Ceiling(metrics.WidthIncludingTrailingWhitespace) + 4;
        int h = (int)Math.Ceiling(metrics.Height) + 4;
        if (w <= 4 || h <= 4) return null;

        // Create GDI DC + DIB section for pixel extraction
        IntPtr screenDC = GetDC(IntPtr.Zero);
        IntPtr memDC = CreateCompatibleDC(screenDC);
        ReleaseDC(IntPtr.Zero, screenDC);

        var bmi = new BITMAPINFOHEADER
        {
            biSize = 40, biWidth = w, biHeight = -h,
            biPlanes = 1, biBitCount = 32
        };
        IntPtr hBitmap = CreateDIBSection(memDC, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (hBitmap == IntPtr.Zero) { DeleteDC(memDC); return null; }
        IntPtr oldBmp = SelectObject(memDC, hBitmap);

        // Clear to transparent
        int bufLen = w * h * 4;
        Marshal.Copy(new byte[bufLen], 0, bits, bufLen);

        // Create D2D render target bound to our GDI DC
        var rtProps = new RenderTargetProperties
        {
            PixelFormat = new DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, DCommon.AlphaMode.Premultiplied),
            DpiX = 96, DpiY = 96
        };
        using var dcRt = _d2dFactory.CreateDCRenderTarget(rtProps);
        dcRt.BindDC(memDC, new RawRect(0, 0, w, h));

        // Render with color font support
        dcRt.BeginDraw();
        dcRt.Clear(new Color4(0, 0, 0, 0));
        using var brush = dcRt.CreateSolidColorBrush(new Color4(0, 0, 0, 1));
        dcRt.DrawTextLayout(new Vector2(0, 0), textLayout, brush, DrawTextOptions.EnableColorFont);
        dcRt.EndDraw();

        // Read pixels from DIB section
        byte[] pixels = new byte[bufLen];
        Marshal.Copy(bits, pixels, 0, bufLen);

        var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        source.Freeze();

        // Cleanup GDI resources
        SelectObject(memDC, oldBmp);
        DeleteObject(hBitmap);
        DeleteDC(memDC);

        return source;
    }

    // ============ P/Invoke (GDI for DIB section) ============

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }
}
