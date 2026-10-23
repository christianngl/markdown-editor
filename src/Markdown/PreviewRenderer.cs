using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MarkdownEditor.Editor;

namespace MarkdownEditor.Markdown
{
    /// <summary>
    /// Rendert Markdown in ein bearbeitbares FlowDocument (echte Tabellen, Listen, farbige Emojis, Bilder).
    /// Jeder Block wird mit seinen Quellzeilen verknüpft (<see cref="BlockInfo"/>), damit Änderungen in der
    /// Vorschau gezielt zurück ins Markdown übertragen werden können. Es wird kein HTML erzeugt.
    /// </summary>
    public static class PreviewRenderer
    {
        static readonly double[] HeadingScale = { 1.0, 1.9, 1.55, 1.3, 1.15, 1.05, 1.0 };
        public static readonly FontFamily TextFont = new FontFamily("Segoe UI, Segoe UI Emoji, Segoe UI Symbol");
        public static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas");

        public static RenderResult Render(IList<string> lines, PreviewStyle s)
        {
            var doc = new FlowDocument
            {
                FontFamily = TextFont,
                FontSize = s.FontSize,
                Foreground = s.Theme.Text,
                Background = s.Theme.EditorBg,
                PagePadding = new Thickness(28, 20, 28, 48),
                TextAlignment = TextAlignment.Left
            };
            var map = new Dictionary<Block, BlockInfo>();
            RenderBlocks(doc.Blocks, lines, 0, s, map, true);
            if (doc.Blocks.Count == 0)
            {
                var empty = new Paragraph();
                StyleParagraph(empty, BlockKind.Paragraph, 0, s);
                doc.Blocks.Add(empty);
            }
            foreach (Block b in doc.Blocks)
            {
                BlockInfo info;
                if (map.TryGetValue(b, out info))
                    info.Baseline = MarkdownSerializer.Join(MarkdownSerializer.SerializeBlock(b, map));
            }
            return new RenderResult { Document = doc, Map = map, SourceLines = new List<string>(lines) };
        }

        /// <summary>Darstellung eines Absatzes je nach Typ (auch für Formatierungen direkt in der Vorschau).</summary>
        public static void StyleParagraph(Paragraph p, BlockKind kind, int level, PreviewStyle s)
        {
            p.ClearValue(TextElement.FontFamilyProperty);
            p.ClearValue(Block.BorderBrushProperty);
            p.ClearValue(Block.BorderThicknessProperty);
            p.ClearValue(Block.PaddingProperty);
            p.ClearValue(TextElement.BackgroundProperty);
            p.FontSize = s.FontSize;
            p.FontWeight = FontWeights.Normal;
            p.Foreground = s.Theme.Text;
            p.Margin = new Thickness(0, 0, 0, 12);

            if (kind == BlockKind.Heading)
            {
                p.FontSize = s.FontSize * HeadingScale[Math.Max(1, Math.Min(6, level))];
                p.FontWeight = FontWeights.SemiBold;
                p.Foreground = s.Theme.Heading;
                p.Margin = new Thickness(0, s.FontSize, 0, s.FontSize * 0.5);
                if (level <= 2)
                {
                    p.BorderBrush = s.Theme.Border;
                    p.BorderThickness = new Thickness(0, 0, 0, 1);
                    p.Padding = new Thickness(0, 0, 0, 4);
                }
            }
            else if (kind == BlockKind.Code)
            {
                p.FontFamily = MonoFont;
                p.FontSize = s.FontSize * 0.9;
                p.Background = s.Theme.CodeBg;
                p.Foreground = s.Theme.CodeText;
                p.Padding = new Thickness(12, 8, 12, 8);
                p.Margin = new Thickness(0, 4, 0, 12);
            }
        }

        /// <summary>Schätzt die Überschriftenebene anhand der Schriftgröße (für Absätze ohne Verknüpfung).</summary>
        public static int GuessHeadingLevel(double fontSize, double baseSize)
        {
            for (int level = 1; level <= 5; level++)
                if (fontSize >= baseSize * (HeadingScale[level] + HeadingScale[level + 1]) / 2) return level;
            return 0;
        }

