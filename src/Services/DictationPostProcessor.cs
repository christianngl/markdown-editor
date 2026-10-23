using System;
using System.Text.RegularExpressions;

namespace MarkdownEditor.Services
{
    /// <summary>Bereitet den von whisper.cpp erkannten Text für die Notiz auf.</summary>
    public static class DictationPostProcessor
    {
        const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        /// <summary>Markierungen und typische Halluzinationen bei Stille.</summary>
        static readonly Regex Artifacts = new Regex(
            @"\[[^\]\n]*\]" +                                                                 // [BLANK_AUDIO], [Musik] …
            @"|\((?:Musik|Applaus|Lachen|Stille|Ger(?:ä|ae)usche?|Husten|music|applause|laughter|silence|noise)[^)\n]*\)" +
            @"|\*(?:Musik|Applaus|Lachen|Stille|Husten|music|applause|laughter)[^*\n]*\*" +
            @"|Untertitel(?:ung)?\s+(?:im\s+Auftrag\s+)?(?:des|der)\s+(?:ZDF|Amara\.org)[^.\n]*\.?" +
            @"|Untertitel\s+von\s+Stephanie\s+Geiges\.?", Opt);

        // Sprachbefehle: Komma/Leerraum davor und Satzzeichen danach werden mit ersetzt; ein Satzende davor bleibt.
        const string Pre = @"[ \t,;:]*\b";
        const string Post = @"\b[ \t,;:.!?]*";
        // Toleriert typische Erkennungsfehler: „neue Absatz“, „neuen Zeile“, „Aufzählungspunktangebot“ (zusammengeschrieben).
        static readonly Regex Paragraph = new Regex(Pre + @"neue[rn]?\s+Absatz" + Post, Opt);
        static readonly Regex Line = new Regex(Pre + @"neue[nr]?\s+Zeile" + Post, Opt);
        static readonly Regex Bullet = new Regex(Pre + @"Aufz(?:ä|ae)hlungspunkt(?:e)?[ \t,;:.!?]*", Opt);
        static readonly Regex Task = new Regex(Pre + @"(?:Checkbox|neue\s+Aufgabe)" + Post, Opt);
        static readonly Regex Heading = new Regex(Pre + @"neue\s+(?:Ü|Ue)berschrift" + Post, Opt);

        /// <summary>Rohtext → Notiztext (Zeilenumbrüche als "\n").</summary>
        public static string Process(string raw, bool voiceCommands)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string t = raw.Replace("\r", "");
            t = Artifacts.Replace(t, " ");
            // whisper schreibt ein Segment pro Zeile – zu Fließtext verbinden
            t = Regex.Replace(t, @"\s+", " ").Trim();
            if (t.Length == 0) return "";

            if (voiceCommands)
            {
                t = Paragraph.Replace(t, "\n\n");
                t = Line.Replace(t, "\n");
                t = Bullet.Replace(t, "\n- ");
                t = Task.Replace(t, "\n- [ ] ");
                t = Heading.Replace(t, "\n## ");
                // Satzanfang nach eingefügtem Umbruch/Marker groß schreiben
                t = Regex.Replace(t, @"(\n(?:- \[ \] |- |## )?)(\p{Ll})", m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());
                t = Regex.Replace(t, @" +\n", "\n");
                t = Regex.Replace(t, @"\n{3,}", "\n\n");
                t = t.TrimEnd(' ');
                if (t.Trim().Length == 0) return t.Trim(' ');
                if (!t.StartsWith("\n")) t = t.TrimStart();
            }
            return t;
        }

        /// <summary>Setzt ein Leerzeichen davor, wenn direkt vor dem Cursor ein Wort oder Satzzeichen steht.</summary>
        public static string FitToContext(string text, string lineBeforeCaret)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(lineBeforeCaret)) return text;
            char before = lineBeforeCaret[lineBeforeCaret.Length - 1];
            char first = text[0];
            if (char.IsWhiteSpace(before) || first == '\n' || char.IsWhiteSpace(first)) return text;
            if (first == '.' || first == ',' || first == ';' || first == ':' || first == '!' || first == '?') return text;
            return " " + text;
        }
    }
}
