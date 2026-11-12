using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;
using MarkdownEditor.Themes;

namespace MarkdownEditor
{
    /// <summary>
    /// Fensterrahmen im coss-Stil: Werkzeugleiste (Seitenleiste, Pfad + Speicherstatus, Ansicht-Segmente,
    /// Format-Symbole, Diktat, Suche), Seitenleisten-Tabs, Bereichsköpfe, Statusleiste und Lesebreite.
    /// Alle Schaltflächen rufen dieselben <see cref="Cmd"/>-Befehle auf wie Menüs und Tastenkürzel.
    /// </summary>
    public sealed partial class MainWindow
    {
        const double ReadingWidth = 760;

        ToggleButton _sidebarToggle;
        TextBlock _crumbFolder, _crumbName;
        FrameworkElement _crumbChevron;
        Border _saveBadge;
        Ellipse _saveDot;
        TextBlock _saveText;
        RadioButton _segBoth, _segMarkdown, _segPreview;
        RadioButton _sideTabOutline, _sideTabFiles, _sideTabFolder;
        Button _levelButton, _dictButton, _themeButton;
        TextBlock _levelText;
        StackPanel _formatIcons, _crumbPanel;
        TextBlock _statusCaret, _statusWords;
        Border _editorHeader, _previewHeader;
        string _dictState = "";

        // ================================================================ Werkzeugleiste

        Border BuildToolbar()
        {
            var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 0, 10, 0), Height = 52 };

            // Links: Seitenleiste + Pfad + Status
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _sidebarToggle = new ToggleButton { Content = Icons.Make(Icons.PanelLeft), ToolTip = "Seitenleiste (F9)" };
            _sidebarToggle.SetResourceReference(StyleProperty, "IconToggle");
            AutomationName(_sidebarToggle, "Seitenleiste");
            _sidebarToggle.Click += delegate { Execute(_cSidebar); };
            left.Children.Add(_sidebarToggle);