        static void RenderBlocks(BlockCollection target, IList<string> lines, int offset, PreviewStyle s, Dictionary<Block, BlockInfo> map, bool topLevel)
        {
            LineInfo[] infos = MarkdownSyntax.Classify(lines);
            int i = 0;
            while (i < lines.Count)
            {
                LineInfo info = infos[i];
                string line = lines[i];
                int start = i;
                Block block;
                var bi = new BlockInfo();

                switch (info.Kind)
                {
                    case LineKind.Blank:
                        i++;
                        continue;

                    case LineKind.Heading:
                    {
                        var p = new Paragraph();
                        bi.Kind = BlockKind.Heading;
                        bi.Level = info.Level;
                        StyleParagraph(p, BlockKind.Heading, info.Level, s);
                        string body = line.Substring(Math.Min(info.PrefixLength, line.Length)).TrimEnd();
                        body = body.TrimEnd('#').TrimEnd();
                        AddInlines(p.Inlines, body, s);
                        block = p;
                        i++;
                        break;
                    }

                    case LineKind.Rule:
                    {
                        var line1 = new Border { Height = 1, Background = s.Theme.Border, Margin = new Thickness(0, 8, 0, 14) };
                        block = new BlockUIContainer(line1);
                        bi.Kind = BlockKind.Rule;
                        i++;
                        break;
                    }

                    case LineKind.Fence:
                    case LineKind.Code:
                    {
                        var p = new Paragraph();
                        bi.Kind = BlockKind.Code;
                        StyleParagraph(p, BlockKind.Code, 0, s);
                        if (info.Kind == LineKind.Fence)
                        {
                            bi.FenceOpen = line.Trim();
                            i++;
                        }
                        bool first = true;
                        while (i < lines.Count && infos[i].Kind == LineKind.Code)
                        {
                            if (!first) p.Inlines.Add(new LineBreak());
                            p.Inlines.Add(new Run(lines[i]));
                            first = false;
                            i++;
                        }
                        if (i < lines.Count && infos[i].Kind == LineKind.Fence)
                        {
                            bi.FenceClose = lines[i].Trim();
                            i++;
                        }
                        else if (bi.FenceOpen.StartsWith("~"))
                        {
                            bi.FenceClose = "~~~";
                        }
                        block = p;
                        break;
                    }

                    case LineKind.TableHeader:
                    {
                        i++;
                        while (i < lines.Count && (infos[i].Kind == LineKind.TableSeparator || infos[i].Kind == LineKind.TableRow)) i++;
                        var tableLines = new List<string>();
                        for (int k = start; k < i; k++) tableLines.Add(lines[k]);
                        MarkdownTable mt = MarkdownTable.Parse(tableLines);
                        block = BuildTable(mt, s);
                        bi.Kind = BlockKind.Table;
                        bi.Aligns = new List<ColumnAlign>(mt.Aligns);
                        break;
                    }

                    case LineKind.Quote:
                    {
                        var inner = new List<string>();
                        while (i < lines.Count && infos[i].Kind == LineKind.Quote)
                        {
                            inner.Add(lines[i].Substring(Math.Min(infos[i].PrefixLength, lines[i].Length)));
                            i++;
                        }
                        var section = new Section
                        {
                            BorderBrush = s.Theme.Accent,
                            BorderThickness = new Thickness(3, 0, 0, 0),
                            Padding = new Thickness(12, 2, 0, 2),
                            Margin = new Thickness(0, 4, 0, 12),
                            Foreground = s.Theme.Quote
                        };
                        RenderBlocks(section.Blocks, inner, -1, s, map, false);
                        if (section.Blocks.Count == 0) section.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
                        block = section;
                        bi.Kind = BlockKind.Quote;
                        break;
                    }

                    case LineKind.Bullet:
                    case LineKind.Ordered:
                    {
                        var list = new List
                        {
                            MarkerStyle = info.Kind == LineKind.Ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                            Margin = new Thickness(0, 2, 0, 12),
                            Padding = new Thickness(26, 0, 0, 0)
                        };
                        if (info.Kind == LineKind.Ordered)
                        {
                            int n;
                            if (int.TryParse(line.Trim().Split('.', ')')[0], out n) && n > 0) list.StartIndex = n;
                        }
                        LineKind kind = info.Kind;
                        while (i < lines.Count && infos[i].Kind == kind)
                        {
                            string ls = lines[i];
                            string prefix = ls.Substring(0, Math.Min(infos[i].PrefixLength, ls.Length));
                            string body = ls.Substring(prefix.Length);
                            if (prefix.Contains("[ ]")) body = "☐ " + body;
                            else if (prefix.Contains("[x]") || prefix.Contains("[X]")) body = "☑ " + body;
                            var p = new Paragraph { Margin = new Thickness(infos[i].Level * 10, 1, 0, 1) };
                            AddInlines(p.Inlines, body, s);
                            list.ListItems.Add(new ListItem(p));
                            i++;
                        }
                        block = list;
                        bi.Kind = BlockKind.List;
                        break;
                    }

                    default:
                    {
                        // Absatz: aufeinanderfolgende Textzeilen; "  " oder "\" am Zeilenende = Zeilenumbruch
                        var p = new Paragraph();
                        StyleParagraph(p, BlockKind.Paragraph, 0, s);
                        bool first = true;
                        string prev = null;
                        while (i < lines.Count && (first || infos[i].Kind == LineKind.Normal))
                        {
                            string ls = lines[i];
                            if (!first)
                            {
                                if (prev.EndsWith("  ") || prev.EndsWith("\\")) p.Inlines.Add(new LineBreak());
                                else p.Inlines.Add(new Run(" "));
                            }
                            string text = ls.Trim();
                            if (text.EndsWith("\\")) text = text.Substring(0, text.Length - 1);
                            AddInlines(p.Inlines, text, s);
                            prev = ls;
                            first = false;
                            i++;
                        }
                        block = p;
                        bi.Kind = BlockKind.Paragraph;
                        break;
                    }
                }

                if (topLevel && offset >= 0)
                {
                    bi.Start = offset + start;
                    bi.End = offset + i;
                }
                map[block] = bi;
                target.Add(block);
            }
        }

