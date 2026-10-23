using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using MarkdownEditor.Dialogs;
using MarkdownEditor.Markdown;

namespace MarkdownEditor.Editor
{
    /// <summary>
    /// Formatierungsaktionen für Kontextmenü, Menüleiste und Tastenkürzel.
    /// Jede Aktion fügt ausschließlich Markdown-Syntax ein (bzw. entfernt sie beim erneuten Anwenden).
    /// </summary>
    public static class Formatter
    {
        const string Placeholder = "Text";

        // ---------------------------------------------------------------- Überschriften / Standardtext

        /// <summary>level 1–6 = Überschrift, 0 = Standardtext (entfernt Überschrift/Liste/Zitat).</summary>
        public static void SetHeading(MarkdownEditorBox ed, int level)
        {
            TextPos a, b;
            ed.GetSelection(out a, out b);
            List<string> lines = ed.GetLines();
            LineInfo[] infos = ed.LineInfos;

            // Gleiche Ebene erneut gewählt → zurück zu Standardtext
            if (level > 0 && a.Line == b.Line && Info(infos, a.Line).Kind == LineKind.Heading && Info(infos, a.Line).Level == level)
                level = 0;

            var result = new List<string>();
            int caretDelta = 0;
            for (int i = a.Line; i <= b.Line; i++)
            {
                string s = lines[i];
                LineInfo info = Info(infos, i);
                if (info.IsCode || info.IsTable || (s.Trim().Length == 0 && a.Line != b.Line))
                {
                    result.Add(s);
                    continue;
                }
                bool stripBlocks = level == 0 || info.Kind == LineKind.Heading;
                string body = stripBlocks && info.Kind != LineKind.Normal && info.Kind != LineKind.Rule
                    ? s.Substring(Math.Min(info.PrefixLength, s.Length))
                    : s;
                string n = level > 0 ? new string('#', level) + " " + body : body;
                if (i == a.Line) caretDelta = n.Length - s.Length;
                result.Add(n);
            }
            ed.ReplaceLines(a.Line, b.Line, result);
            if (a.Line == b.Line)
                ed.SetCaret(new TextPos(a.Line, Math.Max(0, a.Col + caretDelta)));
        }

        // ---------------------------------------------------------------- Inline-Formatierungen

        public static void Bold(MarkdownEditorBox ed) { ToggleWrap(ed, "**"); }
        public static void Italic(MarkdownEditorBox ed) { ToggleWrap(ed, "*"); }
        public static void Underline(MarkdownEditorBox ed) { ToggleWrap(ed, "__"); }
        public static void Strike(MarkdownEditorBox ed) { ToggleWrap(ed, "~~"); }
        public static void InlineCode(MarkdownEditorBox ed) { ToggleWrap(ed, "`"); }