            _crumbPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _crumbFolder = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180 };
            _crumbFolder.SetResourceReference(TextBlock.ForegroundProperty, "MutedFg");
            _crumbChevron = Icons.Make(Icons.ChevronRight, 14);
            _crumbChevron.Margin = new Thickness(5, 0, 5, 0);
            _crumbChevron.SetResourceReference(TextElement.ForegroundProperty, "MutedFg");
            _crumbChevron.Opacity = 0.7;
            _crumbName = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
            _crumbPanel.Children.Add(_crumbFolder);
            _crumbPanel.Children.Add(_crumbChevron);
            _crumbPanel.Children.Add(_crumbName);

            _saveDot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            _saveText = new TextBlock { FontSize = 12, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal };
            badgeRow.Children.Add(_saveDot);
            badgeRow.Children.Add(_saveText);
            _saveBadge = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2, 9, 3), Margin = new Thickness(10, 0, 0, 0), Child = badgeRow, VerticalAlignment = VerticalAlignment.Center };
            _crumbPanel.Children.Add(_saveBadge);
            left.Children.Add(_crumbPanel);
            DockPanel.SetDock(left, Dock.Left);
            dock.Children.Add(left);

            // Rechts: Format, Diktat, Suche
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _levelText = new TextBlock { Text = "Standardtext", VerticalAlignment = VerticalAlignment.Center, MinWidth = 92 };
            var levelRow = new StackPanel { Orientation = Orientation.Horizontal };
            levelRow.Children.Add(_levelText);
            FrameworkElement chevron = Icons.Make(Icons.ChevronDown, 14);
            chevron.Margin = new Thickness(6, 0, 0, 0);
            chevron.SetResourceReference(TextElement.ForegroundProperty, "MutedFg");
            levelRow.Children.Add(chevron);
            _levelButton = new Button { Content = levelRow, Padding = new Thickness(10, 0, 8, 0), Margin = new Thickness(0, 0, 6, 0), Focusable = false, FontWeight = FontWeights.Normal, ToolTip = "Textebene (Strg+0 … Strg+6)" };
            _levelButton.Click += delegate { ShowLevelMenu(); };

            _formatIcons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _formatIcons.Children.Add(_levelButton);
            _formatIcons.Children.Add(ToolIcon(Icons.Bold, _cBold));
            _formatIcons.Children.Add(ToolIcon(Icons.Italic, _cItalic));
            _formatIcons.Children.Add(ToolIcon(Icons.Underline, _cUnderline));
            _formatIcons.Children.Add(ToolIcon(Icons.Strike, _cStrike));
            _formatIcons.Children.Add(ToolIcon(Icons.Code, _cCode));
            _formatIcons.Children.Add(ToolSep());
            _formatIcons.Children.Add(ToolIcon(Icons.Bullets, _cBullet));
            _formatIcons.Children.Add(ToolIcon(Icons.Numbers, _cOrdered));
            _formatIcons.Children.Add(ToolIcon(Icons.Task, _cTask));
            _formatIcons.Children.Add(ToolSep());
            _formatIcons.Children.Add(ToolIcon(Icons.Link, _cLink));
            _formatIcons.Children.Add(ToolIcon(Icons.Image, _cImage));
            _formatIcons.Children.Add(ToolIcon(Icons.Table, _cTable, "Tabelle"));
            _formatIcons.Children.Add(ToolIcon(Icons.Smile, _cEmoji, "Emoji"));
            _formatIcons.Children.Add(ToolSep());
            right.Children.Add(_formatIcons);

            _dictButton = new Button { Padding = new Thickness(10, 0, 8, 0), Margin = new Thickness(2, 0, 4, 0), Focusable = false };
            _dictButton.Click += delegate { Execute(_cDictate); };
            right.Children.Add(_dictButton);
            right.Children.Add(ToolIcon(Icons.Search, _cFind, "Suchen"));
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);

            // Mitte: Ansicht
            var seg = SegmentShell();
            _segBoth = Segment("Beide", "view", delegate { Execute(_cViewBoth); }, "Markdown und Vorschau (Strg+Umschalt+1)");
            _segMarkdown = Segment("Markdown", "view", delegate { Execute(_cViewMarkdown); }, "Nur Markdown (Strg+Umschalt+2)");
            _segPreview = Segment("Vorschau", "view", delegate { Execute(_cViewPreview); }, "Nur Vorschau (Strg+Umschalt+3)");
            var segRow = (StackPanel)seg.Child;
            segRow.Children.Add(_segBoth);
            segRow.Children.Add(_segMarkdown);
            segRow.Children.Add(_segPreview);
            seg.HorizontalAlignment = HorizontalAlignment.Center;
            seg.VerticalAlignment = VerticalAlignment.Center;
            seg.Margin = new Thickness(12, 0, 12, 0);
            dock.Children.Add(seg);

            var bar = new Border { Child = dock, BorderThickness = new Thickness(0, 0, 0, 1) };
            bar.SetResourceReference(Border.BackgroundProperty, "EditorBg");
            bar.SetResourceReference(Border.BorderBrushProperty, "Hairline");
            bar.SizeChanged += delegate { AdaptToolbar(bar.ActualWidth); };
            UpdateDictationButton();
            return bar;
        }

        /// <summary>Schmale Fenster: zuerst die Format-Symbole (bleiben über Menü und Kürzel erreichbar), dann Pfad und Diktat-Text ausblenden.</summary>
        void AdaptToolbar(double width)
        {
            _formatIcons.Visibility = width < 1160 ? Visibility.Collapsed : Visibility.Visible;
            _crumbPanel.Visibility = width < 720 ? Visibility.Collapsed : Visibility.Visible;
            _crumbFolder.Visibility = _crumbChevron.Visibility = width < 1320 && _formatIcons.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        Button ToolIcon(string icon, Cmd c, string name = null)
        {
            string label = name ?? c.Header.TrimEnd('…', '.');
            string gesture = GestureText(c);
            var b = new Button { Content = Icons.Make(icon), ToolTip = gesture.Length > 0 ? label + "  (" + gesture + ")" : label };
            b.SetResourceReference(StyleProperty, "Icon");
            AutomationName(b, label);
            b.Click += delegate { Execute(c); };
            return b;
        }

        static FrameworkElement ToolSep()
        {
            var r = new Border { Width = 1, Height = 20, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            r.SetResourceReference(Border.BackgroundProperty, "Hairline");
            return r;
        }

        static void AutomationName(DependencyObject d, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(d, name);
        }

        static Border SegmentShell()
        {
            var shell = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(3), Child = new StackPanel { Orientation = Orientation.Horizontal } };
            shell.SetResourceReference(Border.BackgroundProperty, "Subtle");
            return shell;
        }

        RadioButton Segment(string text, string group, Action click, string tip)
        {
            var rb = new RadioButton { Content = text, GroupName = group, ToolTip = tip, Margin = new Thickness(0, 0, 2, 0) };
            rb.SetResourceReference(StyleProperty, "Seg");
            rb.Click += delegate { click(); };
            return rb;
        }

        static FrameworkElement Kbd(string text)
        {
            var t = new TextBlock { Text = text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11 };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedFg");
            var b = new Border { Child = t, CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), VerticalAlignment = VerticalAlignment.Center };
            b.SetResourceReference(Border.BackgroundProperty, "Subtle");
            return b;
        }

        void ShowLevelMenu()
        {
            string kind = CaretBlockKind();
            var menu = new ContextMenu { PlacementTarget = _levelButton, Placement = PlacementMode.Bottom, VerticalOffset = 4 };
            menu.Items.Add(Opt(_cNormal, kind == "p"));
            for (int i = 0; i < _cHeadings.Length; i++) menu.Items.Add(Opt(_cHeadings[i], kind == "h" + (i + 1)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Opt(_cQuote, kind == "quote"));
            menu.Items.Add(Opt(_cCodeBlock, kind == "code"));
            menu.Items.Add(new Separator());
            menu.Items.Add(Opt(_cBullet, kind == "bullet"));
            menu.Items.Add(Opt(_cOrdered, kind == "ordered"));
            menu.Items.Add(Opt(_cTask, kind == "task" || kind == "done"));
            menu.Items.Add(MI(_cRule));
            menu.IsOpen = true;
        }

        void ShowThemeMenu()
        {
            var menu = new ContextMenu { PlacementTarget = _themeButton, Placement = PlacementMode.Top, VerticalOffset = -4 };
            menu.Items.Add(new MenuItem { Header = "Theme", Style = (Style)FindResource("MenuLabel") });
            foreach (Theme t in ThemeCatalog.All)
            {
                Theme theme = t;
                var item = new MenuItem { Header = t.Name, InputGestureText = t.IsDark ? "dunkel" : "hell", IsChecked = t == ThemeManager.Current };
                item.Click += delegate { ApplyTheme(theme); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(MI(_cDark));
            menu.IsOpen = true;
        }

        static string KindLabel(string kind)
        {
            switch (kind)
            {
                case "quote": return "Zitat";
                case "code": return "Codeblock";
                case "bullet": return "Aufzählung";
                case "ordered": return "Nummerierung";
                case "task": case "done": return "Aufgabe";
                case "table": return "Tabelle";
                default:
                    if (kind.Length == 2 && kind[0] == 'h') return "Überschrift " + kind[1];
                    return "Standardtext";
            }
        }

        /// <summary>Pfad, Speicherstatus, Textebene am Cursor und Diktat-Knopf auf den aktuellen Stand bringen.</summary>
        void UpdateChrome()
        {
            if (_crumbName == null) return;
            _crumbName.Text = _path != null ? Path.GetFileName(_path) : "Unbenannt";
            string folder = _path != null ? Path.GetFileName(Path.GetDirectoryName(_path) ?? "") : "";
            _crumbFolder.Text = folder.Length > 0 ? folder : (_path != null ? Path.GetPathRoot(_path) : "Neues Dokument");
            _crumbPanel.ToolTip = _path ?? "Noch nicht gespeichert";

            string fg, bg, dot, text;
            if (_dirty) { fg = "WarningFg"; bg = "WarningBg"; dot = "WarningDot"; text = _settings.AutoSave && _path != null ? "Wird gespeichert" : "Ungespeichert"; }
            else if (_path == null) { fg = "MutedFg"; bg = "Subtle"; dot = "MutedFg"; text = "Neu"; }
            else { fg = "SuccessFg"; bg = "SuccessBg"; dot = "SuccessDot"; text = "Gespeichert"; }
            _saveText.Text = text;
            _saveText.SetResourceReference(TextBlock.ForegroundProperty, fg);
            _saveBadge.SetResourceReference(Border.BackgroundProperty, bg);
            _saveDot.SetResourceReference(Shape.FillProperty, dot);

            _levelText.Text = KindLabel(CaretBlockKind());
            UpdateDictationButton();
        }

        void UpdateDictationButton()
        {
            if (_dictButton == null) return;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (_recorder != null)
            {
                TimeSpan d = _recorder.Duration;
                string state = "rec";
                if (_dictState != state)
                {
                    _dictState = state;
                    _dictButton.SetResourceReference(BackgroundProperty, "DangerBg");
                    _dictButton.SetResourceReference(BorderBrushProperty, "DangerBorder");
                    _dictButton.SetResourceReference(ForegroundProperty, "DangerFg");
                    _dictButton.ToolTip = "Diktat beenden (Strg+Umschalt+M) · Esc verwirft";
                    AutomationName(_dictButton, "Diktat beenden");
                }
                var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                dot.SetResourceReference(Shape.FillProperty, "DangerDot");
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = (int)d.TotalMinutes + ":" + d.Seconds.ToString("00"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                int level = _recorder.Level; // 0–100
                var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 16, Margin = new Thickness(0, 0, 8, 0) };
                for (int i = 0; i < 5; i++)
                {
                    double h = level > i * 20 ? 6 + 10 * Math.Min(1.0, (level - i * 20) / 20.0) : 3;
                    var r = new Border { Width = 2, Height = h, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
                    r.SetResourceReference(Border.BackgroundProperty, "DangerFg");
                    bars.Children.Add(r);
                }
                row.Children.Add(bars);
                row.Children.Add(new TextBlock { Text = "Beenden", VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                string state = _dictationCts != null ? "busy" : "idle";
                if (_dictState != state)
                {
                    _dictState = state;
                    _dictButton.SetResourceReference(BackgroundProperty, "SurfaceBg");
                    _dictButton.SetResourceReference(BorderBrushProperty, "InputBorder");
                    _dictButton.SetResourceReference(ForegroundProperty, "ChromeFg");
                    _dictButton.ToolTip = state == "busy" ? "Wird transkribiert · Esc bricht ab" : "Diktat starten (Strg+Umschalt+M)";
                    AutomationName(_dictButton, state == "busy" ? "Diktat wird transkribiert" : "Diktat starten");
                }
                FrameworkElement mic = Icons.Make(Icons.Mic);
                mic.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(mic);
                row.Children.Add(new TextBlock { Text = state == "busy" ? "Transkribiere …" : "Diktat", VerticalAlignment = VerticalAlignment.Center });
                if (state == "idle")
                {
                    FrameworkElement k = Kbd("Strg ⇧ M");
                    k.Margin = new Thickness(8, 0, 0, 0);
                    row.Children.Add(k);
                }
            }
            _dictButton.Content = row;
        }

        // ================================================================ Seitenleiste

        FrameworkElement BuildSideTabs()
        {
            Border shell = SegmentShell();
            shell.Margin = new Thickness(12, 12, 12, 10);
            var grid = new UniformGrid { Rows = 1 };
            _sideTabOutline = Segment("Gliederung", "side", delegate { SetSideMode(SideMode.Outline); }, "Überschriften des Dokuments");
            _sideTabFiles = Segment("Dateien", "side", delegate { SetSideMode(SideMode.Files); }, "Zuletzt verwendete Dateien");
            _sideTabFolder = Segment("Ordner", "side", delegate { SetSideMode(SideMode.Folder); }, "Dateien im Ordner");
            foreach (RadioButton rb in new[] { _sideTabOutline, _sideTabFiles, _sideTabFolder })
            {
                rb.Padding = new Thickness(0);
                rb.Margin = new Thickness(0);
                grid.Children.Add(rb);
            }
            shell.Child = grid;
            return shell;
        }

        FrameworkElement BuildSideFooter()
        {
            var grid = new UniformGrid { Rows = 1, Margin = new Thickness(12, 8, 12, 12) };
            var plus = Icons.Make(Icons.Plus, 14);
            plus.Margin = new Thickness(0, 0, 6, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(plus);
            row.Children.Add(new TextBlock { Text = "Neu", VerticalAlignment = VerticalAlignment.Center });
            var neu = new Button { Content = row, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Neues Dokument (Strg+N)" };
            neu.SetResourceReference(StyleProperty, "Primary");
            neu.Click += delegate { Execute(_cNew); };
            var open = new Button { Content = "Öffnen …", Margin = new Thickness(4, 0, 0, 0), ToolTip = "Datei öffnen (Strg+O)" };
            open.Click += delegate { Execute(_cOpen); };
            grid.Children.Add(neu);
            grid.Children.Add(open);
            return grid;
        }

        /// <summary>Listeneintrag mit Symbol, Titel und optionaler zweiter Zeile.</summary>
        static FrameworkElement SideRow(string icon, string title, string detail, bool strong)
        {
            var dock = new DockPanel();
            if (icon != null)
            {
                FrameworkElement ic = Icons.Make(icon, 15);
                ic.Margin = new Thickness(0, detail != null ? 2 : 0, 10, 0);
                ic.VerticalAlignment = detail != null ? VerticalAlignment.Top : VerticalAlignment.Center;
                ic.SetResourceReference(TextElement.ForegroundProperty, strong ? "ChromeFg" : "MutedFg");
                DockPanel.SetDock(ic, Dock.Left);
                dock.Children.Add(ic);
            }
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis });
            if (detail != null)
            {
                var d = new TextBlock { Text = detail, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
                d.SetResourceReference(TextBlock.ForegroundProperty, "MutedFg");
                stack.Children.Add(d);
            }
            dock.Children.Add(stack);
            return dock;
        }

        /// <summary>Gliederungseintrag: Ebene (H1…H6) gedimmt in Monospace, dann der Titel.</summary>
        static FrameworkElement OutlineRow(int level, string text)
        {
            var dock = new DockPanel();
            var tag = new TextBlock { Text = "H" + level, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11, Width = 24, VerticalAlignment = VerticalAlignment.Center };
            tag.SetResourceReference(TextBlock.ForegroundProperty, "MutedFg");
            DockPanel.SetDock(tag, Dock.Left);
            dock.Children.Add(tag);
            dock.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = level == 1 ? FontWeights.SemiBold : FontWeights.Normal });
            return dock;
        }

        static TextBlock SectionLabel(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedFg");
            return t;
        }

        // ================================================================ Bereichsköpfe & Lesebreite

        Border PaneHeader(string text, string badge)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedFg");
            row.Children.Add(t);
            if (badge != null)
            {
                var bt = new TextBlock { Text = badge, FontSize = 11, FontWeight = FontWeights.Medium };
                bt.SetResourceReference(TextBlock.ForegroundProperty, "MutedFg");
                var b = new Border { Child = bt, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                b.SetResourceReference(Border.BackgroundProperty, "Subtle");
                row.Children.Add(b);
            }
            var header = new Border { Child = row, Height = 34, Padding = new Thickness(28, 0, 16, 0) };
            header.SetResourceReference(Border.BackgroundProperty, "EditorBg");
            DockPanel.SetDock(header, Dock.Top);
            return header;
        }

        /// <summary>
        /// In der Einzelansicht wird der Text auf eine ruhige Lesebreite (≈ 760 px) zentriert,
        /// in der geteilten Ansicht bleibt der normale Rand.
        /// </summary>
        void ApplyReadingWidth()
        {
            bool single = _viewMode != ViewMode.Both;
            if (_preview.Document != null)
            {
                double side = single ? Math.Max(28, (_preview.ActualWidth - ReadingWidth) / 2) : 28;
                _preview.Document.PagePadding = new Thickness(side, 20, side, 48);
            }
            if (_editor.Document != null)
            {
                double side = single ? Math.Max(28, (_editor.ActualWidth - ReadingWidth) / 2) : 28;
                _editor.Document.PagePadding = new Thickness(side, 18, side, 48);
            }
            Visibility hv = single ? Visibility.Collapsed : Visibility.Visible;
            if (_editorHeader != null) _editorHeader.Visibility = hv;
            if (_previewHeader != null) _previewHeader.Visibility = hv;
        }

        // ================================================================ Statusleiste

        Border BuildStatusBar()
        {
            var dock = new DockPanel { Margin = new Thickness(14, 0, 8, 0), Height = 30 };
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _statusCaret = StatusText();
            _statusWords = StatusText();
            var themeRow = new StackPanel { Orientation = Orientation.Horizontal };
            var themeName = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            themeRow.Children.Add(themeName);
            FrameworkElement chev = Icons.Make(Icons.ChevronDown, 12);
            chev.Margin = new Thickness(4, 0, 0, 0);
            themeRow.Children.Add(chev);
            _themeButton = new Button { Content = themeRow, FontSize = 12, Padding = new Thickness(6, 0, 6, 0), MinHeight = 22, Height = 22, ToolTip = "Theme wählen" };
            _themeButton.SetResourceReference(StyleProperty, "Ghost");
            _themeButton.SetResourceReference(ForegroundProperty, "MutedFg");
            _themeButton.Tag = themeName;
            _themeButton.Click += delegate { ShowThemeMenu(); };

            right.Children.Add(_statusCaret);
            right.Children.Add(StatusSep());
            right.Children.Add(_statusWords);
            right.Children.Add(StatusSep());
            right.Children.Add(_themeButton);
            right.Children.Add(StatusSep());
            right.Children.Add(StatusText("UTF-8"));
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);

            _statusLeft.FontSize = 12;
            _statusLeft.VerticalAlignment = VerticalAlignment.Center;
            _statusLeft.TextTrimming = TextTrimming.CharacterEllipsis;
            _statusLeft.SetResourceReference(TextBlock.ForegroundProperty, "MutedFg");
            dock.Children.Add(_statusLeft);

            var border = new Border { Child = dock, BorderThickness = new Thickness(0, 1, 0, 0) };
            border.SetResourceReference(Border.BackgroundProperty, "ChromeBg");
            border.SetResourceReference(Border.BorderBrushProperty, "Hairline");
            return border;
        }

        static TextBlock StatusText(string text = "")
        {
            var t = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedFg");
            return t;
        }

        static FrameworkElement StatusSep()
        {
            var r = new Border { Width = 1, Height = 12, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            r.SetResourceReference(Border.BackgroundProperty, "Hairline");
            return r;
        }

        void UpdateStatusRight(string where)
        {
            if (_statusCaret == null) return;
            _statusCaret.Text = where;
            _statusWords.Text = _wordCount == 1 ? "1 Wort" : _wordCount + " Wörter";
            ((TextBlock)_themeButton.Tag).Text = ThemeManager.Current != null ? ThemeManager.Current.Name : "";
        }

        // ================================================================ Kontextmenü: Schrift als Symbolreihe

        MenuItem BuildFontRow(ContextMenu menu)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            AddFontButton(row, menu, Icons.Bold, _cBold);
            AddFontButton(row, menu, Icons.Italic, _cItalic);
            AddFontButton(row, menu, Icons.Underline, _cUnderline);
            AddFontButton(row, menu, Icons.Strike, _cStrike);
            AddFontButton(row, menu, Icons.Code, _cCode);
            AddFontButton(row, menu, Icons.ClearFormat, _cClear);
            var item = new MenuItem { Header = row };
            item.SetResourceReference(StyleProperty, "InlineRow");
            return item;
        }

        void AddFontButton(Panel row, ContextMenu menu, string icon, Cmd c)
        {
            Button b = ToolIcon(icon, c);
            b.Click += delegate { menu.IsOpen = false; };
            row.Children.Add(b);
        }
    }
}