        static Table BuildTable(MarkdownTable mt, PreviewStyle s)
        {
            var table = new Table
            {
                CellSpacing = 0,
                Margin = new Thickness(0, 4, 0, 14),
                BorderBrush = s.Theme.Border,
                BorderThickness = new Thickness(1, 1, 0, 0)
            };
            for (int col = 0; col < mt.ColumnCount; col++)
                table.Columns.Add(new TableColumn());

            var group = new TableRowGroup();
            table.RowGroups.Add(group);
            for (int r = 0; r < mt.Rows.Count; r++)
            {
                var row = new TableRow();
                if (r == 0)
                {
                    row.Background = s.Theme.CodeBg;
                    row.FontWeight = FontWeights.SemiBold;
                }
                for (int col = 0; col < mt.ColumnCount; col++)
                {
                    var p = new Paragraph { Margin = new Thickness(0), TextAlignment = ToTextAlignment(mt.Aligns[col]) };
                    AddInlines(p.Inlines, mt.Rows[r][col], s);
                    row.Cells.Add(new TableCell(p)
                    {
                        BorderBrush = s.Theme.Border,
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding = new Thickness(8, 4, 8, 4)
                    });
                }
                group.Rows.Add(row);
            }
            return table;
        }

        static TextAlignment ToTextAlignment(ColumnAlign a)
        {
            switch (a)
            {
                case ColumnAlign.Center: return TextAlignment.Center;
                case ColumnAlign.Right: return TextAlignment.Right;
                default: return TextAlignment.Left;
            }
        }

        static void AddInlines(InlineCollection target, string s, PreviewStyle st)
        {
            foreach (InlineSegment seg in MarkdownSyntax.ParseInline(s))
            {
                if (seg.Has(InlineStyle.Marker)) continue;

                if (seg.Has(InlineStyle.Image))
                {
                    Image img = TryLoadImage(seg.Url, st);
                    if (img != null)
                    {
                        target.Add(new InlineUIContainer(img) { Tag = new InlineTag("![" + seg.Text + "](" + seg.Url + ")") });
                        continue;
                    }
                }

                InlineCollection into = target;
                if (seg.Has(InlineStyle.Link) && !string.IsNullOrEmpty(seg.Url))
                {
                    var link = target.LastInline as Hyperlink;
                    if (link == null || (link.Tag as string) != seg.Url)
                    {
                        link = new Hyperlink();
                        AttachLink(link, seg.Url, st);
                        target.Add(link);
                    }
                    into = link.Inlines;
                }

                if (seg.Has(InlineStyle.Code))
                {
                    into.Add(StyledRun(seg.Text, seg, st));
                    continue;
                }

                // Text mit Emojis: Emojis als farbiges Bild, Rest als Text
                string text = seg.Text;
                int plainStart = 0;
                for (int k = 0; k < text.Length; k++)
                {
                    int len;
                    Emoji e = EmojiCatalog.MatchAt(text, k, out len);
                    if (e == null || e.Image == null) continue;
                    if (k > plainStart) into.Add(StyledRun(text.Substring(plainStart, k - plainStart), seg, st));
                    into.Add(CreateEmojiInline(e, text.Substring(k, len), st));
                    k += len - 1;
                    plainStart = k + 1;
                }
                if (plainStart < text.Length) into.Add(StyledRun(text.Substring(plainStart), seg, st));
            }
        }

