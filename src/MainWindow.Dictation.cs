using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MarkdownEditor.Dialogs;
using MarkdownEditor.Editor;
using MarkdownEditor.Markdown;
using MarkdownEditor.Services;

namespace MarkdownEditor
{
    /// <summary>Diktat: Mikrofon aufnehmen, lokal mit whisper.cpp transkribieren und am Cursor einfügen.</summary>
    public sealed partial class MainWindow
    {
        static readonly TimeSpan MaxRecording = TimeSpan.FromMinutes(10);

        Cmd _cDictate;
        AudioRecorder _recorder;
        DispatcherTimer _dictationTimer;
        CancellationTokenSource _dictationCts;
        bool _dictationCancelledByUser;

        void InitDictationCommands()
        {
            _cDictate = Reg("Diktat starten/beenden", ModifierKeys.Control | ModifierKeys.Shift, Key.M, ToggleDictation);
        }

        MenuItem BuildDictationMenu()
        {
            var menu = new MenuItem { Header = "Dik_tat" };
            menu.Items.Add(MI(_cDictate));
            menu.Items.Add(new Separator());
            menu.Items.Add(MI("Mikrofon / Modell / Sprache…", delegate { ShowDictationSettings(); }));
            return menu;
        }

        void ShowDictationSettings()
        {
            var dlg = new DictationSettingsDialog(_settings) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            _settings.DictationDevice = dlg.DeviceId;
            _settings.DictationModel = dlg.Model.Length > 0 ? dlg.Model : WhisperTranscriber.DefaultModel;
            _settings.DictationLanguage = dlg.SpeechLanguage;
            _settings.DictationVoiceCommands = dlg.VoiceCommands;
            _settings.Save();
        }

        void ToggleDictation()
        {
            if (_dictationCts != null) { ShowNotice("Diktat wird noch transkribiert – Esc bricht ab"); return; }
            if (_recorder != null) FinishDictation();
            else StartDictation();
        }

