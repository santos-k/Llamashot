using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Llamashot.Core;

public static class SyntaxHighlighter
{
    private static readonly Color KeywordColor = Color.FromRgb(0x56, 0x9C, 0xD6);
    private static readonly Color StringColor = Color.FromRgb(0xCE, 0x91, 0x78);
    private static readonly Color CommentColor = Color.FromRgb(0x6A, 0x99, 0x55);
    private static readonly Color NumberColor = Color.FromRgb(0xB5, 0xCE, 0xA8);
    private static readonly Color TypeColor = Color.FromRgb(0x4E, 0xC9, 0xB0);
    private static readonly Color DefaultColor = Color.FromRgb(0xDC, 0xDC, 0xDC);
    private static readonly Color XmlTagColor = Color.FromRgb(0x56, 0x9C, 0xD6);
    private static readonly Color JsonKeyColor = Color.FromRgb(0x9C, 0xDC, 0xFE);

    private static readonly Dictionary<string, string[]> LanguageKeywords = new()
    {
        { "cs", new[] { "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while", "async", "await", "record", "init", "required" } },
        { "py", new[] { "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "None", "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while", "with", "yield" } },
        { "js", new[] { "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "export", "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof", "let", "new", "null", "of", "return", "super", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while", "with", "yield" } },
        { "java", new[] { "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue", "default", "do", "double", "else", "enum", "extends", "final", "finally", "float", "for", "if", "implements", "import", "instanceof", "int", "interface", "long", "native", "new", "null", "package", "private", "protected", "public", "return", "short", "static", "strictfp", "super", "switch", "synchronized", "this", "throw", "throws", "transient", "try", "void", "volatile", "while", "true", "false", "var", "record", "sealed" } },
        { "go", new[] { "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "for", "func", "go", "goto", "if", "import", "interface", "map", "package", "range", "return", "select", "struct", "switch", "type", "var", "nil", "true", "false" } },
        { "rs", new[] { "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static", "struct", "super", "trait", "true", "type", "unsafe", "use", "where", "while" } },
        { "sql", new[] { "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER", "TABLE", "INDEX", "VIEW", "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "ON", "AND", "OR", "NOT", "NULL", "IS", "IN", "BETWEEN", "LIKE", "ORDER", "BY", "GROUP", "HAVING", "LIMIT", "OFFSET", "AS", "SET", "VALUES", "INTO", "DISTINCT", "COUNT", "SUM", "AVG", "MAX", "MIN", "UNION", "ALL", "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END", "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT", "DEFAULT", "CHECK", "UNIQUE" } },
    };

    private static readonly Dictionary<string, string> ExtToLang = new(StringComparer.OrdinalIgnoreCase)
    {
        // C#
        { ".cs", "cs" }, { ".csx", "cs" },
        // C/C++
        { ".cpp", "cs" }, { ".c", "cs" }, { ".cc", "cs" }, { ".cxx", "cs" },
        { ".h", "cs" }, { ".hh", "cs" }, { ".hpp", "cs" }, { ".hxx", "cs" },
        { ".m", "cs" }, { ".mm", "cs" },
        // Python
        { ".py", "py" }, { ".pyw", "py" }, { ".pyi", "py" },
        // JavaScript / TypeScript
        { ".js", "js" }, { ".jsx", "js" }, { ".mjs", "js" }, { ".cjs", "js" },
        { ".ts", "js" }, { ".tsx", "js" },
        { ".vue", "js" }, { ".svelte", "js" },
        // JVM
        { ".java", "java" }, { ".kt", "java" }, { ".kts", "java" },
        { ".scala", "java" }, { ".groovy", "java" },
        // Go / Rust
        { ".go", "go" },
        { ".rs", "rs" },
        // SQL
        { ".sql", "sql" },
        // Scripting (use JS-like highlighting)
        { ".rb", "js" }, { ".php", "js" }, { ".swift", "js" }, { ".dart", "js" },
        { ".lua", "js" }, { ".pl", "js" }, { ".r", "js" },
        { ".jl", "js" }, { ".ex", "js" }, { ".exs", "js" },
    };

    public static FlowDocument Highlight(string text, string filePath)
    {
        var doc = new FlowDocument
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(DefaultColor),
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 13,
            PagePadding = new Thickness(16, 12, 16, 12)
        };

        var ext = Path.GetExtension(filePath);

        if (IsXmlLike(ext))
        {
            HighlightXml(doc, text);
            return doc;
        }

        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            HighlightJson(doc, text);
            return doc;
        }

        if (IsConfigFile(ext))
        {
            HighlightConfig(doc, text);
            return doc;
        }

        var lang = ExtToLang.GetValueOrDefault(ext ?? "", "");
        var keywords = lang != "" ? LanguageKeywords.GetValueOrDefault(lang) : null;
        var lineComment = lang switch
        {
            "py" => "#",
            "sql" => "--",
            _ when ext == ".bat" || ext == ".cmd" => "REM",
            _ when ext == ".ps1" || ext == ".sh" || ext == ".bash" => "#",
            _ => "//"
        };

        HighlightCode(doc, text, keywords, lineComment);
        return doc;
    }

    private static bool IsXmlLike(string? ext) =>
        ext != null && (ext.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".htm", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".xaml", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".svg", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase));

    private static bool IsConfigFile(string? ext) =>
        ext != null && (ext.Equals(".ini", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".toml", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".cfg", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".conf", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".env", StringComparison.OrdinalIgnoreCase));

    private static void HighlightCode(FlowDocument doc, string text, string[]? keywords, string lineComment)
    {
        var keywordSet = keywords != null ? new HashSet<string>(keywords) : null;
        var lines = text.Split('\n');
        var maxLines = Math.Min(lines.Length, 10000);

        for (int i = 0; i < maxLines; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 20 };

            para.Inlines.Add(new Run($"{i + 1,5}  ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            });

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(lineComment))
            {
                para.Inlines.Add(new Run(line) { Foreground = new SolidColorBrush(CommentColor) });
                doc.Blocks.Add(para);
                continue;
            }

            HighlightLine(para, line, keywordSet);
            doc.Blocks.Add(para);
        }

        if (lines.Length > maxLines)
        {
            var truncPara = new Paragraph { Margin = new Thickness(0, 10, 0, 0) };
            truncPara.Inlines.Add(new Run($"\n--- File truncated ({lines.Length:N0} lines total) ---")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontStyle = FontStyles.Italic
            });
            doc.Blocks.Add(truncPara);
        }
    }

    private static void HighlightLine(Paragraph para, string line, HashSet<string>? keywords)
    {
        var pattern = @"(""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`)|(\b\d+\.?\d*\b)|(//.*$)|(#.*$)|(\b[A-Za-z_]\w*\b)|(.)";
        var matches = Regex.Matches(line, pattern);

        foreach (Match m in matches)
        {
            if (m.Groups[1].Success)
                para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(StringColor) });
            else if (m.Groups[2].Success)
                para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(NumberColor) });
            else if (m.Groups[3].Success || m.Groups[4].Success)
                para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(CommentColor) });
            else if (m.Groups[5].Success)
            {
                if (keywords != null && keywords.Contains(m.Value))
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(KeywordColor) });
                else if (m.Value.Length > 0 && char.IsUpper(m.Value[0]))
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(TypeColor) });
                else
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(DefaultColor) });
            }
            else
                para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(DefaultColor) });
        }
    }

    private static void HighlightXml(FlowDocument doc, string text)
    {
        var lines = text.Split('\n');
        var maxLines = Math.Min(lines.Length, 10000);

        for (int i = 0; i < maxLines; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 20 };
            para.Inlines.Add(new Run($"{i + 1,5}  ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            });

            var xmlPattern = @"(<!--.*?-->)|(""[^""]*"")|(</?\w[\w:.-]*)(/?>|>)|([^<""]+)";
            foreach (Match m in Regex.Matches(line, xmlPattern))
            {
                if (m.Groups[1].Success)
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(CommentColor) });
                else if (m.Groups[2].Success)
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(StringColor) });
                else if (m.Groups[3].Success)
                {
                    para.Inlines.Add(new Run(m.Groups[3].Value) { Foreground = new SolidColorBrush(XmlTagColor) });
                    if (m.Groups[4].Success)
                        para.Inlines.Add(new Run(m.Groups[4].Value) { Foreground = new SolidColorBrush(DefaultColor) });
                }
                else
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(DefaultColor) });
            }

            doc.Blocks.Add(para);
        }
    }

    private static void HighlightJson(FlowDocument doc, string text)
    {
        var lines = text.Split('\n');
        var maxLines = Math.Min(lines.Length, 10000);

        for (int i = 0; i < maxLines; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 20 };
            para.Inlines.Add(new Run($"{i + 1,5}  ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            });

            var jsonPattern = @"(""(?:[^""\\]|\\.)*"")\s*(:)|(""(?:[^""\\]|\\.)*"")|(\b(?:true|false|null)\b)|(-?\d+\.?\d*(?:[eE][+-]?\d+)?)|([{}\[\]:,])|(.)";
            foreach (Match m in Regex.Matches(line, jsonPattern))
            {
                if (m.Groups[1].Success)
                {
                    para.Inlines.Add(new Run(m.Groups[1].Value) { Foreground = new SolidColorBrush(JsonKeyColor) });
                    para.Inlines.Add(new Run(m.Groups[2].Value) { Foreground = new SolidColorBrush(DefaultColor) });
                }
                else if (m.Groups[3].Success)
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(StringColor) });
                else if (m.Groups[4].Success)
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(KeywordColor) });
                else if (m.Groups[5].Success)
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(NumberColor) });
                else
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(DefaultColor) });
            }

            doc.Blocks.Add(para);
        }
    }

    private static void HighlightConfig(FlowDocument doc, string text)
    {
        var lines = text.Split('\n');
        var maxLines = Math.Min(lines.Length, 10000);

        for (int i = 0; i < maxLines; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 20 };
            para.Inlines.Add(new Run($"{i + 1,5}  ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            });

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                para.Inlines.Add(new Run(line) { Foreground = new SolidColorBrush(CommentColor) });
            }
            else if (trimmed.StartsWith('[') && trimmed.Contains(']'))
            {
                para.Inlines.Add(new Run(line) { Foreground = new SolidColorBrush(TypeColor) });
            }
            else if (trimmed.Contains('=') || trimmed.Contains(':'))
            {
                var sep = trimmed.Contains('=') ? '=' : ':';
                var idx = line.IndexOf(sep);
                para.Inlines.Add(new Run(line[..idx]) { Foreground = new SolidColorBrush(JsonKeyColor) });
                para.Inlines.Add(new Run(line[idx].ToString()) { Foreground = new SolidColorBrush(DefaultColor) });
                para.Inlines.Add(new Run(line[(idx + 1)..]) { Foreground = new SolidColorBrush(StringColor) });
            }
            else
            {
                para.Inlines.Add(new Run(line) { Foreground = new SolidColorBrush(DefaultColor) });
            }

            doc.Blocks.Add(para);
        }
    }
}