        /// <summary>
        /// Umschließt die Auswahl (oder das Wort am Cursor) mit <paramref name="marker"/>.
        /// Ist der Text bereits so formatiert, wird die Formatierung entfernt.
        /// </summary>
        public static void ToggleWrap(MarkdownEditorBox ed, string marker)
        {
            TextPos a, b;
            ed.GetSelection(out a, out b);
            if (a.Line != b.Line)
            {
                WrapLines(ed, a, b, marker);
                return;
            }

            string line = ed.LineText(a.Line);
            int s0 = a.Col, s1 = b.Col;
            int m = marker.Length;

            if (s0 == s1)
            {
                int ws = s0, we = s0;
                while (ws > 0 && IsWordChar(line[ws - 1])) ws--;
                while (we < line.Length && IsWordChar(line[we])) we++;
                if (ws == we)
                {
                    // Kein Wort am Cursor → Platzhalter einfügen und markieren
                    ed.Replace(a, a, marker + Placeholder + marker);
                    ed.Select(new TextPos(a.Line, a.Col + m), new TextPos(a.Line, a.Col + m + Placeholder.Length));
                    return;
                }
                s0 = ws;
                s1 = we;
            }
            while (s0 < s1 && char.IsWhiteSpace(line[s0])) s0++;
            while (s1 > s0 && char.IsWhiteSpace(line[s1 - 1])) s1--;

            // Bereits formatiert? Markerzeichen direkt vor/nach der Auswahl zählen.
            char mc = marker[0];
            int before = 0, after = 0;
            while (s0 - before - 1 >= 0 && line[s0 - before - 1] == mc) before++;
            while (s1 + after < line.Length && line[s1 + after] == mc) after++;
            int n = Math.Min(before, after);
            bool present = m == 1 ? (mc == '`' ? n >= 1 : n % 2 == 1) : n >= m;

            if (present)
            {
                string newLine = line.Substring(0, s0 - m) + line.Substring(s0, s1 - s0) + line.Substring(s1 + m);
                ed.ReplaceLines(a.Line, a.Line, new[] { newLine });
                ed.Select(new TextPos(a.Line, s0 - m), new TextPos(a.Line, s1 - m));
                return;
            }

            string inner = line.Substring(s0, s1 - s0);
            if (inner.Length >= 2 * m && inner.StartsWith(marker) && inner.EndsWith(marker))
            {
                string stripped = inner.Substring(m, inner.Length - 2 * m);
                ed.Replace(new TextPos(a.Line, s0), new TextPos(a.Line, s1), stripped);
                ed.Select(new TextPos(a.Line, s0), new TextPos(a.Line, s0 + stripped.Length));
                return;
            }

            ed.Replace(new TextPos(a.Line, s0), new TextPos(a.Line, s1), marker + inner + marker);
            ed.Select(new TextPos(a.Line, s0 + m), new TextPos(a.Line, s1 + m));
        }

        /// <summary>
        /// Entfernt Fett, Kursiv, Unterstrichen, Durchgestrichen und Inline-Code in der Auswahl
        /// (ohne Auswahl: in der ganzen Zeile). Links und Zeilenpräfixe bleiben erhalten.
        /// </summary>
        public static void ClearFormatting(MarkdownEditorBox ed)
        {
            TextPos a, b;
            ed.GetSelection(out a, out b);
            bool wholeLine = a.Line == b.Line && a.Col == b.Col;
            List<string> lines = ed.GetLines();
            LineInfo[] infos = ed.LineInfos;
            var result = new List<string>();
            int selStart = 0, selEnd = 0;
            for (int i = a.Line; i <= b.Line; i++)
            {
                string s = lines[i];
                LineInfo info = Info(infos, i);
                int prefix = Math.Min(info.PrefixLength, s.Length);
                int from = wholeLine || i != a.Line ? 0 : a.Col;
                int to = wholeLine || i != b.Line ? s.Length : Math.Min(b.Col, s.Length);
                from = Math.Max(from, prefix);
                to = Math.Max(from, to);
                // Markierungen direkt um die Auswahl mitnehmen („fett“ in **fett** markiert)
                while (from > prefix && "*_~`".IndexOf(s[from - 1]) >= 0) from--;
                while (to < s.Length && "*_~`".IndexOf(s[to]) >= 0) to++;
                string inner = info.IsCode || info.Kind == LineKind.Fence ? s.Substring(from, to - from) : StripInline(s.Substring(from, to - from));
                if (i == a.Line) selStart = from;
                if (i == b.Line) selEnd = from + inner.Length;
                result.Add(s.Substring(0, from) + inner + s.Substring(to));
            }
            ed.ReplaceLines(a.Line, b.Line, result);
            if (wholeLine) ed.SetCaret(new TextPos(a.Line, Math.Min(a.Col, result[0].Length)));
            else ed.Select(new TextPos(a.Line, selStart), new TextPos(b.Line, selEnd));
        }

