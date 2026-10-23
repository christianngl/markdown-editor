using System;
using System.Collections.Generic;
using System.Text;

namespace MarkdownEditor.Markdown
{
    public enum ColumnAlign { None, Left, Center, Right }

    /// <summary>
    /// Tabellenmodell. Zeile 0 ist die Kopfzeile. Wird als Markdown-Fließtext gespeichert:
    /// <code>
    /// Spalte 1 | Spalte 2
    /// -------- | --------
    /// Wert 1   | Wert 2
    /// </code>
    /// </summary>
    public sealed class MarkdownTable
    {
        public readonly List<List<string>> Rows = new List<List<string>>();
        public readonly List<ColumnAlign> Aligns = new List<ColumnAlign>();

        public int ColumnCount { get { return Aligns.Count; } }

        public static MarkdownTable CreateDefault(int columns, int bodyRows)
        {
            var t = new MarkdownTable();
            var header = new List<string>();
            for (int c = 0; c < columns; c++)
            {
                header.Add("Spalte " + (c + 1));
                t.Aligns.Add(ColumnAlign.None);
            }
            t.Rows.Add(header);
            for (int r = 0; r < bodyRows; r++)
            {
                var row = new List<string>();
                for (int c = 0; c < columns; c++)
                    row.Add("Wert " + (c + 1));
                t.Rows.Add(row);
            }
            return t;
        }

        /// <summary>lines[0] = Kopfzeile, lines[1] = Trennzeile, Rest = Daten.</summary>
        public static MarkdownTable Parse(IList<string> lines)
        {
            var t = new MarkdownTable();
            if (lines.Count == 0) return CreateDefault(2, 1);

            List<string> header = SplitRow(lines[0]);
            List<string> sep = lines.Count > 1 ? SplitRow(lines[1]) : new List<string>();
            int cols = Math.Max(1, header.Count);
            for (int c = 0; c < cols; c++)
                t.Aligns.Add(c < sep.Count ? ParseAlign(sep[c]) : ColumnAlign.None);

            t.Rows.Add(Normalize(header, cols));
            for (int i = 2; i < lines.Count; i++)
                t.Rows.Add(Normalize(SplitRow(lines[i]), cols));
            return t;
        }

        public static List<string> SplitRow(string line)
        {
            var cells = new List<string>();
            string s = line.Trim();
            if (s.StartsWith("|")) s = s.Substring(1);
            if (s.EndsWith("|") && !s.EndsWith("\\|")) s = s.Substring(0, s.Length - 1);

            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\' && i + 1 < s.Length && s[i + 1] == '|') { sb.Append('|'); i++; }
                else if (ch == '|') { cells.Add(sb.ToString().Trim()); sb.Clear(); }
                else sb.Append(ch);
            }
            cells.Add(sb.ToString().Trim());
            return cells;
        }

        static ColumnAlign ParseAlign(string cell)
        {
            string c = cell.Trim();
            bool left = c.StartsWith(":"), right = c.EndsWith(":");
            if (left && right) return ColumnAlign.Center;
            if (right) return ColumnAlign.Right;
            if (left) return ColumnAlign.Left;
            return ColumnAlign.None;
        }

        static List<string> Normalize(List<string> row, int cols)
        {
            var r = new List<string>(cols);
            for (int c = 0; c < cols; c++)
                r.Add(c < row.Count ? row[c] : "");
            return r;
        }

        /// <summary>Erzeugt sauber ausgerichteten Markdown-Text (in Monospace als Raster lesbar).</summary>
        public List<string> ToMarkdownLines()
        {
            int cols = Math.Max(1, ColumnCount);
            var cells = new List<List<string>>();
            foreach (List<string> row in Rows)
            {
                var r = new List<string>();
                for (int c = 0; c < cols; c++)
                {
                    string v = c < row.Count && row[c] != null ? row[c] : "";
                    r.Add(v.Replace("\r", "").Replace("\n", " ").Replace("|", "\\|").Trim());
                }
                cells.Add(r);
            }
            if (cells.Count == 0) cells.Add(Normalize(new List<string>(), cols));

            var widths = new int[cols];
            for (int c = 0; c < cols; c++)
            {
                widths[c] = 3;
                foreach (List<string> r in cells)
                    widths[c] = Math.Max(widths[c], r[c].Length);
            }

            // Ohne äußere Pipes (wie gewünscht), außer wenn es sonst mehrdeutig wäre.
            bool outer = cols == 1;
            foreach (List<string> r in cells)
                if (r[0].Length == 0 || r[cols - 1].Length == 0) outer = true;

            var lines = new List<string>();
            lines.Add(FormatRow(cells[0], widths, outer));
            var sep = new List<string>();
            for (int c = 0; c < cols; c++)
                sep.Add(SeparatorCell(c < Aligns.Count ? Aligns[c] : ColumnAlign.None, widths[c]));
            lines.Add(FormatRow(sep, widths, outer));
            for (int r = 1; r < cells.Count; r++)
                lines.Add(FormatRow(cells[r], widths, outer));
            return lines;
        }

        string FormatRow(List<string> cells, int[] widths, bool outer)
        {
            var parts = new List<string>();
            for (int c = 0; c < widths.Length; c++)
                parts.Add(Pad(cells[c], widths[c], c < Aligns.Count ? Aligns[c] : ColumnAlign.None));
            string line = string.Join(" | ", parts);
            return outer ? "| " + line + " |" : line.TrimEnd();
        }

        static string Pad(string text, int width, ColumnAlign align)
        {
            int free = width - text.Length;
            if (free <= 0) return text;
            if (align == ColumnAlign.Right) return new string(' ', free) + text;
            if (align == ColumnAlign.Center)
            {
                int left = free / 2;
                return new string(' ', left) + text + new string(' ', free - left);
            }
            return text + new string(' ', free);
        }

        static string SeparatorCell(ColumnAlign align, int width)
        {
            switch (align)
            {
                case ColumnAlign.Left: return ":" + new string('-', width - 1);
                case ColumnAlign.Right: return new string('-', width - 1) + ":";
                case ColumnAlign.Center: return ":" + new string('-', Math.Max(1, width - 2)) + ":";
                default: return new string('-', width);
            }
        }
    }
}
