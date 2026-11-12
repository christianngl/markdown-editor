using System;
using System.Windows.Media;

namespace MarkdownEditor.Themes
{
    /// <summary>Farbschema für Editor, Vorschau und Fensterrahmen.</summary>
    public sealed class Theme
    {
        public readonly string Name;
        public readonly bool IsDark;
        public readonly Brush EditorBg, Text, Dim, Accent, Heading, Link, CodeBg, CodeText, Quote, Border, Selection;
        public readonly Brush ChromeBg, ChromeFg, MenuBg, MenuHover, ScrollThumb;
        public readonly Color ChromeColor, ChromeTextColor;

        public Theme(string name, bool dark,
            string editorBg, string text, string dim, string accent, string heading, string link,
            string codeBg, string codeText, string quote,
            string chromeBg, string chromeFg, string border, string menuBg, string menuHover, string selection)
        {
            Name = name;
            IsDark = dark;
            EditorBg = B(editorBg);
            Text = B(text);
            Dim = B(dim);
            Accent = B(accent);
            Heading = B(heading);
            Link = B(link);
            CodeBg = B(codeBg);
            CodeText = B(codeText);
            Quote = B(quote);
            ChromeBg = B(chromeBg);
            ChromeFg = B(chromeFg);
            Border = B(border);
            MenuBg = B(menuBg);
            MenuHover = B(menuHover);
            Selection = B(selection);
            ScrollThumb = B(dim, 0.55);
            ChromeColor = C(chromeBg);
            ChromeTextColor = C(chromeFg);
        }

        static Color C(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        static Brush B(string hex, double opacity = 1.0)
        {
            var b = new SolidColorBrush(C(hex)) { Opacity = opacity };
            b.Freeze();
            return b;
        }
    }

    public static class ThemeCatalog
    {
        //                                 Hintergrund Text       Gedimmt    Akzent     Überschr.  Link       Code-BG    Code       Zitat      Chrome     Chrome-FG  Rahmen     Menü       Hover      Auswahl
        public static readonly Theme[] All =
        {
            new Theme("Minimal Light",   false, "#FFFFFF", "#262626", "#8F8F8F", "#737373", "#171717", "#2563EB", "#F5F5F5", "#C2410C", "#5C5C5C", "#FAFAFA", "#262626", "#EBEBEB", "#FFFFFF", "#F0F0F0", "#BFD3F7"),
            new Theme("Minimal Dark",    true,  "#141414", "#E5E5E5", "#6B6B6B", "#8A8A8A", "#F5F5F5", "#60A5FA", "#1F1F1F", "#FBBF24", "#A3A3A3", "#101010", "#E5E5E5", "#262626", "#1C1C1C", "#2A2A2A", "#2B4A7A"),
            new Theme("Solarized Light", false, "#FDF6E3", "#586E75", "#A3AFAF", "#268BD2", "#CB4B16", "#268BD2", "#EEE8D5", "#D33682", "#657B83", "#EEE8D5", "#586E75", "#DDD6C1", "#FDF6E3", "#E4DCC5", "#93C3E8"),
            new Theme("Solarized Dark",  true,  "#002B36", "#93A1A1", "#4E6A72", "#268BD2", "#B58900", "#2AA198", "#073642", "#D33682", "#839496", "#00212B", "#93A1A1", "#0D3D4A", "#03313D", "#0E4452", "#2A6A85"),
            new Theme("Monokai",         true,  "#272822", "#F8F8F2", "#75715E", "#A6E22E", "#F92672", "#66D9EF", "#3E3D32", "#E6DB74", "#A59F85", "#1E1F1C", "#F8F8F2", "#3B3C35", "#2D2E27", "#49483E", "#75715E"),
            new Theme("Nord",            true,  "#2E3440", "#D8DEE9", "#616E88", "#88C0D0", "#8FBCBB", "#81A1C1", "#3B4252", "#EBCB8B", "#A3ABB9", "#272C36", "#D8DEE9", "#3B4252", "#323846", "#434C5E", "#5E81AC"),
            new Theme("Sepia",           false, "#F7F1E3", "#433422", "#A8987F", "#9C5A1A", "#5B3A1A", "#9C5A1A", "#EFE6D2", "#8A3B12", "#6E5B45", "#EFE6D2", "#433422", "#DCCFB4", "#F7F1E3", "#E6DAC0", "#D8B98A"),
        };

        public static Theme Find(string name)
        {
            foreach (Theme t in All)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            return All[0];
        }
    }
}