        static string StripInline(string t)
        {
            t = Regex.Replace(t, @"(\*\*|__|~~)(?=\S)(.+?)(?<=\S)\1", "$2");
            t = Regex.Replace(t, @"`([^`]+)`", "$1");
            return Regex.Replace(t, @"(?<![\\\w*])([*_])(?=\S)(.+?)(?<=\S)\1(?![\w*])", "$2");
        }

        static void WrapLines(MarkdownEditorBox ed, TextPos a, TextPos b, string marker)
        {
            List<string> lines = ed.GetLines();
            LineInfo[] infos = ed.LineInfos;
            var result = new List<string>();
            for (int i = a.Line; i <= b.Line; i++)
            {
                string s = lines[i];
                LineInfo info = Info(infos, i);
                if (info.IsCode || info.IsTable || info.Kind == LineKind.Rule || s.Trim().Length == 0)
                {
                    result.Add(s);
                    continue;
                }
                int from = i == a.Line ? a.Col : 0;
                int to = i == b.Line ? b.Col : s.Length;
                from = Math.Max(from, Math.Min(info.PrefixLength, s.Length));
                to = Math.Max(from, Math.Min(to, s.Length));
                while (from < to && char.IsWhiteSpace(s[from])) from++;
                while (to > from && char.IsWhiteSpace(s[to - 1])) to--;
                if (to <= from)
                {
                    result.Add(s);
                    continue;
                }
                result.Add(s.Substring(0, from) + marker + s.Substring(from, to - from) + marker + s.Substring(to));
            }
            ed.ReplaceLines(a.Line, b.Line, result);
            ed.Select(new TextPos(a.Line, 0), new TextPos(b.Line, result[result.Count - 1].Length));
        }

        // ---------------------------------------------------------------- Blockelemente

        public static void InsertRule(MarkdownEditorBox ed)
        {
            InsertBlock(ed, new[] { "---" });
        }

        public static void InsertText(MarkdownEditorBox ed, string text)
        {
            ed.InsertText(text);
        }

        public static void ToggleBullet(MarkdownEditorBox ed) { ToggleLinePrefix(ed, LineKind.Bullet, false); }
        public static void ToggleTask(MarkdownEditorBox ed) { ToggleLinePrefix(ed, LineKind.Bullet, true); }
        public static void ToggleOrdered(MarkdownEditorBox ed) { ToggleLinePrefix(ed, LineKind.Ordered, false); }
        public static void ToggleQuote(MarkdownEditorBox ed) { ToggleLinePrefix(ed, LineKind.Quote, false); }

        static void ToggleLinePrefix(MarkdownEditorBox ed, LineKind kind, bool task)
        {
            TextPos a, b;
            ed.GetSelection(out a, out b);
            List<string> lines = ed.GetLines();
            LineInfo[] infos = ed.LineInfos;

            bool allHave = true;
            for (int i = a.Line; i <= b.Line; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                LineInfo info = Info(infos, i);
                bool isTask = lines[i].Substring(0, Math.Min(info.PrefixLength, lines[i].Length)).Contains("[");
                if (info.Kind != kind || (kind == LineKind.Bullet && isTask != task)) { allHave = false; break; }
            }

            var result = new List<string>();
            int number = 1;
            for (int i = a.Line; i <= b.Line; i++)
            {
                string s = lines[i];
                LineInfo info = Info(infos, i);
                if (info.IsCode || info.IsTable || (s.Trim().Length == 0 && a.Line != b.Line))
                {
                    result.Add(s);
                    continue;
                }
                string indent = "";
                string body = s;
                if (info.Kind == LineKind.Bullet || info.Kind == LineKind.Ordered || info.Kind == LineKind.Quote || info.Kind == LineKind.Heading)
                {
                    if (info.Kind != LineKind.Quote && info.Kind != LineKind.Heading) indent = new string(' ', info.Level);
                    body = s.Substring(Math.Min(info.PrefixLength, s.Length));
                }
                if (allHave)
                {
                    result.Add(indent + body);
                    continue;
                }
                string prefix = kind == LineKind.Quote ? "> "
                    : kind == LineKind.Ordered ? (number++) + ". "
                    : task ? "- [ ] " : "- ";
                result.Add(indent + prefix + body);
            }
            TextPos end = ed.ReplaceLines(a.Line, b.Line, result);
            ed.SetCaret(end);
        }

