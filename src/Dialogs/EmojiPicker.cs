using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MarkdownEditor.Editor;
using MarkdownEditor.Themes;

namespace MarkdownEditor.Dialogs
{
    /// <summary>
    /// Emoji-Auswahl im Stil der Apple-Tastatur: Suche, „Häufig verwendet“, Kategorien mit Schnellsprung.
    /// Das Fenster wird wiederverwendet (nur ausgeblendet), damit es ab dem zweiten Öffnen sofort erscheint.
    /// </summary>
    public sealed class EmojiPicker : Window
    {
        const double Cell = 36;
        const double ImageSize = 26;

        readonly TextBox _search = new TextBox { Margin = new Thickness(8, 8, 8, 6), VerticalContentAlignment = VerticalAlignment.Center };
        readonly TextBlock _placeholder = new TextBlock { Text = "Emoji suchen (z. B. Herz, Katze, Daumen)", Margin = new Thickness(15, 13, 0, 0), Opacity = 0.5, IsHitTestVisible = false };
        readonly ScrollViewer _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        readonly StackPanel _sections = new StackPanel { Margin = new Thickness(6, 0, 6, 6) };
        readonly StackPanel _searchResults = new StackPanel { Margin = new Thickness(6, 0, 6, 6) };
        readonly TextBlock _hover = new TextBlock { Margin = new Thickness(10, 4, 10, 2), FontSize = 12, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly Dictionary<string, FrameworkElement> _headers = new Dictionary<string, FrameworkElement>();
        readonly Func<IList<string>> _recentProvider;
        WrapPanel _recentPanel;
        FrameworkElement _recentHeader;

        /// <summary>Wird mit dem gewählten Emoji (Unicode) ausgelöst.</summary>
        public event Action<string> EmojiChosen;

        public EmojiPicker(Func<IList<string>> recentProvider)
        {
            _recentProvider = recentProvider;
            Title = "Emoji";
            Width = 392;
            Height = 440;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = true;
            SetResourceReference(BackgroundProperty, "MenuBg");
            SetResourceReference(ForegroundProperty, "ChromeFg");

            var searchHost = new Grid();
            searchHost.Children.Add(_search);
            searchHost.Children.Add(_placeholder);
            _placeholder.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");
            _search.TextChanged += delegate { OnSearch(); };
            _search.PreviewKeyDown += OnSearchKey;

            BuildSections();
            _scroll.Content = _sections;

            var catBar = new UniformGrid { Rows = 1, Margin = new Thickness(4, 2, 4, 6) };
            foreach (EmojiCategory cat in EmojiCatalog.Categories)
            {
                EmojiCategory c = cat;
                FrameworkElement icon = MakeGlyph(EmojiCatalog.Find(cat.Icon), cat.Icon, 20);
                var b = new Button { Content = icon, ToolTip = cat.Name, Padding = new Thickness(2), Margin = new Thickness(1), BorderThickness = new Thickness(0), Focusable = false };
                b.SetResourceReference(BackgroundProperty, "MenuBg");
                b.Click += delegate { JumpTo(c.Name); };
                catBar.Children.Add(b);
            }

            var bottom = new DockPanel();
            DockPanel.SetDock(_hover, Dock.Top);
            bottom.Children.Add(_hover);
            bottom.Children.Add(catBar);
            _hover.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");

            var root = new DockPanel();
            DockPanel.SetDock(searchHost, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(searchHost);
            root.Children.Add(bottom);
            root.Children.Add(_scroll);

            var frame = new Border { Child = root, BorderThickness = new Thickness(1) };
            frame.SetResourceReference(Border.BorderBrushProperty, "ChromeBorder");
            Content = frame;

            Deactivated += delegate { Hide(); };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
            SourceInitialized += delegate { ThemeManager.ApplyTitleBar(this); };
        }

        /// <summary>Zeigt den Picker an einer Bildschirmposition (in DIP).</summary>
        public void ShowAt(Point topLeft)
        {
            RefreshRecent();
            _search.Text = "";
            Rect area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(topLeft.X, area.Right - Width));
            Top = topLeft.Y + Height > area.Bottom ? Math.Max(area.Top, topLeft.Y - Height - 28) : topLeft.Y;
            Show();
            Activate();
            _scroll.ScrollToHome();
            _search.Focus();
        }

        void BuildSections()
        {
            _recentHeader = MakeHeader("Häufig verwendet");
            _recentPanel = new WrapPanel();
            _sections.Children.Add(_recentHeader);
            _sections.Children.Add(_recentPanel);
            RefreshRecent();

            foreach (EmojiCategory cat in EmojiCatalog.Categories)
            {
                FrameworkElement header = MakeHeader(cat.Name);
                _headers[cat.Name] = header;
                _sections.Children.Add(header);
                var panel = new WrapPanel();
                foreach (Emoji e in cat.Items)
                    if (e.Image != null) panel.Children.Add(MakeCell(e)); // ohne Bild zeigt Windows oft nur ein Kästchen
                _sections.Children.Add(panel);
            }
        }

        void RefreshRecent()
        {
            _recentPanel.Children.Clear();
            IList<string> recent = _recentProvider != null ? _recentProvider() : null;
            if (recent != null)
                foreach (string s in recent)
                {
                    Emoji e = EmojiCatalog.Find(s);
                    if (e != null) _recentPanel.Children.Add(MakeCell(e));
                }
            Visibility v = _recentPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _recentHeader.Visibility = v;
            _recentPanel.Visibility = v;
        }

        static FrameworkElement MakeHeader(string text)
        {
            var tb = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(4, 10, 0, 4), Opacity = 0.7 };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");
            return tb;
        }

