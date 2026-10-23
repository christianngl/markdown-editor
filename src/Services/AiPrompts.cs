namespace MarkdownEditor.Services
{
    /// <summary>System-Prompts für die KI-Befehle.</summary>
    public static class AiPrompts
    {
        const string Base =
            "Du arbeitest in einem Markdown-Editor. Der Nutzer schickt dir eine Notiz oder einen Ausschnitt daraus in <notiz>-Tags. " +
            "Antworte ausschließlich mit dem Ergebnis als Markdown – ohne Einleitung, ohne Schlusskommentar und ohne umschließenden Codeblock. " +
            "Schreibe in der Sprache der Notiz.\n\n";

        public const string Summarize = Base +
            "Fasse die Notiz zusammen: zuerst ein bis zwei Sätze mit der Kernaussage, danach die wichtigsten Punkte als Aufzählung. " +
            "Offene Aufgaben, Entscheidungen und Termine übernimmst du ausdrücklich, falls es welche gibt. Erfinde nichts hinzu.";

        public const string Structure = Base +
            "Gliedere die Notiz neu, damit sie übersichtlich wird: sinnvolle Überschriften (ab ##), Aufzählungen, " +
            "Aufgabenlisten (- [ ]) für To-dos und Tabellen nur dort, wo echte tabellarische Daten stehen. " +
            "Behalte alle Informationen, Links, Zahlen und Codeblöcke bei; lass nichts weg und erfinde nichts. " +
            "Doppelte Aussagen darfst du zusammenführen und offensichtliche Tippfehler korrigieren.";

        public const string Custom = Base +
            "Führe die Anweisung des Nutzers auf die Notiz aus.";

        /// <summary>Nachricht an Claude: optionale Anweisung, danach der Notiztext.</summary>
        public static string BuildUserMessage(string note, string instruction)
        {
            string head = string.IsNullOrWhiteSpace(instruction) ? "" : "Anweisung: " + instruction.Trim() + "\n\n";
            return head + "<notiz>\n" + note + "\n</notiz>";
        }
    }
}