        static readonly Regex TaskBoxRx = new Regex(@"^\s*[-*+]\s+\[([ xX])\]");

        /// <summary>Hakt die Aufgaben in der Auswahl ab bzw. öffnet sie wieder (- [ ] ↔ - [x]).</summary>
        public static void ToggleTaskDone(MarkdownEditorBox ed)
        {
            TextPos a, b;
            ed.GetSelection(out a, out b);
            List<string> lines = ed.GetLines();
            var result = new List<string>();
            bool any = false;
            for (int i = a.Line; i <= b.Line; i++)
            {
                string s = lines[i];
                Match m = TaskBoxRx.Match(s);
                if (m.Success)
                {
                    int at = m.Groups[1].Index;
                    s = s.Substring(0, at) + (m.Groups[1].Value == " " ? "x" : " ") + s.Substring(at + 1);
                    any = true;
                }
                result.Add(s);
            }
            if (!any) return;
            ed.ReplaceLines(a.Line, b.Line, result);
            if (a.Line == b.Line && a.Col == b.Col) ed.SetCaret(a);
            else ed.Select(a, b);
        }

        public static void ToggleCodeBlock(MarkdownEditorBox ed)
        {
            TextPos a, b;
            ed.GetSelection(out a, out b);
            string line = ed.LineText(a.Line);
            if (a.Line == b.Line && line.Trim().Length == 0)
            {
                ed.ReplaceLines(a.Line, a.Line, new[] { "```", "", "```" });
                ed.SetCaret(new TextPos(a.Line + 1, 0));
                return;
            }
            List<string> lines = ed.GetLines();
            var result = new List<string> { "```" };
            for (int i = a.Line; i <= b.Line; i++) result.Add(lines[i]);
            result.Add("```");
            TextPos end = ed.ReplaceLines(a.Line, b.Line, result);
            ed.SetCaret(end);
        }

        public static void InsertLink(MarkdownEditorBox ed)
        {
            TextPos a, b;
            ed.GetSelection(out a, out b);
            if (a.Line != b.Line) b = new TextPos(a.Line, ed.LineText(a.Line).Length);
            string line = ed.LineText(a.Line);
            string sel = line.Substring(a.Col, Math.Max(0, Math.Min(b.Col, line.Length) - a.Col));

            if (sel.Length == 0)
            {
                ed.Replace(a, b, "[Linktext](https://)");
                ed.Select(new TextPos(a.Line, a.Col + 1), new TextPos(a.Line, a.Col + 9));
            }
            else if (Regex.IsMatch(sel, @"^(https?://|www\.)\S+$"))
            {
                ed.Replace(a, b, "[Linktext](" + sel + ")");
                ed.Select(new TextPos(a.Line, a.Col + 1), new TextPos(a.Line, a.Col + 9));
            }
            else
            {
                ed.Replace(a, b, "[" + sel + "](https://)");
                int urlStart = a.Col + sel.Length + 3;
                ed.Select(new TextPos(a.Line, urlStart), new TextPos(a.Line, urlStart + 8));
            }
        }

        /// <summary>
        /// Fügt ![alt](url) hinter der Auswahl ein. Die Auswahl selbst bleibt stehen – aus der Vorschau
        /// heraus umfasst sie den ganzen Block, das Bild landet dann an dessen Ende.
        /// </summary>
        public static void InsertImage(MarkdownEditorBox ed, string alt, string url)
        {
            TextPos a, b;
            ed.GetSelection(out a, out b);
            string line = ed.LineText(b.Line);
            int col = Math.Min(b.Col, line.Length);
            string md = "![" + alt.Replace("[", "").Replace("]", "") + "](" + url + ")";
            if (col > 0 && !char.IsWhiteSpace(line[col - 1])) md = " " + md;
            ed.Replace(new TextPos(b.Line, col), new TextPos(b.Line, col), md);
            ed.SetCaret(new TextPos(b.Line, col + md.Length));
        }

