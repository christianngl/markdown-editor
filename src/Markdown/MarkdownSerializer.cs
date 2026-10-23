using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MarkdownEditor.Markdown
{
    /// <summary>
    /// Übersetzt die bearbeitbare Vorschau (FlowDocument) zurück in Markdown.
    /// <see cref="Assemble"/> übernimmt für unveränderte Blöcke den Original-Quelltext
    /// und serialisiert nur die tatsächlich bearbeiteten Blöcke neu.
    /// </summary>
    public static class MarkdownSerializer
    {
        const int FBold = 1, FItalic = 2, FUnderline = 4, FStrike = 8, FCode = 16;
        static readonly int[] FlagOrder = { FBold, FItalic, FUnderline, FStrike, FCode };
        static readonly Regex LineStartSyntax = new Regex(@"^(#{1,6}(\s|$)|>|[-+*]\s|\d+[.)]\s|(-\s*){3,}$|=+\s*$)");

        sealed class Seg
        {
            public string Text;
            public int Flags;
            public bool Raw;
        }

        public static string Join(IList<string> lines)
        {
            return string.Join("\n", lines);
        }

        // ================================================================ Gesamtes Dokument

        /// <summary>
        /// Baut den Markdown-Text aus der Vorschau neu auf. Unveränderte Blöcke (Serialisierung == Baseline)
        /// übernehmen ihre Originalzeilen inklusive der Leerzeilen dazwischen. Aktualisiert die Verknüpfungen.
        /// </summary>
        public static List<string> Assemble(FlowDocument doc, Dictionary<Block, BlockInfo> map, IList<string> source)
        {
            var output = new List<string>();
            bool first = true, prevUnchanged = false;
            int prevOldEnd = 0;

            foreach (Block b in doc.Blocks)
            {
                BlockInfo info;
                map.TryGetValue(b, out info);
                List<string> ser = SerializeBlock(b, map);
                string joined = Join(ser);
                bool unchanged = info != null && info.Start >= 0 && info.End <= source.Count && info.Baseline == joined;
                if (!unchanged && IsBlank(ser)) continue; // leerer Absatz → kein Markdown

                if (first)
                {
                    if (unchanged) CopyLines(source, 0, info.Start, output);
                }
                else if (unchanged && prevUnchanged && info.Start >= prevOldEnd && AllBlank(source, prevOldEnd, info.Start))
                {
                    CopyLines(source, prevOldEnd, info.Start, output);   // Original-Abstand beibehalten
                }
                else
                {
                    output.Add("");                                      // Blöcke durch Leerzeile trennen
                }

                int newStart = output.Count;
                int oldEnd = unchanged ? info.End : 0;
                if (unchanged) CopyLines(source, info.Start, info.End, output);
                else output.AddRange(ser);

                if (info == null)
                {
                    info = new BlockInfo { Kind = GuessKind(b) };
                    map[b] = info;
                }
                info.Start = newStart;
                info.End = output.Count;
                info.Baseline = joined;
                prevUnchanged = unchanged;
                prevOldEnd = oldEnd;
                first = false;
            }
            if (prevUnchanged) CopyLines(source, prevOldEnd, source.Count, output);
            if (output.Count == 0) output.Add("");
            return output;
        }

        static void CopyLines(IList<string> src, int from, int to, List<string> output)
        {
            for (int i = Math.Max(0, from); i < to && i < src.Count; i++) output.Add(src[i]);
        }

        static bool AllBlank(IList<string> src, int from, int to)
        {
            for (int i = from; i < to && i < src.Count; i++)
                if (src[i].Trim().Length > 0) return false;
            return true;
        }

        static bool IsBlank(List<string> lines)
        {
            foreach (string l in lines) if (l.Trim().Length > 0) return false;
            return true;
        }

        /// <summary>Typ eines Blocks ohne Verknüpfung (z. B. nach Rückgängig in der Vorschau) anhand der Formatierung.</summary>
        static BlockKind GuessKind(Block b)
        {
            if (b is List) return BlockKind.List;
            if (b is Table) return BlockKind.Table;
            if (b is Section) return BlockKind.Quote;
            if (b is BlockUIContainer) return BlockKind.Rule;
            var p = b as Paragraph;
            if (p != null && IsMono(p.FontFamily)) return BlockKind.Code;
            return BlockKind.Paragraph;
        }

        // ================================================================ Blöcke

        public static List<string> SerializeBlock(Block b, Dictionary<Block, BlockInfo> map)
        {
            BlockInfo info;
            map.TryGetValue(b, out info);
            var lines = new List<string>();

            var p = b as Paragraph;
            if (p != null)
            {
                BlockKind kind = info != null ? info.Kind : GuessKind(p);
                int level = info != null ? info.Level : 0;
                if (info == null && kind == BlockKind.Paragraph)
                {
                    double baseSize = p.Parent is FlowDocument ? ((FlowDocument)p.Parent).FontSize : p.FontSize;
                    level = PreviewRenderer.GuessHeadingLevel(p.FontSize, baseSize);
                    if (level > 0 && p.FontWeight.ToOpenTypeWeight() >= 600) kind = BlockKind.Heading;
                }

                if (kind == BlockKind.Code)
                {
                    lines.Add(info != null ? info.FenceOpen : "```");
                    lines.AddRange(PlainText(p.Inlines).Split('\n'));
                    lines.Add(info != null ? info.FenceClose : "```");
                    return lines;
                }
                string text = InlineMarkdown(p, false);
                if (kind == BlockKind.Heading)
                {
                    string h = text.Replace("  \n", " ").Trim();
                    lines.Add(new string('#', Math.Max(1, Math.Min(6, level))) + " " + h);
                    return lines;
                }
                foreach (string l in text.Split('\n'))
                    lines.Add(EscapeLineStart(l));
                if (IsBlank(lines)) lines.Clear();
                return lines;
            }

            if (b is BlockUIContainer)
            {
                lines.Add("---");
                return lines;
            }

            var list = b as List;
            if (list != null)
            {
                SerializeList(list, 0, lines);
                return lines;
            }

            var table = b as Table;
            if (table != null)
            {
                lines.AddRange(SerializeTable(table, info));
                return lines;
            }

            var section = b as Section;
            if (section != null)
            {
                bool firstChild = true;
                foreach (Block child in section.Blocks)
                {
                    List<string> inner = SerializeBlock(child, map);
                    if (IsBlank(inner)) continue;
                    if (!firstChild) lines.Add(">");
                    foreach (string l in inner) lines.Add(l.Length > 0 ? "> " + l : ">");
                    firstChild = false;
                }
                return lines;
            }

            // Unbekannter Blocktyp: reiner Text
            string raw = new TextRange(b.ContentStart, b.ContentEnd).Text.Replace("\r\n", "\n");
            lines.AddRange(raw.Split('\n'));
            return lines;
        }

        static void SerializeList(List list, int indent, List<string> output)
        {
            bool ordered = list.MarkerStyle != TextMarkerStyle.Disc && list.MarkerStyle != TextMarkerStyle.Circle
                && list.MarkerStyle != TextMarkerStyle.Square && list.MarkerStyle != TextMarkerStyle.Box && list.MarkerStyle != TextMarkerStyle.None;
            int number = Math.Max(1, list.StartIndex);
            foreach (ListItem item in list.ListItems)
            {
                bool firstPara = true;
                string marker = ordered ? number + ". " : "- ";
                foreach (Block child in item.Blocks)
                {
                    var p = child as Paragraph;
                    var sub = child as List;
                    if (sub != null)
                    {
                        SerializeList(sub, indent + marker.Length, output);
                        continue;
                    }
                    if (p == null) continue;
                    string text = InlineMarkdown(p, false);
                    if (text.StartsWith("☐ ")) text = "[ ] " + text.Substring(2);
                    else if (text.StartsWith("☑ ")) text = "[x] " + text.Substring(2);
                    if (text.Trim().Length == 0 && firstPara) continue;

                    int extra = (int)Math.Round(p.Margin.Left / 10.0);
                    string pad = new string(' ', indent + Math.Max(0, extra));
                    string[] parts = text.Split('\n');
                    for (int k = 0; k < parts.Length; k++)
                    {
                        if (k == 0 && firstPara) output.Add(pad + marker + parts[k]);
                        else output.Add(pad + new string(' ', marker.Length) + parts[k]);
                    }
                    firstPara = false;
                }
                if (!firstPara) number++;
            }
        }

        static List<string> SerializeTable(Table table, BlockInfo info)
        {
            var mt = new MarkdownTable();
            foreach (TableRowGroup group in table.RowGroups)
            {
                foreach (TableRow row in group.Rows)
                {
                    var cells = new List<string>();
                    foreach (TableCell cell in row.Cells)
                    {
                        var parts = new List<string>();
                        foreach (Block cb in cell.Blocks)
                        {
                            var p = cb as Paragraph;
                            string t = p != null ? InlineMarkdown(p, true) : new TextRange(cb.ContentStart, cb.ContentEnd).Text;
                            t = t.Replace("  \n", " ").Replace("\n", " ").Trim();
                            if (t.Length > 0) parts.Add(t);
                        }
                        cells.Add(string.Join(" ", parts));
                    }
                    mt.Rows.Add(cells);
                }
            }
            int cols = 1;
            foreach (List<string> r in mt.Rows) cols = Math.Max(cols, r.Count);
            for (int c = 0; c < cols; c++)
            {
                ColumnAlign a = ColumnAlign.None;
                if (info != null && info.Aligns != null && info.Aligns.Count == cols) a = info.Aligns[c];
                mt.Aligns.Add(a);
            }
            foreach (List<string> r in mt.Rows) while (r.Count < cols) r.Add("");
            return mt.ToMarkdownLines();
        }

        // ================================================================ Inline

        static string PlainText(InlineCollection inlines)
        {
            var sb = new StringBuilder();
            foreach (Inline inline in inlines)
            {
                var run = inline as Run;
                if (run != null) { sb.Append(run.Text); continue; }
                if (inline is LineBreak) { sb.Append('\n'); continue; }
                var iuc = inline as InlineUIContainer;
                if (iuc != null) { var tag = iuc.Tag as InlineTag; if (tag != null) sb.Append(tag.Markdown); continue; }
                var span = inline as Span;
                if (span != null) sb.Append(PlainText(span.Inlines));
            }
            return sb.ToString();
        }

        /// <summary>Inline-Inhalt eines Absatzes als Markdown; Zeilenumbrüche werden zu "  \n".</summary>
        static string InlineMarkdown(Paragraph p, bool inTable)
        {
            var segs = new List<Seg>();
            Collect(p.Inlines, p, segs);
            return Render(segs, inTable).TrimEnd(' ');
        }

        static void Collect(InlineCollection inlines, Paragraph p, List<Seg> segs)
        {
            foreach (Inline inline in inlines)
            {
                var run = inline as Run;
                if (run != null)
                {
                    if (run.Text.Length > 0) segs.Add(new Seg { Text = run.Text, Flags = FlagsOf(run, p) });
                    continue;
                }
                if (inline is LineBreak)
                {
                    segs.Add(new Seg { Text = "  \n", Raw = true });
                    continue;
                }
                var iuc = inline as InlineUIContainer;
                if (iuc != null)
                {
                    var tag = iuc.Tag as InlineTag;
                    if (tag != null) segs.Add(new Seg { Text = tag.Markdown, Raw = true });
                    continue;
                }
                var link = inline as Hyperlink;
                if (link != null)
                {
                    var inner = new List<Seg>();
                    Collect(link.Inlines, p, inner);
                    string url = link.Tag as string ?? (link.NavigateUri != null ? link.NavigateUri.OriginalString : "");
                    string text = Render(inner, false).Trim();
                    if (text.Length > 0) segs.Add(new Seg { Text = "[" + text + "](" + url + ")", Raw = true });
                    continue;
                }
                var span = inline as Span;
                if (span != null) Collect(span.Inlines, p, segs);
            }
        }

        static int FlagsOf(Run run, Paragraph p)
        {
            int f = 0;
            int w = run.FontWeight.ToOpenTypeWeight();
            if (w >= 600 && w > p.FontWeight.ToOpenTypeWeight()) f |= FBold;
            if (run.FontStyle == FontStyles.Italic && p.FontStyle != FontStyles.Italic) f |= FItalic;
            if (IsMono(run.FontFamily) && !IsMono(p.FontFamily)) f |= FCode;

            for (DependencyObject o = run; o != null && o != p; o = ((TextElement)o).Parent as TextElement)
            {
                var inl = o as Inline;
                if (inl == null || inl is Hyperlink || inl.TextDecorations == null) continue;
                foreach (TextDecoration d in inl.TextDecorations)
                {
                    if (d.Location == TextDecorationLocation.Underline) f |= FUnderline;
                    if (d.Location == TextDecorationLocation.Strikethrough) f |= FStrike;
                }
            }
            return f;
        }

        static bool IsMono(FontFamily family)
        {
            string s = family != null ? family.Source : "";
            return s.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Mono", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string Marker(int flag)
        {
            switch (flag)
            {
                case FBold: return "**";
                case FItalic: return "*";
                case FUnderline: return "__";
                case FStrike: return "~~";
                default: return "`";
            }
        }

        /// <summary>Setzt Markierungen an Formatierungswechseln; Leerzeichen bleiben außerhalb der Markierungen.</summary>
        static string Render(List<Seg> segs, bool inTable)
        {
            var sb = new StringBuilder();
            var stack = new List<int>();
            string pendingWs = "";

            foreach (Seg seg in segs)
            {
                string text;
                int flags;
                string lead = "", trail = "";
                if (seg.Raw)
                {
                    text = seg.Text;
                    flags = 0;
                }
                else
                {
                    bool code = (seg.Flags & FCode) != 0;
                    string t = code ? seg.Text : Escape(seg.Text);
                    if (t.Trim().Length == 0)
                    {
                        pendingWs += t;
                        continue;
                    }
                    int a = 0, z = t.Length;
                    while (a < z && t[a] == ' ') a++;
                    while (z > a && t[z - 1] == ' ') z--;
                    lead = t.Substring(0, a);
                    trail = t.Substring(z);
                    text = t.Substring(a, z - a);
                    flags = seg.Flags;
                }

                // Markierungen schließen, die nicht mehr gelten (inkl. aller darüber geöffneten)
                int k = stack.FindIndex(fl => (flags & fl) == 0);
                if (k >= 0)
                {
                    for (int j = stack.Count - 1; j >= k; j--) sb.Append(Marker(stack[j]));
                    stack.RemoveRange(k, stack.Count - k);
                }
                sb.Append(pendingWs);
                pendingWs = "";
                sb.Append(lead);
                foreach (int fl in FlagOrder)
                {
                    if ((flags & fl) == 0 || stack.Contains(fl)) continue;
                    sb.Append(Marker(fl));
                    stack.Add(fl);
                }
                sb.Append(text);
                pendingWs = trail;
            }
            for (int j = stack.Count - 1; j >= 0; j--) sb.Append(Marker(stack[j]));
            sb.Append(pendingWs);
            return sb.ToString();
        }

        static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 4);
            foreach (char c in s)
            {
                if (c == '\\' || c == '`' || c == '*' || c == '_' || c == '[' || c == ']' || c == '~') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Verhindert, dass normaler Text am Zeilenanfang als Markdown-Struktur gelesen wird (z. B. "# ").</summary>
        static string EscapeLineStart(string line)
        {
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || !LineStartSyntax.IsMatch(trimmed)) return line;
            int indent = line.Length - trimmed.Length;
            Match num = Regex.Match(trimmed, @"^(\d+)([.)])");
            if (num.Success) // "1. Text" → "1\. Text"
                return line.Substring(0, indent) + num.Groups[1].Value + "\\" + trimmed.Substring(num.Groups[1].Length);
            return line.Substring(0, indent) + "\\" + trimmed;
        }
    }
}
