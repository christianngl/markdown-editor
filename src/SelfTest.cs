using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MarkdownEditor.Dialogs;
using MarkdownEditor.Editor;
using MarkdownEditor.Markdown;
using MarkdownEditor.Services;

namespace MarkdownEditor
{
    /// <summary>
    /// Interner Selbsttest:  MarkdownEditor.exe --selftest &lt;ergebnis.txt&gt; &lt;datei.md&gt;
    /// Spielt Bearbeitungen in Vorschau und Markdown durch (ohne echte Maus/Tastatur, außerhalb des Bildschirms),
    /// schreibt die Ergebnisse in eine Textdatei und speichert Bilder des Fensters. Einstellungen bleiben unverändert.
    /// </summary>
    public sealed partial class MainWindow
    {
        readonly StringBuilder _log = new StringBuilder();
        int _passed, _failed;

        public static void PrepareSelfTest(MainWindow w)
        {
            AppSettings.ReadOnly = true;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -6000;
            w.Top = 100;
            w.Width = 1300;
            w.Height = 820;
            w.WindowState = WindowState.Normal;
            w.ShowActivated = false;
        }

        public void RunSelfTestWhenReady(string outFile)
        {
            Loaded += delegate
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                {
                    try { RunSelfTest(outFile); }
                    catch (Exception ex) { _log.AppendLine("ABBRUCH: " + ex); _failed++; }
                    _log.AppendLine();
                    _log.AppendLine("Ergebnis: " + _passed + " bestanden, " + _failed + " fehlgeschlagen");
                    File.WriteAllText(outFile, _log.ToString(), new UTF8Encoding(false));
                    _dirty = false;
                    Close();
                }));
            };
        }

        void Check(string name, bool ok, string detail)
        {
            if (ok) _passed++; else _failed++;
            _log.AppendLine((ok ? "[OK]   " : "[FAIL] ") + name + (detail != null ? "   → " + detail : ""));
        }

        void RunSelfTest(string outFile)
        {
            string dir = Path.GetDirectoryName(outFile);
            SetViewMode(ViewMode.Both, false);
            UpdateLayout();
            string original = _editor.GetText();

            // 1. Unveränderte Vorschau → Markdown bleibt exakt gleich
            RenderPreview(_editor.GetLines());
            _previewDirty = true;
            SyncFromPreview();
            Check("Rundlauf ohne Änderung", _editor.GetText() == original, null);

            // 2. Tippen in der Vorschau
            _previewActive = true;
            Paragraph p = FindParagraph("Weiterer Text.");
            Check("Absatz in Vorschau gefunden", p != null, null);
            if (p == null) return;
            TextPointer end = p.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            _preview.Selection.Select(end, end);
            _preview.CaretPosition.InsertTextInRun(" Neu aus der Vorschau");
            SyncFromPreview();
            Check("Text aus Vorschau übernommen", _editor.GetLines().Contains("Weiterer Text. Neu aus der Vorschau"), Last(2));

            // 3. Fett in der Vorschau (Strg+B)
            TextRange word = FindText(p, "Vorschau");
            _preview.Selection.Select(word.Start, word.End);
            Execute(_cBold);
            SyncFromPreview();
            Check("Fett aus Vorschau → **…**", _editor.GetLines().Contains("Weiterer Text. Neu aus der **Vorschau**"), Last(2));

            // 4. Überschrift in der Vorschau
            _preview.Selection.Select(p.ContentEnd, p.ContentEnd);
            Execute(_cH2);
            SyncFromPreview();
            Check("Überschrift 2 aus Vorschau → ##", _editor.GetLines().Contains("## Weiterer Text. Neu aus der **Vorschau**"), Last(2));

            // 5. Emoji in der Vorschau
            _preview.Selection.Select(p.ContentEnd, p.ContentEnd);
            _preview.InsertEmoji("😀");
            SyncFromPreview();
            Check("Emoji aus Vorschau als Unicode", _editor.GetText().Contains("**Vorschau**😀"), Last(2));

            // 6. Tabellenzelle bearbeiten
            var table = _preview.Document.Blocks.OfType<Table>().FirstOrDefault();
            Check("Tabelle in Vorschau gefunden", table != null, null);
            if (table != null)
            {
                Paragraph cell = (Paragraph)table.RowGroups[0].Rows[1].Cells[0].Blocks.FirstBlock;
                cell.ContentEnd.GetInsertionPosition(LogicalDirection.Backward).InsertTextInRun("X");
                SyncFromPreview();
                Check("Tabellenzelle → Markdown-Tabelle", _editor.GetLines().Any(l => l.StartsWith("Wert 1X")), FindLine("Wert 1X"));
            }

            // 7. Listenpunkt bearbeiten
            var list = _preview.Document.Blocks.OfType<List>().FirstOrDefault();
            if (list != null)
            {
                Paragraph item = (Paragraph)list.ListItems.FirstListItem.Blocks.FirstBlock;
                item.ContentEnd.GetInsertionPosition(LogicalDirection.Backward).InsertTextInRun(" geändert");
                SyncFromPreview();
                List<string> lines = _editor.GetLines();
                Check("Listenpunkt → Markdown", lines.Contains("- Aufzählung eins geändert"), FindLine("Aufzählung"));
                Check("Andere Listenpunkte unverändert", lines.Contains("- [ ] Offene Aufgabe") && lines.Contains("- [x] Erledigte Aufgabe"), null);
            }

            // 8. Unbearbeitete Teile bleiben exakt erhalten
            string[] keep = original.Split('\n').Where(l => l.StartsWith("Standardtext mit") || l.StartsWith("## Überblick") || l == "```" || l == "var x = 42;").ToArray();
            List<string> now = _editor.GetLines();
            Check("Unbearbeitete Zeilen exakt erhalten", keep.All(now.Contains), null);

            // 9. Enter im Markdown → Zeilenumbruch, der in der Vorschau eine neue Zeile ergibt
            _previewActive = false;
            int idx = _editor.GetLines().FindIndex(l => l.StartsWith("Standardtext mit"));
            _editor.SetCaret(new TextPos(idx, _editor.LineText(idx).Length));
            _editor.HandleEnter();
            _editor.InsertText("Zweite Zeile");
            Check("Enter fügt Zeilenumbruch (zwei Leerzeichen) ein", _editor.LineText(idx).EndsWith("  ") && _editor.LineText(idx + 1) == "Zweite Zeile", "'" + _editor.LineText(idx).Substring(Math.Max(0, _editor.LineText(idx).Length - 12)) + "'");
            RenderPreview(_editor.GetLines());
            Paragraph para = FindParagraph("Standardtext mit");
            Check("Vorschau zeigt neue Zeile", para != null && para.Inlines.OfType<LineBreak>().Any(), null);
            _editor.Undo();
            _editor.Undo();

            // 10. Bekannte Dateien: fehlende Datei wird beim Öffnen entfernt
            string missing = Path.Combine(dir, "gibt-es-nicht.md");
            _settings.TouchFile(missing);
            OpenKnownFile(missing);
            Check("Fehlende Datei aus Liste entfernt", !_settings.KnownFiles.Any(f => f.Path == missing), null);
            Check("Aktuelle Datei in der Liste", _path != null && _settings.KnownFiles.Any(f => string.Equals(f.Path, _path, StringComparison.OrdinalIgnoreCase)), null);

            // 11. Emoji-Daten
            int total = EmojiCatalog.Categories.Sum(c => c.Items.Count);
            int withImage = EmojiCatalog.Categories.Sum(c => c.Items.Count(e => e.Png != null));
            Check("Emoji-Kategorien wie Apple (8)", EmojiCatalog.Categories.Count == 8, string.Join(", ", EmojiCatalog.Categories.Select(c => c.Name + " (" + c.Items.Count + ")")));
            Check("Emoji-Anzahl", total > 1800, total + " Emojis, " + withImage + " mit Farbbild");
            Check("Deutsche Suche (\"herz\")", EmojiCatalog.Search("herz", 50).Count > 5, string.Join(" ", EmojiCatalog.Search("herz", 12).Select(e => e.Symbol)));
            Emoji grin = EmojiCatalog.Find("😀");
            Check("Emoji-Name deutsch", grin != null && grin.Name.Length > 0, grin != null ? grin.Name : null);

            // 12. KI-Ergebnis übernehmen (ohne Netzwerk, mit festem Ergebnis)
            _editor.SetText("# Titel\nZeile eins\nZeile zwei");
            List<string> aiLines = _editor.GetLines();
            Check("KI: Auswahltext über Zeilen", TextBetween(aiLines, new TextPos(1, 6), new TextPos(2, 5)) == "eins\nZeile", null);
            ApplyAiResult(AiResultAction.Replace, new TextPos(0, 0), new TextPos(2, 10), "## Neu\n- Punkt");
            Check("KI: Notiz ersetzt", _editor.GetText() == "## Neu\n- Punkt", _editor.GetText());
            _editor.Undo();
            Check("KI: ein Strg+Z stellt das Original her", _editor.GetText() == "# Titel\nZeile eins\nZeile zwei", _editor.GetText());
            ApplyAiResult(AiResultAction.Insert, new TextPos(1, 0), new TextPos(1, 10), "Zusatz");
            Check("KI: nach Auswahl eingefügt", _editor.GetText() == "# Titel\nZeile eins\n\nZusatz\nZeile zwei", _editor.GetText());
            string sealedKey = Dpapi.Protect("sk-ant-test");
            Check("KI: Schlüssel per DPAPI verschlüsselt", !string.IsNullOrEmpty(sealedKey) && !sealedKey.Contains("sk-ant") && Dpapi.Unprotect(sealedKey) == "sk-ant-test", null);

            // 13. Diktat (ohne Mikrofon/whisper: Aufbereitung, WAV-Format, Einfügen)
            string dp = DictationPostProcessor.Process("[BLANK_AUDIO]\n Erster Gedanke.\n (Musik) Zweiter Gedanke. ", true);
            Check("Diktat: Artefakte entfernt, Segmente verbunden", dp == "Erster Gedanke. Zweiter Gedanke.", dp);
            dp = DictationPostProcessor.Process("Einkaufen. Neuer Absatz. Milch, neue Zeile, Brot. Aufzählungspunkt Butter. Neue Aufgabe anrufen.", true);
            Check("Diktat: Sprachbefehle", dp == "Einkaufen.\n\nMilch\nBrot.\n- Butter.\n- [ ] Anrufen.", dp.Replace("\n", "⏎"));
            dp = DictationPostProcessor.Process("Besprechung am Montag. Neue Absatz. Aufzählungspunktangebot prüfen.", true);
            Check("Diktat: tolerante Sprachbefehle (whisper-Ausgabe)", dp == "Besprechung am Montag.\n\n- Angebot prüfen.", dp.Replace("\n", "⏎"));
            dp = DictationPostProcessor.Process("Bitte eine neue Zeile einfügen.", false);
            Check("Diktat: Sprachbefehle abschaltbar", dp == "Bitte eine neue Zeile einfügen.", dp);
            Check("Diktat: nur Stille → leer", DictationPostProcessor.Process(" [BLANK_AUDIO] \n Untertitel im Auftrag des ZDF, 2021", true) == "", null);
            Check("Diktat: Leerzeichen vor Einfügen", DictationPostProcessor.FitToContext("Text", "Wort") == " Text"
                && DictationPostProcessor.FitToContext("Text", "Wort ") == "Text" && DictationPostProcessor.FitToContext("Text", "") == "Text"
                && DictationPostProcessor.FitToContext(", weiter", "Wort") == ", weiter", null);
            byte[] wavBytes = AudioRecorder.BuildWav(new byte[3200]);
            Check("Diktat: WAV-Header (16 kHz mono PCM16)", wavBytes.Length == 3244 && Encoding.ASCII.GetString(wavBytes, 0, 4) == "RIFF"
                && BitConverter.ToInt32(wavBytes, 4) == 3236 && BitConverter.ToInt32(wavBytes, 24) == 16000
                && BitConverter.ToInt16(wavBytes, 22) == 1 && BitConverter.ToInt32(wavBytes, 40) == 3200, null);
            _editor.SetText("Notiz: Wort");
            _previewActive = false;
            _editor.SetCaret(new TextPos(0, 11));
            InsertDictation("diktiert.\n- Punkt");
            Check("Diktat: am Cursor eingefügt", _editor.GetText() == "Notiz: Wort diktiert.\n- Punkt", _editor.GetText());
            _editor.Undo();
            Check("Diktat: ein Strg+Z entfernt das Diktat", _editor.GetText() == "Notiz: Wort", _editor.GetText());
            string whisperProblem;
            bool whisperReady = WhisperTranscriber.IsInstalled(_settings.DictationModel, out whisperProblem);
            _log.AppendLine("[INFO] whisper.cpp " + (whisperReady ? "eingerichtet: " + WhisperTranscriber.WhisperDir : "nicht eingerichtet") + ", Mikrofone: " + AudioRecorder.GetDevices().Count);

            // 14. Ansichten: nur Vorschau / nur Markdown / beide
            _editor.SetText("# Titel\n\nText");
            SetViewMode(ViewMode.Preview, false);
            Check("Ansicht: nur Vorschau", _editorHost.Visibility == Visibility.Collapsed && _previewHost.Visibility == Visibility.Visible
                && _previewSplitter.Visibility == Visibility.Collapsed && _previewActive, null);
            Paragraph textPara = FindParagraph("Text");
            if (textPara != null) _preview.Selection.Select(textPara.ContentEnd, textPara.ContentEnd);
            Execute(_cBullet); // struktureller Befehl läuft über das (ausgeblendete) Markdown
            Check("Ansicht: Befehl aus reiner Vorschau → Markdown + Vorschau aktualisiert",
                _editor.GetLines().Contains("- Text") && _preview.Document.Blocks.OfType<List>().Any(), Last(1));
            SetViewMode(ViewMode.Markdown, false);
            Check("Ansicht: nur Markdown", _editorHost.Visibility == Visibility.Visible && _previewHost.Visibility == Visibility.Collapsed
                && _previewSplitter.Visibility == Visibility.Collapsed && !_previewActive, null);
            SetViewMode(ViewMode.Both, false);
            Check("Ansicht: beide", _editorHost.Visibility == Visibility.Visible && _previewHost.Visibility == Visibility.Visible
                && _previewSplitter.Visibility == Visibility.Visible, null);
            CycleViewMode();
            Check("Ansicht: F12 wechselt Both → Markdown", _viewMode == ViewMode.Markdown, _viewMode.ToString());
            SetViewMode(ViewMode.Both, false);

            // Bilder für die Sichtprüfung
            _editor.SetText(original);
            RenderPreview(_editor.GetLines());
            SetSidebarVisible(true);
            SetSideMode(SideMode.Files);
            UpdateLayout();
            SaveImage(this, Path.Combine(dir, "selftest-main.png"));

            var picker = new EmojiPicker(() => new List<string> { "😀", "👍", "🚀", "❤️", "✅" }) { ShowActivated = false, Left = -6000, Top = 100 };
            picker.Show();
            picker.UpdateLayout();
            SaveImage(picker, Path.Combine(dir, "selftest-emoji.png"));
            picker.Close();
        }

        string Last(int n)
        {
            List<string> l = _editor.GetLines().Where(x => x.Trim().Length > 0).ToList();
            return string.Join(" ⏎ ", l.Skip(Math.Max(0, l.Count - n)));
        }

        string FindLine(string part)
        {
            return _editor.GetLines().FirstOrDefault(l => l.Contains(part));
        }

        Paragraph FindParagraph(string startsWith)
        {
            foreach (Block b in _preview.Document.Blocks)
            {
                var p = b as Paragraph;
                if (p != null && new TextRange(p.ContentStart, p.ContentEnd).Text.StartsWith(startsWith)) return p;
            }
            return null;
        }

        static TextRange FindText(Paragraph p, string text)
        {
            for (TextPointer tp = p.ContentStart; tp != null && tp.CompareTo(p.ContentEnd) < 0; tp = tp.GetNextContextPosition(LogicalDirection.Forward))
            {
                if (tp.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
                string run = tp.GetTextInRun(LogicalDirection.Forward);
                int i = run.IndexOf(text, StringComparison.Ordinal);
                if (i >= 0) return new TextRange(tp.GetPositionAtOffset(i), tp.GetPositionAtOffset(i + text.Length));
            }
            return new TextRange(p.ContentEnd, p.ContentEnd);
        }

        static void SaveImage(Window w, string file)
        {
            var root = (FrameworkElement)w.Content;
            int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
            if (width <= 0 || height <= 0) return;
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(w.Background, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height));
            }
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(file)) enc.Save(fs);
        }
    }
}