        static Run StyledRun(string text, InlineSegment seg, PreviewStyle st)
        {
            var r = new Run(text);
            if (seg.Has(InlineStyle.Bold)) r.FontWeight = FontWeights.Bold;
            if (seg.Has(InlineStyle.Italic)) r.FontStyle = FontStyles.Italic;
            var deco = new TextDecorationCollection();
            if (seg.Has(InlineStyle.Underline))
                foreach (TextDecoration d in TextDecorations.Underline) deco.Add(d);
            if (seg.Has(InlineStyle.Strike))
                foreach (TextDecoration d in TextDecorations.Strikethrough) deco.Add(d);
            if (deco.Count > 0) r.TextDecorations = deco;
            if (seg.Has(InlineStyle.Code))
            {
                r.FontFamily = MonoFont;
                r.Background = st.Theme.CodeBg;
                r.Foreground = st.Theme.CodeText;
            }
            return r;
        }

        /// <summary>Farbiges Emoji als nicht editierbares Inline-Element; serialisiert wird das Unicode-Zeichen.</summary>
        public static InlineUIContainer CreateEmojiInline(Emoji e, string symbol, PreviewStyle st)
        {
            return new InlineUIContainer(CreateEmojiImage(e, st)) { Tag = new InlineTag(symbol), BaselineAlignment = BaselineAlignment.Center };
        }

        public static Image CreateEmojiImage(Emoji e, PreviewStyle st)
        {
            double size = Math.Round(st.FontSize * 1.25);
            var img = new Image { Source = e.Image, Width = size, Height = size, Margin = new Thickness(1, 0, 1, 0), ToolTip = e.Name };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        public static void AttachLink(Hyperlink link, string url, PreviewStyle st)
        {
            link.Tag = url;
            link.Foreground = st.Theme.Link;
            link.ToolTip = url + "  (Strg+Klick zum Öffnen)";
            string baseDir = st.BaseDir;
            link.Click += delegate
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) OpenUrl(link.Tag as string, baseDir);
            };
        }

        static string FirstToken(string url)
        {
            string u = (url ?? "").Trim();
            int space = u.IndexOf(' ');
            return space > 0 ? u.Substring(0, space) : u;
        }

        static string ResolveLocal(string url, string baseDir)
        {
            string u = Uri.UnescapeDataString(FirstToken(url)).Replace('/', '\\');
            if (Path.IsPathRooted(u)) return u;
            return baseDir != null ? Path.GetFullPath(Path.Combine(baseDir, u)) : null;
        }

        static Image TryLoadImage(string url, PreviewStyle st)
        {
            try
            {
                string u = FirstToken(url);
                Uri uri;
                if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    uri = new Uri(u);
                }
                else
                {
                    string path = ResolveLocal(u, st.BaseDir);
                    if (path == null || !File.Exists(path)) return null;
                    uri = new Uri(path);
                }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                return new Image { Source = bmp, MaxWidth = 640, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void OpenUrl(string url, string baseDir)
        {
            try
            {
                string u = FirstToken(url);
                if (u.Length == 0) return;
                if (u.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) u = "https://" + u;
                if (!u.Contains("://") && !u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    string local = ResolveLocal(u, baseDir);
                    if (local == null || !(File.Exists(local) || Directory.Exists(local))) return;
                    u = local;
                }
                Process.Start(new ProcessStartInfo(u) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Link konnte nicht geöffnet werden:\n" + ex.Message, "Markdown Editor");
            }
        }
    }
}