        // ---------------------------------------------------------------- Tabellen

        public static bool IsInTable(MarkdownEditorBox ed)
        {
            int s, e;
            return FindTable(ed, ed.CaretPos().Line, out s, out e);
        }

        static bool FindTable(MarkdownEditorBox ed, int line, out int start, out int end)
        {
            LineInfo[] infos = ed.LineInfos;
            start = end = line;
            if (!Info(infos, line).IsTable) return false;
            while (start > 0 && infos[start].Kind != LineKind.TableHeader && infos[start - 1].IsTable) start--;
            while (end + 1 < infos.Length && infos[end + 1].Kind != LineKind.TableHeader && infos[end + 1].IsTable) end++;
            return true;
        }

        /// <summary>Öffnet den Tabellen-Editor: bearbeitet die Tabelle am Cursor oder fügt eine neue ein.</summary>
        public static void InsertOrEditTable(MarkdownEditorBox ed, Window owner)
        {
            int start, end;
            bool editing = FindTable(ed, ed.CaretPos().Line, out start, out end);
            List<string> lines = ed.GetLines();
            MarkdownTable table = editing
                ? MarkdownTable.Parse(lines.GetRange(start, end - start + 1))
                : MarkdownTable.CreateDefault(3, 2);

            var dlg = new TableDialog(table, editing) { Owner = owner };
            if (dlg.ShowDialog() != true || dlg.Result == null) return;

            List<string> md = dlg.Result.ToMarkdownLines();
            if (editing)
            {
                TextPos endPos = ed.ReplaceLines(start, end, md);
                ed.SetCaret(endPos);
            }
            else
            {
                InsertBlock(ed, md);
            }
        }

        /// <summary>Zeile (0 = Kopfzeile) und Spalte der Tabellenzelle am Cursor.</summary>
        public static bool TableCellAt(MarkdownEditorBox ed, out int row, out int col)
        {
            TextPos caret = ed.CaretPos();
            int start, end;
            row = col = 0;
            if (!FindTable(ed, caret.Line, out start, out end)) return false;
            row = Math.Max(0, caret.Line - start - 1);   // Trennzeile zählt zur Kopfzeile
            string line = ed.LineText(caret.Line);
            int upto = Math.Min(caret.Col, line.Length);
            for (int i = 0; i < upto; i++)
            {
                if (line[i] == '\\') i++;
                else if (line[i] == '|') col++;
            }
            if (line.TrimStart().StartsWith("|") && col > 0) col--;
            return true;
        }

        /// <summary>
        /// Ändert die Tabelle am Cursor über das Tabellenmodell und schreibt sie ausgerichtet zurück.
        /// <paramref name="change"/> liefert die Zeile, in die der Cursor danach gesetzt wird.
        /// </summary>
        public static void ModifyTable(MarkdownEditorBox ed, Func<MarkdownTable, int> change)
        {
            int start, end;
            if (!FindTable(ed, ed.CaretPos().Line, out start, out end)) return;
            MarkdownTable table = MarkdownTable.Parse(ed.GetLines().GetRange(start, end - start + 1));
            int row = change(table);
            List<string> md = table.ToMarkdownLines();
            ed.ReplaceLines(start, end, md);
            int line = start + (row <= 0 ? 0 : row + 1);
            ed.SetCaret(new TextPos(Math.Min(line, start + md.Count - 1), 0));
        }

        // ---------------------------------------------------------------- Links