        FrameworkElement MakeCell(Emoji e)
        {
            var cell = new Border
            {
                Width = Cell,
                Height = Cell,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Child = MakeGlyph(e, e.Symbol, ImageSize),
                ToolTip = e.Name,
                Cursor = Cursors.Hand
            };
            cell.MouseEnter += delegate { cell.SetResourceReference(Border.BackgroundProperty, "MenuHover"); _hover.Text = e.Symbol + "  " + e.Name; };
            cell.MouseLeave += delegate { cell.Background = Brushes.Transparent; };
            cell.MouseLeftButtonUp += delegate { Choose(e.Symbol); };
            return cell;
        }

        /// <summary>Farbiges Bild, falls vorhanden – sonst das Zeichen in der Systemschrift.</summary>
        public static FrameworkElement MakeGlyph(Emoji e, string fallback, double size)
        {
            if (e != null && e.Image != null)
            {
                var img = new Image { Source = e.Image, Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
            return new TextBlock { Text = fallback, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = size * 0.8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        void Choose(string symbol)
        {
            Hide();
            Action<string> h = EmojiChosen;
            if (h != null) h(symbol);
        }

        void JumpTo(string category)
        {
            if (_search.Text.Length > 0) _search.Text = "";
            FrameworkElement header;
            if (!_headers.TryGetValue(category, out header)) return;
            _scroll.UpdateLayout();
            Point p = header.TranslatePoint(new Point(0, 0), _sections);
            _scroll.ScrollToVerticalOffset(p.Y - 4);
        }

        void OnSearch()
        {
            string q = _search.Text.Trim();
            _placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (q.Length == 0)
            {
                _scroll.Content = _sections;
                return;
            }
            _searchResults.Children.Clear();
            List<Emoji> hits = EmojiCatalog.Search(q, 200);
            _searchResults.Children.Add(MakeHeader(hits.Count > 0 ? "Suchergebnisse" : "Keine Treffer"));
            var panel = new WrapPanel();
            foreach (Emoji e in hits)
                if (e.Image != null) panel.Children.Add(MakeCell(e));
            _searchResults.Children.Add(panel);
            _scroll.Content = _searchResults;
            _scroll.ScrollToHome();
        }

        void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            List<Emoji> hits = EmojiCatalog.Search(_search.Text.Trim(), 1);
            if (hits.Count > 0) Choose(hits[0].Symbol);
        }
    }
}
