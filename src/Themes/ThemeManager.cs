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

            foreach (Window w in Application.Current.Windows)
                ApplyTitleBar(w);
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
