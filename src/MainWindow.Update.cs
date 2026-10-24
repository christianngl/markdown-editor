using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MarkdownEditor.Services;

namespace MarkdownEditor
{
    /// <summary>Updates: beim Start und über Hilfe → „Nach Updates suchen…“ neue GitHub-Releases finden und installieren.</summary>
    public sealed partial class MainWindow
    {
        bool _updateBusy;
        bool _closingForUpdate;   // Änderungen sind schon gespeichert oder verworfen: beim Schließen nicht erneut fragen

        void AddUpdateItems(ItemCollection items)
        {
            items.Add(MI("Nach Updates suchen…", delegate { CheckForUpdates(true); }));
            var auto = new MenuItem { Header = "Beim Start nach Updates suchen", IsCheckable = true, IsChecked = _settings.UpdateCheckOnStart };
            auto.Click += delegate { _settings.UpdateCheckOnStart = auto.IsChecked; _settings.Save(); };
            items.Add(auto);
        }

        /// <summary>Sucht im Hintergrund. Beim automatischen Start bleiben Fehler und „kein Update“ stumm.</summary>
        void CheckForUpdates(bool manual)
        {
            if (_updateBusy) return;
            _updateBusy = true;
            if (manual) ShowNotice("Suche nach Updates…");
            Task.Factory.StartNew(() => UpdateService.FetchLatest()).ContinueWith(t =>
            {
                _updateBusy = false;
                if (t.IsFaulted)
                {
                    if (manual)
                        MessageBox.Show(this, "Die Suche nach Updates ist fehlgeschlagen.\n\n" + t.Exception.GetBaseException().Message,
                            AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                ReleaseInfo r = t.Result;
                if (!UpdateService.IsNewer(r))
                {
                    if (manual)
                        MessageBox.Show(this, "Du verwendest die aktuelle Version " + UpdateService.CurrentVersion + ".", AppName,
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                OfferUpdate(r);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void OfferUpdate(ReleaseInfo r)
        {
            string notes = r.Notes.Trim();
            if (notes.Length > 1200) notes = notes.Substring(0, 1200) + "\n…";
            string text = "Version " + r.Version + " ist verfügbar (installiert: " + UpdateService.CurrentVersion + ").\n\n" +
                          (notes.Length > 0 ? "Neu:\n" + notes.Replace("\r\n", "\n") + "\n\n" : "") +
                          (r.AssetUrl != null ? "Jetzt aktualisieren? Der Editor startet danach neu."
                                              : "Das Release enthält keine EXE. Release-Seite öffnen?");
            if (MessageBox.Show(this, text, AppName + " – Update", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;
            if (r.AssetUrl == null)
            {
                try { Process.Start(new ProcessStartInfo(r.PageUrl) { UseShellExecute = true }); } catch (Exception) { }
                return;
            }
            if (!ConfirmDiscard()) return;

            _updateBusy = true;
            ShowNotice("Lade Version " + r.Version + "…");
            Task.Factory.StartNew(() => UpdateService.Download(r)).ContinueWith(t =>
            {
                _updateBusy = false;
                if (t.IsFaulted)
                {
                    MessageBox.Show(this, "Das Update konnte nicht geladen werden.\n\n" + t.Exception.GetBaseException().Message,
                        AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                try
                {
                    UpdateService.ScheduleReplace(t.Result, _path);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Das Update konnte nicht installiert werden.\n\n" + ex.Message, AppName,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _closingForUpdate = true;
                Close();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