        static readonly Regex MdLinkRx = new Regex(@"(?<!!)\[([^\]]*)\]\(([^)\s]*)(\s+""[^""]*"")?\)");
        static readonly Regex BareUrlRx = new Regex(@"(https?://|www\.)[^\s<>()\[\]]+");

        /// <summary>Link am Cursor: [Text](Adresse) oder eine nackte Adresse (dann ist <paramref name="text"/> null).</summary>
        public static bool LinkAt(MarkdownEditorBox ed, out string text, out string url)
        {
            Match m = LinkMatchAt(ed);
            text = url = null;
            if (m == null) return false;
            bool md = m.Groups[2].Success && m.Value.StartsWith("[");
            text = md ? m.Groups[1].Value : null;
            url = md ? m.Groups[2].Value : m.Value;
            return true;
        }

        /// <summary>Ersetzt die Adresse des Links am Cursor.</summary>
        public static void SetLinkUrl(MarkdownEditorBox ed, string url)
        {
            Match m = LinkMatchAt(ed);
            if (m == null) return;
            int line = ed.CaretPos().Line;
            bool md = m.Value.StartsWith("[");
            int at = md ? m.Groups[2].Index : m.Index;
            int len = md ? m.Groups[2].Length : m.Length;
            ed.Replace(new TextPos(line, at), new TextPos(line, at + len), url);
            ed.SetCaret(new TextPos(line, at));
        }

        /// <summary>[Text](Adresse) → Text.</summary>
        public static void RemoveLink(MarkdownEditorBox ed)
        {
            Match m = LinkMatchAt(ed);
            if (m == null || !m.Value.StartsWith("[")) return;
            int line = ed.CaretPos().Line;
            ed.Replace(new TextPos(line, m.Index), new TextPos(line, m.Index + m.Length), m.Groups[1].Value);
            ed.SetCaret(new TextPos(line, m.Index + m.Groups[1].Length));
        }

        static Match LinkMatchAt(MarkdownEditorBox ed)
        {
            TextPos caret = ed.CaretPos();
            string line = ed.LineText(caret.Line);
            foreach (Match m in MdLinkRx.Matches(line))
                if (caret.Col >= m.Index && caret.Col <= m.Index + m.Length) return m;
            foreach (Match m in BareUrlRx.Matches(line))
                if (caret.Col >= m.Index && caret.Col <= m.Index + m.Length && !InsideMdLink(line, m.Index)) return m;
            return null;
        }

        static bool InsideMdLink(string line, int index)
        {
            foreach (Match m in MdLinkRx.Matches(line))
                if (index >= m.Index && index < m.Index + m.Length) return true;
            return index > 1 && line[index - 1] == '(' && line[index - 2] == ']';   // Bild-URL
        }

        /// <summary>Fügt einen Block (Tabelle, Trennlinie) unter der aktuellen Zeile ein – mit Leerzeilen davor/danach.</summary>
        static void InsertBlock(MarkdownEditorBox ed, IList<string> block)
        {
            TextPos a, b;
            ed.GetSelection(out a, out b);
            List<string> lines = ed.GetLines();
            string cur = lines[b.Line];
            bool curBlank = cur.Trim().Length == 0;

            var sb = new StringBuilder();
            if (!curBlank) sb.Append("\n\n");
            else if (b.Line > 0 && lines[b.Line - 1].Trim().Length > 0) sb.Append("\n");
            sb.Append(string.Join("\n", block));
            sb.Append("\n");

            TextPos at = curBlank ? new TextPos(b.Line, 0) : new TextPos(b.Line, cur.Length);
            ed.Replace(at, new TextPos(b.Line, cur.Length), sb.ToString());
        }

        // ---------------------------------------------------------------- Helfer

        static LineInfo Info(LineInfo[] infos, int i)
        {
            return i >= 0 && i < infos.Length ? infos[i] : new LineInfo(LineKind.Normal, 0, 0);
        }

        static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '-' || c == '\'';
        }
    }
}