        void StartDictation()
        {
            string problem;
            if (!WhisperTranscriber.IsInstalled(_settings.DictationModel, out problem))
            {
                MessageBox.Show(this, problem, AppName + " – Diktat", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var recorder = new AudioRecorder();
            try
            {
                recorder.Start(_settings.DictationDevice);
            }
            catch (Exception ex)
            {
                recorder.Dispose();
                MessageBox.Show(this, ex.Message, AppName + " – Diktat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _recorder = recorder;
            _dictationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _dictationTimer.Tick += delegate { OnDictationTick(); };
            _dictationTimer.Start();
            OnDictationTick();
        }

        void OnDictationTick()
        {
            if (_recorder == null) return;
            TimeSpan d = _recorder.Duration;
            if (d >= MaxRecording) { FinishDictation(); return; }
            int bars = (_recorder.Level + 19) / 20;
            _notice = "● Aufnahme " + (int)d.TotalMinutes + ":" + d.Seconds.ToString("00") + "  " +
                      new string('▮', bars) + new string('▯', 5 - bars) + "   Strg+Umschalt+M beendet · Esc bricht ab";
            UpdateStatus();
        }

        void StopDictationTimer()
        {
            if (_dictationTimer == null) return;
            _dictationTimer.Stop();
            _dictationTimer = null;
        }

        /// <summary>Esc: laufende Aufnahme verwerfen bzw. Transkription abbrechen.</summary>
        bool TryCancelDictation()
        {
            if (_recorder != null)
            {
                StopDictationTimer();
                _recorder.Dispose();
                _recorder = null;
                ShowNotice("Diktat verworfen");
                return true;
            }
            if (_dictationCts != null)
            {
                _dictationCancelledByUser = true;
                _dictationCts.Cancel();
                return true;
            }
            return false;
        }

        async void FinishDictation()
        {
            StopDictationTimer();
            AudioRecorder recorder = _recorder;
            _recorder = null;
            if (recorder == null) return;

            string wav = WhisperTranscriber.NewTempWavPath();
            TimeSpan duration;
            try
            {
                recorder.StopAndSave(wav);
                duration = recorder.Duration;
            }
            catch (Exception ex)
            {
                WhisperTranscriber.TryDelete(wav);
                ShowNotice("Diktat fehlgeschlagen");
                MessageBox.Show(this, "Die Aufnahme konnte nicht gespeichert werden:\n" + ex.Message, AppName + " – Diktat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                recorder.Dispose();
            }
            if (duration.TotalSeconds < 0.5)
            {
                WhisperTranscriber.TryDelete(wav);
                ShowNotice("Diktat: keine Aufnahme");
                return;
            }

            _dictationCancelledByUser = false;
            _dictationCts = new CancellationTokenSource(TimeSpan.FromSeconds(60 + 3 * duration.TotalSeconds));
            _notice = "Diktat: transkribiere lokal … (Esc bricht ab)";
            UpdateStatus();
            Cursor = Cursors.AppStarting;

            string raw;
            try
            {
                raw = await WhisperTranscriber.TranscribeAsync(wav, _settings.DictationModel, _settings.DictationLanguage, _dictationCts.Token);
            }
            catch (OperationCanceledException)
            {
                ShowNotice(_dictationCancelledByUser ? "Diktat abgebrochen" : "Diktat: Zeitüberschreitung bei der Transkription");
                return;
            }
            catch (Exception ex)
            {
                ShowNotice("Diktat fehlgeschlagen");
                MessageBox.Show(this, "Die lokale Spracherkennung ist fehlgeschlagen:\n" + ex.Message, AppName + " – Diktat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                WhisperTranscriber.TryDelete(wav);
                _dictationCts.Dispose();
                _dictationCts = null;
                Cursor = null;
            }

            string text = DictationPostProcessor.Process(raw, _settings.DictationVoiceCommands);
            if (text.Trim().Length == 0) { ShowNotice("Diktat: nichts erkannt"); return; }
            InsertDictation(text);
            ShowNotice("Diktat eingefügt (" + (int)Math.Round(duration.TotalSeconds) + " s)");
        }

        /// <summary>Fügt den Text am Cursor ein (ein Undo-Schritt); Mehrzeiliges aus der Vorschau geht über das Markdown.</summary>
        void InsertDictation(string text)
        {
            bool inPreview = _previewActive && _previewHost.Visibility == Visibility.Visible;
            if (inPreview && text.IndexOf('\n') < 0)
            {
                _preview.Focus();
                string before = _preview.CaretPosition.GetTextInRun(System.Windows.Documents.LogicalDirection.Backward);
                _preview.Selection.Text = DictationPostProcessor.FitToContext(text, before);
                _preview.Selection.Select(_preview.Selection.End, _preview.Selection.End);
                return;
            }
            TextPos a, b;
            if (inPreview)
            {
                // Mehrzeilig aus der Vorschau: im Markdown ans Ende des aktuellen Blocks anfügen
                SyncFromPreview();
                BlockInfo info = _preview.InfoOf(_preview.TopLevelBlockAtCaret());
                int last = info != null && info.End > info.Start && info.Start >= 0 ? info.End - 1 : _editor.CaretPos().Line;
                a = b = new TextPos(last, _editor.LineText(last).Length);
            }
            else _editor.GetSelection(out a, out b);
            _previewActive = false;
            string line = _editor.LineText(a.Line);
            _editor.Replace(a, b, DictationPostProcessor.FitToContext(text, line.Substring(0, Math.Min(a.Col, line.Length))));
            FocusEditor();
        }

        /// <summary>Beim Schließen: Aufnahme verwerfen, Transkription abbrechen.</summary>
        void ShutdownDictation()
        {
            StopDictationTimer();
            if (_recorder != null) { _recorder.Dispose(); _recorder = null; }
            if (_dictationCts != null) { _dictationCancelledByUser = true; _dictationCts.Cancel(); }
        }
    }
}
