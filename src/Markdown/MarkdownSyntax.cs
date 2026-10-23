using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkdownEditor.Markdown
{
    public enum LineKind
    {
        Blank, Normal, Heading, Quote, Bullet, Ordered, Rule, Fence, Code, TableHeader, TableSeparator, TableRow
    }

    /// <summary>Klassifizierung einer einzelnen Markdown-Zeile.</summary>
    public struct LineInfo
    {
        public readonly LineKind Kind;
        public readonly int Level;        // Überschriftenebene bzw. Einrückung bei Listen
        public readonly int PrefixLength; // Länge der Markdown-Markierung am Zeilenanfang

        public LineInfo(LineKind kind, int level, int prefixLength)
        {
            Kind = kind;
            Level = level;
            PrefixLength = prefixLength;
        }

        public bool IsTable
        {
            get { return Kind == LineKind.TableHeader || Kind == LineKind.TableSeparator || Kind == LineKind.TableRow; }
        }

        public bool IsCode
        {
            get { return Kind == LineKind.Fence || Kind == LineKind.Code; }
        }
    }

    [Flags]
    public enum InlineStyle
    {
        None = 0, Bold = 1, Italic = 2, Underline = 4, Strike = 8, Code = 16, Link = 32, Marker = 64, Url = 128, Image = 256
    }

    public sealed class InlineSegment
    {
        public string Text;
        public InlineStyle Style;
        public string Url;

        public bool Has(InlineStyle s) { return (Style & s) != 0; }
    }

    /// <summary>
    /// Leichtgewichtiger Markdown-Parser: Zeilenklassifizierung (Blockebene)
    /// und Tokenisierung von Inline-Formatierungen. Wird vom Editor und von der Vorschau genutzt.
    /// </summary>
    public static class MarkdownSyntax
    {
        static readonly Regex HeadingRx = new Regex(@"^(#{1,6})(\s+|$)", RegexOptions.Compiled);
        static readonly Regex RuleRx = new Regex(@"^ {0,3}([-*_])( *\1){2,} *$", RegexOptions.Compiled);
        static readonly Regex QuoteRx = new Regex(@"^ {0,3}>\s?", RegexOptions.Compiled);
        static readonly Regex BulletRx = new Regex(@"^(\s*)([-*+])\s+(\[[ xX]\]\s+)?", RegexOptions.Compiled);
        static readonly Regex OrderedRx = new Regex(@"^(\s*)(\d{1,9})([.)])\s+", RegexOptions.Compiled);
        static readonly Regex FenceRx = new Regex(@"^ {0,3}(```|~~~)", RegexOptions.Compiled);
        static readonly Regex TableSepRx = new Regex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$", RegexOptions.Compiled);

        public static LineInfo[] Classify(IList<string> lines)
        {
            var result = new LineInfo[lines.Count];
            bool inFence = false;
            string fenceToken = null;

            for (int i = 0; i < lines.Count; i++)
            {
                string s = lines[i];
                Match m = FenceRx.Match(s);

                if (inFence)
                {
                    if (m.Success && m.Groups[1].Value == fenceToken && s.Trim().Length == fenceToken.Length)
                    {
                        result[i] = new LineInfo(LineKind.Fence, 0, s.Length);
                        inFence = false;
                    }
                    else
                    {
                        result[i] = new LineInfo(LineKind.Code, 0, 0);
                    }
                    continue;
                }
                if (m.Success)
                {
                    inFence = true;
                    fenceToken = m.Groups[1].Value;
                    result[i] = new LineInfo(LineKind.Fence, 0, s.Length);
                    continue;
                }
                if (s.Trim().Length == 0)
                {
                    result[i] = new LineInfo(LineKind.Blank, 0, 0);
                    continue;
                }

                // Tabellen (GFM): Kopfzeile + Trennzeile + Datenzeilen
                if (i > 0 && result[i - 1].Kind == LineKind.TableHeader)
                {
                    result[i] = new LineInfo(LineKind.TableSeparator, 0, 0);
                    continue;
                }
                if (i > 0 && (result[i - 1].Kind == LineKind.TableSeparator || result[i - 1].Kind == LineKind.TableRow) && HasPipe(s))
                {
                    result[i] = new LineInfo(LineKind.TableRow, 0, 0);
                    continue;
                }
                if (HasPipe(s) && i + 1 < lines.Count && IsTableSeparator(lines[i + 1]))
                {
                    result[i] = new LineInfo(LineKind.TableHeader, 0, 0);
                    continue;
                }

                m = HeadingRx.Match(s);
                if (m.Success)
                {
                    result[i] = new LineInfo(LineKind.Heading, m.Groups[1].Length, m.Length);
                    continue;
                }
                if (RuleRx.IsMatch(s))
                {
                    result[i] = new LineInfo(LineKind.Rule, 0, s.Length);
                    continue;
                }
                m = QuoteRx.Match(s);
                if (m.Success)
                {
                    result[i] = new LineInfo(LineKind.Quote, 0, m.Length);
                    continue;
                }
                m = BulletRx.Match(s);
                if (m.Success)
                {
                    result[i] = new LineInfo(LineKind.Bullet, m.Groups[1].Length, m.Length);
                    continue;
                }
                m = OrderedRx.Match(s);
                if (m.Success)
                {
                    result[i] = new LineInfo(LineKind.Ordered, m.Groups[1].Length, m.Length);
                    continue;
                }
                result[i] = new LineInfo(LineKind.Normal, 0, 0);
            }
            return result;
        }

        public static bool IsTableSeparator(string s)
        {
            return HasPipe(s) && TableSepRx.IsMatch(s);
        }

        public static bool HasPipe(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] == '|' && (i == 0 || s[i - 1] != '\\'))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ Inline

        public static List<InlineSegment> ParseInline(string s)
        {
            var output = new List<InlineSegment>();
            if (!string.IsNullOrEmpty(s))
                ParseInto(s, 0, s.Length, InlineStyle.None, output);
            return Merge(output);
        }

        static void ParseInto(string s, int start, int end, InlineStyle style, List<InlineSegment> output)
        {
            var plain = new StringBuilder();
            int i = start;
            while (i < end)
            {
                char c = s[i];

                // Escape: \* \_ \| ...
                if (c == '\\' && i + 1 < end && IsPunctuation(s[i + 1]))
                {
                    Flush(plain, style, output);
                    Add(output, "\\", style | InlineStyle.Marker, null);
                    plain.Append(s[i + 1]);
                    i += 2;
                    continue;
                }

                // `Inline-Code`
                if (c == '`')
                {
                    int j = s.IndexOf('`', i + 1, end - i - 1);
                    if (j > i + 1)
                    {
                        Flush(plain, style, output);
                        Add(output, "`", style | InlineStyle.Marker, null);
                        Add(output, s.Substring(i + 1, j - i - 1), style | InlineStyle.Code, null);
                        Add(output, "`", style | InlineStyle.Marker, null);
                        i = j + 1;
                        continue;
                    }
                }

                // **fett**, __unterstrichen__, ~~durchgestrichen~~
                if ((c == '*' || c == '_' || c == '~') && i + 1 < end && s[i + 1] == c && (c != '_' || !IsWordChar(s, i - 1)))
                {
                    string token = new string(c, 2);
                    int j = FindClose(s, token, i + 2, end);
                    if (j > 0)
                    {
                        InlineStyle flag = c == '*' ? InlineStyle.Bold : c == '_' ? InlineStyle.Underline : InlineStyle.Strike;
                        Flush(plain, style, output);
                        Add(output, token, style | InlineStyle.Marker, null);
                        ParseInto(s, i + 2, j, style | flag, output);
                        Add(output, token, style | InlineStyle.Marker, null);
                        i = j + 2;
                        continue;
                    }
                }

                // *kursiv* oder _kursiv_
                if ((c == '*' || c == '_') && (i + 1 >= end || s[i + 1] != c) && (c != '_' || !IsWordChar(s, i - 1)))
                {
                    int j = FindCloseSingle(s, c, i + 1, end);
                    if (j > 0)
                    {
                        Flush(plain, style, output);
                        Add(output, c.ToString(), style | InlineStyle.Marker, null);
                        ParseInto(s, i + 1, j, style | InlineStyle.Italic, output);
                        Add(output, c.ToString(), style | InlineStyle.Marker, null);
                        i = j + 1;
                        continue;
                    }
                }

                // [Link](url) und ![Bild](url)
                if (c == '[' || (c == '!' && i + 1 < end && s[i + 1] == '['))
                {
                    bool image = c == '!';
                    int open = image ? i + 1 : i;
                    int close = FindBracket(s, open, end);
                    if (close > 0 && close + 1 < end && s[close + 1] == '(')
                    {
                        int paren = s.IndexOf(')', close + 2, end - close - 2);
                        if (paren > 0)
                        {
                            string url = s.Substring(close + 2, paren - close - 2);
                            Flush(plain, style, output);
                            Add(output, s.Substring(i, open - i + 1), style | InlineStyle.Marker, null);
                            if (image)
                            {
                                Add(output, s.Substring(open + 1, close - open - 1), style | InlineStyle.Link | InlineStyle.Image, url);
                            }
                            else
                            {
                                int first = output.Count;
                                ParseInto(s, open + 1, close, style | InlineStyle.Link, output);
                                for (int k = first; k < output.Count; k++)
                                    output[k].Url = url;
                            }
                            Add(output, "](", style | InlineStyle.Marker, null);
                            Add(output, url, style | InlineStyle.Marker | InlineStyle.Url, url);
                            Add(output, ")", style | InlineStyle.Marker, null);
                            i = paren + 1;
                            continue;
                        }
                    }
                }

                plain.Append(c);
                i++;
            }
            Flush(plain, style, output);
        }

        static void Flush(StringBuilder plain, InlineStyle style, List<InlineSegment> output)
        {
            if (plain.Length == 0) return;
            Add(output, plain.ToString(), style, null);
            plain.Clear();
        }

        static void Add(List<InlineSegment> output, string text, InlineStyle style, string url)
        {
            if (text.Length == 0) return;
            output.Add(new InlineSegment { Text = text, Style = style, Url = url });
        }

        static List<InlineSegment> Merge(List<InlineSegment> list)
        {
            var merged = new List<InlineSegment>(list.Count);
            foreach (InlineSegment seg in list)
            {
                InlineSegment last = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (last != null && last.Style == seg.Style && last.Url == seg.Url && !seg.Has(InlineStyle.Image))
                    last.Text += seg.Text;
                else
                    merged.Add(seg);
            }
            return merged;
        }

        static int FindClose(string s, string token, int from, int end)
        {
            int j = s.IndexOf(token, from, StringComparison.Ordinal);
            while (j >= 0 && j + token.Length <= end)
            {
                if (j > from && s[from] != ' ' && s[j - 1] != ' ')
                    return j;
                j = s.IndexOf(token, j + 1, StringComparison.Ordinal);
            }
            return -1;
        }

        static int FindCloseSingle(string s, char c, int from, int end)
        {
            if (from >= end || s[from] == ' ') return -1;
            for (int j = from; j < end; j++)
            {
                if (s[j] != c) continue;
                if (j + 1 < end && s[j + 1] == c) { j++; continue; } // Teil eines Doppelmarkers
                if (j > from && s[j - 1] != ' ' && (c != '_' || !IsWordChar(s, j + 1)))
                    return j;
            }
            return -1;
        }

        static int FindBracket(string s, int open, int end)
        {
            int depth = 0;
            for (int j = open; j < end; j++)
            {
                if (s[j] == '\\') { j++; continue; }
                if (s[j] == '[') depth++;
                else if (s[j] == ']' && --depth == 0) return j;
            }
            return -1;
        }

        static bool IsWordChar(string s, int index)
        {
            return index >= 0 && index < s.Length && char.IsLetterOrDigit(s[index]);
        }

        static bool IsPunctuation(char c)
        {
            return char.IsPunctuation(c) || char.IsSymbol(c);
        }
    }
}
