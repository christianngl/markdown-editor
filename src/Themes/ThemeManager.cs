using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace MarkdownEditor.Themes
{
    /// <summary>
    /// Setzt die Theme-Farben als Anwendungsressourcen. Alle Steuerelement-Stile referenzieren
    /// diese per DynamicResource – dadurch wirkt eine Theme-Umschaltung sofort, ohne Neustart.
    /// </summary>
    public static class ThemeManager
    {
        public static Theme Current { get; private set; }

        public static void Initialize(Application app)
        {
            var styles = (ResourceDictionary)XamlReader.Parse(Styles.Xaml);
            app.Resources.MergedDictionaries.Add(styles);
        }

        public static void Apply(Theme theme)
        {
            Current = theme;
            ResourceDictionary r = Application.Current.Resources;
            r["EditorBg"] = theme.EditorBg;
            r["ChromeBg"] = theme.ChromeBg;
            r["ChromeFg"] = theme.ChromeFg;
            r["ChromeBorder"] = theme.Border;
            r["MenuBg"] = theme.MenuBg;
            r["MenuHover"] = theme.MenuHover;
            r["Accent"] = theme.Accent;
            r["Dim"] = theme.Dim;
            r["ScrollThumb"] = theme.ScrollThumb;

            // Abgeleitete coss-Tokens: gedämpfte Flächen und Texte aus der Vordergrundfarbe,
            // Primärfläche = Vordergrund (dunkel auf hell bzw. hell auf dunkel), Zustandsfarben fest.
            Color fg = theme.ChromeTextColor;
            r["MutedFg"] = Alpha(fg, 0.68);
            r["Subtle"] = Alpha(fg, theme.IsDark ? 0.06 : 0.045);
            r["SubtleStrong"] = Alpha(fg, theme.IsDark ? 0.10 : 0.07);
            r["Hairline"] = Alpha(fg, theme.IsDark ? 0.08 : 0.08);
            r["InputBorder"] = Alpha(fg, theme.IsDark ? 0.14 : 0.14);
            r["Ring"] = Alpha(fg, 0.28);
            r["PrimaryBg"] = theme.ChromeFg;
            r["PrimaryFg"] = theme.EditorBg;
            r["SurfaceBg"] = theme.MenuBg;
            r["SuccessFg"] = Solid(theme.IsDark ? "#34D399" : "#047857");
            r["SuccessBg"] = Alpha(Hex("#10B981"), theme.IsDark ? 0.14 : 0.10);
            r["WarningFg"] = Solid(theme.IsDark ? "#FBBF24" : "#B45309");
            r["WarningBg"] = Alpha(Hex("#F59E0B"), theme.IsDark ? 0.14 : 0.12);
            r["DangerFg"] = Solid(theme.IsDark ? "#FCA5A5" : "#B91C1C");
            r["DangerBg"] = Alpha(Hex("#EF4444"), theme.IsDark ? 0.14 : 0.10);
            r["DangerBorder"] = Alpha(Hex("#EF4444"), 0.38);
            r["DangerDot"] = Solid("#EF4444");
            r["WarningDot"] = Solid("#F59E0B");
            r["SuccessDot"] = Solid("#10B981");

            foreach (Window w in Application.Current.Windows)
                ApplyTitleBar(w);
        }

        static Color Hex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        static Brush Solid(string hex)
        {
            var b = new SolidColorBrush(Hex(hex));
            b.Freeze();
            return b;
        }

        /// <summary>Farbe mit Deckkraft als echte ARGB-Farbe (nicht Brush.Opacity), damit sie sich in Vorlagen sauber mischt.</summary>
        static Brush Alpha(Color c, double opacity)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        // ---------------------------------------------------------------- Titelleiste (Windows 10/11)

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_CAPTION_COLOR = 35;
        const int DWMWA_TEXT_COLOR = 36;

        public static void ApplyTitleBar(Window window)
        {
            if (Current == null) return;
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                int dark = Current.IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                int caption = ToColorRef(Current.ChromeColor);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                int text = ToColorRef(Current.ChromeTextColor);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
            catch (Exception)
            {
                // Ältere Windows-Versionen: Titelleiste bleibt im Systemstil.
            }
        }

        static int ToColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }
    }
}
