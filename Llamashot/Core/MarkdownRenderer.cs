using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Llamashot.Core;

public static class MarkdownRenderer
{
    private static readonly FontFamily MonoFont = new("Cascadia Code, Consolas, Courier New");
    private static readonly FontFamily SansFont = new("Segoe UI, Arial");
    private static readonly Color TextColor = Color.FromRgb(0xDC, 0xDC, 0xDC);
    private static readonly Color HeadingColor = Color.FromRgb(0x56, 0x9C, 0xD6);
    private static readonly Color CodeBgColor = Color.FromRgb(0x2D, 0x2D, 0x2D);
    private static readonly Color CodeColor = Color.FromRgb(0xCE, 0x91, 0x78);
    private static readonly Color LinkColor = Color.FromRgb(0x42, 0xA5, 0xF5);
    private static readonly Color QuoteColor = Color.FromRgb(0x88, 0x88, 0x88);
    private static readonly Color HrColor = Color.FromRgb(0x44, 0x44, 0x44);

    public static FlowDocument Render(string markdown)
    {
        var doc = new FlowDocument
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(TextColor),
            FontFamily = SansFont,
            FontSize = 14,
            PagePadding = new Thickness(24, 16, 24, 16)
        };

        var lines = markdown.Split('\n');
        int i = 0;
        bool inCodeBlock = false;
        var codeBlockLines = new List<string>();

        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');

            if (line.TrimStart().StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    AddCodeBlock(doc, string.Join("\n", codeBlockLines));
                    codeBlockLines.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    inCodeBlock = true;
                }
                i++;
                continue;
            }

            if (inCodeBlock)
            {
                codeBlockLines.Add(line);
                i++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            var trimmed = line.Trim();
            if (Regex.IsMatch(trimmed, @"^[-*_]{3,}$"))
            {
                var hr = new Paragraph
                {
                    Margin = new Thickness(0, 8, 0, 8),
                    BorderBrush = new SolidColorBrush(HrColor),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 0, 0, 8)
                };
                doc.Blocks.Add(hr);
                i++;
                continue;
            }

            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var text = headingMatch.Groups[2].Value;
                var fontSize = level switch
                {
                    1 => 28.0,
                    2 => 24.0,
                    3 => 20.0,
                    4 => 17.0,
                    5 => 15.0,
                    _ => 14.0
                };

                var para = new Paragraph
                {
                    FontSize = fontSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(HeadingColor),
                    Margin = new Thickness(0, level <= 2 ? 16 : 10, 0, 6)
                };
                AddInlineMarkdown(para, text);
                doc.Blocks.Add(para);
                i++;
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                var quoteText = trimmed.TrimStart('>').TrimStart();
                var para = new Paragraph
                {
                    Foreground = new SolidColorBrush(QuoteColor),
                    FontStyle = FontStyles.Italic,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(12, 4, 0, 4),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                AddInlineMarkdown(para, quoteText);
                doc.Blocks.Add(para);
                i++;
                continue;
            }

            var ulMatch = Regex.Match(line, @"^(\s*)[-*+]\s+(.+)$");
            if (ulMatch.Success)
            {
                var indent = ulMatch.Groups[1].Value.Length / 2;
                var text = ulMatch.Groups[2].Value;
                var para = new Paragraph
                {
                    Margin = new Thickness(16 + indent * 16, 2, 0, 2)
                };
                para.Inlines.Add(new Run("\u2022  ") { Foreground = new SolidColorBrush(HeadingColor) });
                AddInlineMarkdown(para, text);
                doc.Blocks.Add(para);
                i++;
                continue;
            }

            var olMatch = Regex.Match(line, @"^(\s*)(\d+)\.\s+(.+)$");
            if (olMatch.Success)
            {
                var indent = olMatch.Groups[1].Value.Length / 2;
                var num = olMatch.Groups[2].Value;
                var text = olMatch.Groups[3].Value;
                var para = new Paragraph
                {
                    Margin = new Thickness(16 + indent * 16, 2, 0, 2)
                };
                para.Inlines.Add(new Run($"{num}.  ") { Foreground = new SolidColorBrush(HeadingColor) });
                AddInlineMarkdown(para, text);
                doc.Blocks.Add(para);
                i++;
                continue;
            }

            {
                var para = new Paragraph { Margin = new Thickness(0, 3, 0, 3) };
                AddInlineMarkdown(para, line);
                doc.Blocks.Add(para);
            }

            i++;
        }

        if (inCodeBlock && codeBlockLines.Count > 0)
            AddCodeBlock(doc, string.Join("\n", codeBlockLines));

        return doc;
    }

    private static void AddCodeBlock(FlowDocument doc, string code)
    {
        var para = new Paragraph
        {
            FontFamily = MonoFont,
            FontSize = 12,
            Background = new SolidColorBrush(CodeBgColor),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 6, 0, 6),
            LineHeight = 18
        };
        para.Inlines.Add(new Run(code));
        doc.Blocks.Add(para);
    }

    private static void AddInlineMarkdown(Paragraph para, string text)
    {
        var inlinePattern = @"(?:\*\*|__)(.+?)(?:\*\*|__)|(?:\*|_)(.+?)(?:\*|_)|`([^`]+)`|\[([^\]]+)\]\(([^)]+)\)";
        var matches = Regex.Matches(text, inlinePattern);

        if (matches.Count == 0)
        {
            para.Inlines.Add(new Run(text));
            return;
        }

        int lastIndex = 0;
        foreach (Match m in matches)
        {
            if (m.Index > lastIndex)
                para.Inlines.Add(new Run(text[lastIndex..m.Index]));

            if (m.Groups[1].Success)
            {
                para.Inlines.Add(new Run(m.Groups[1].Value) { FontWeight = FontWeights.Bold });
            }
            else if (m.Groups[2].Success)
            {
                para.Inlines.Add(new Run(m.Groups[2].Value) { FontStyle = FontStyles.Italic });
            }
            else if (m.Groups[3].Success)
            {
                para.Inlines.Add(new Run(m.Groups[3].Value)
                {
                    FontFamily = MonoFont,
                    Background = new SolidColorBrush(CodeBgColor),
                    Foreground = new SolidColorBrush(CodeColor),
                    FontSize = 12
                });
            }
            else if (m.Groups[4].Success)
            {
                var hyperlink = new Hyperlink(new Run(m.Groups[4].Value))
                {
                    Foreground = new SolidColorBrush(LinkColor),
                    TextDecorations = null
                };
                try
                {
                    hyperlink.NavigateUri = new Uri(m.Groups[5].Value, UriKind.RelativeOrAbsolute);
                    hyperlink.RequestNavigate += (s, e) =>
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
                        { UseShellExecute = true });
                    };
                }
                catch { }
                para.Inlines.Add(hyperlink);
            }

            lastIndex = m.Index + m.Length;
        }

        if (lastIndex < text.Length)
            para.Inlines.Add(new Run(text[lastIndex..]));
    }
}
