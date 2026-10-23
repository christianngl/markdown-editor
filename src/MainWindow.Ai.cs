using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarkdownEditor.Dialogs;
using MarkdownEditor.Editor;
using MarkdownEditor.Services;

namespace MarkdownEditor
{
    /// <summary>KI-Befehle: Notiz oder Auswahl von Claude zusammenfassen, strukturieren oder frei umschreiben lassen.</summary>
    public sealed partial class MainWindow
    {
        static readonly TimeSpan AiTimeout = TimeSpan.FromMinutes(3);

        Cmd _cAiSummary, _cAiStructure, _cAiCustom;
        CancellationTokenSource _aiCts;
        bool _aiCancelledByUser;
        string _lastAiInstruction = "";

        void InitAiCommands()
        {
            const ModifierKeys CS = ModifierKeys.Control | ModifierKeys.Shift;
            // Keine Vorschau-/Struktur-Variante: aus der Vorschau heraus wirkt der Befehl auf die ganze Notiz.
            _cAiSummary = Reg("KI: Zusammenfassen", CS, Key.A, delegate { RunAi(AiPrompts.Summarize, "Zusammenfassen", null); });
            _cAiStructure = Reg("KI: Strukturieren", CS, Key.G, delegate { RunAi(AiPrompts.Structure, "Strukturieren", null); });
            _cAiCustom = Reg("KI: Eigene Anweisung…", CS, Key.J, RunAiCustom);
        }

        void AddAiItems(ItemCollection items)
        {
            items.Add(MI(_cAiSummary));
            items.Add(MI(_cAiStructure));
            items.Add(MI(_cAiCustom));
        }

        MenuItem BuildAiMenu()
        {
            var ai = new MenuItem { Header = "_KI" };
            AddAiItems(ai.Items);
            ai.Items.Add(new Separator());
            ai.Items.Add(MI("API-Schlüssel / Modell…", delegate { ShowAiSettings(); }));
            return ai;
        }

        /// <summary>Esc während einer laufenden Anfrage bricht sie ab.</summary>
        bool TryCancelAi()
        {
            if (_aiCts == null) return false;
            _aiCancelledByUser = true;
            _aiCts.Cancel();
            return true;
        }

        bool ShowAiSettings()
        {
            var dlg = new AiSettingsDialog(!string.IsNullOrEmpty(_settings.ApiKey),
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")), _settings.AiModel) { Owner = this };
            if (dlg.ShowDialog() != true) return false;
            if (dlg.ApiKey != null) _settings.ApiKey = dlg.ApiKey.Length > 0 ? dlg.ApiKey : null;
            _settings.AiModel = dlg.Model.Length > 0 ? dlg.Model : AppSettings.DefaultAiModel;
            _settings.Save();
            return true;
        }

        void RunAiCustom()
        {
            var dlg = new InputDialog("KI: Eigene Anweisung", "Was soll Claude mit der Notiz (bzw. der Auswahl) machen?", _lastAiInstruction) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Value.Trim().Length == 0) return;
            _lastAiInstruction = dlg.Value.Trim();
            RunAi(AiPrompts.Custom, "Eigene Anweisung", _lastAiInstruction);
        }

        async void RunAi(string systemPrompt, string label, string instruction)
        {
            if (_aiCts != null) { ShowNotice("Claude arbeitet bereits – Esc bricht ab"); return; }

            bool fromPreview = _previewActive && _previewHost.Visibility == Visibility.Visible;
            SyncFromPreview();

            if (string.IsNullOrWhiteSpace(_settings.EffectiveApiKey) && !ShowAiSettings()) return;
            string apiKey = _settings.EffectiveApiKey;
            if (string.IsNullOrWhiteSpace(apiKey)) return;

            // Eingabe: Auswahl im Markdown-Editor, sonst (oder aus der Vorschau heraus) die ganze Notiz
            List<string> lines = _editor.GetLines();
            string snapshot = string.Join("\n", lines);
            TextPos start, end;
            _editor.GetSelection(out start, out end);
            bool hasSelection = !fromPreview && (start.Line != end.Line || start.Col != end.Col);
            if (!hasSelection)
            {
                start = new TextPos(0, 0);
                end = new TextPos(lines.Count - 1, lines[lines.Count - 1].Length);
            }
            string input = TextBetween(lines, start, end);
            if (input.Trim().Length == 0) { ShowNotice(hasSelection ? "Die Auswahl ist leer" : "Die Notiz ist leer"); return; }

            _aiCancelledByUser = false;
            _aiCts = new CancellationTokenSource(AiTimeout);
            _notice = label + ": Claude arbeitet … (Esc bricht ab)";
            UpdateStatus();
            Cursor = Cursors.AppStarting;

            ClaudeResult result;
            try
            {
                result = await ClaudeClient.CompleteAsync(apiKey, _settings.AiModel, systemPrompt,
                    AiPrompts.BuildUserMessage(input, instruction), _aiCts.Token);
            }
            catch (OperationCanceledException)
            {
                ShowNotice(_aiCancelledByUser ? "KI-Anfrage abgebrochen" : "KI-Anfrage: Zeitüberschreitung");
                return;
            }
            catch (ClaudeException ex)
            {
                ShowNotice("KI-Anfrage fehlgeschlagen");
                MessageBox.Show(this, ex.Message, AppName + " – KI", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (Exception ex)
            {
                ShowNotice("KI-Anfrage fehlgeschlagen");
                MessageBox.Show(this, "Unerwarteter Fehler bei der KI-Anfrage:\n" + ex.Message, AppName + " – KI", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                _aiCts.Dispose();
                _aiCts = null;
                Cursor = null;
            }

            _notice = "";
            UpdateStatus();
            var dlg = new AiResultDialog(label, result.Text, result.Truncated, hasSelection) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Action == AiResultAction.Discard) return;

            SyncFromPreview();
            if (_editor.GetText() != snapshot)
            {
                // Bereichsangaben passen nicht mehr – nichts überschreiben.
                try { Clipboard.SetText(dlg.ResultText.Replace("\n", "\r\n")); }
                catch (Exception) { }
                MessageBox.Show(this, "Die Notiz wurde während der Anfrage geändert. Das Ergebnis wurde nicht eingefügt, sondern in die Zwischenablage kopiert.",
                    AppName + " – KI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ApplyAiResult(dlg.Action, start, end, dlg.ResultText);
        }

        /// <summary>Übernimmt das Ergebnis als ein Undo-Schritt.</summary>
        void ApplyAiResult(AiResultAction action, TextPos start, TextPos end, string text)
        {
            _previewActive = false;
            if (action == AiResultAction.Replace) _editor.Replace(start, end, text);
            else if (action == AiResultAction.Insert) _editor.Replace(end, end, "\n\n" + text);
            FocusEditor();
        }

        static string TextBetween(List<string> lines, TextPos start, TextPos end)
        {
            if (start.Line == end.Line)
            {
                string l = lines[start.Line];
                int a = Math.Min(start.Col, l.Length), b = Math.Min(Math.Max(end.Col, a), l.Length);
                return l.Substring(a, b - a);
            }
            var sb = new StringBuilder();
            sb.Append(lines[start.Line].Substring(Math.Min(start.Col, lines[start.Line].Length)));
            for (int i = start.Line + 1; i < end.Line; i++) sb.Append('\n').Append(lines[i]);
            sb.Append('\n').Append(lines[end.Line].Substring(0, Math.Min(end.Col, lines[end.Line].Length)));
            return sb.ToString();
        }
    }
}
