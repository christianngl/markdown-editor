using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MarkdownEditor.Services;
using MarkdownEditor.Themes;

namespace MarkdownEditor.Dialogs
{
    /// <summary>Mikrofon, Sprachmodell, Sprache und Sprachbefehle für das lokale Diktat.</summary>
    public sealed class DictationSettingsDialog : Window
    {
        static readonly string[] Languages = { "de", "en", "auto" };
        static readonly string[] LanguageNames = { "Deutsch", "Englisch", "Automatisch erkennen" };

        readonly ComboBox _device = new ComboBox { MinWidth = 380, Margin = new Thickness(0, 6, 0, 12) };
        readonly ComboBox _model = new ComboBox { IsEditable = true, MinWidth = 380, Margin = new Thickness(0, 6, 0, 4) };
        readonly ComboBox _language = new ComboBox { MinWidth = 380, Margin = new Thickness(0, 6, 0, 12) };
        readonly CheckBox _commands = new CheckBox { Margin = new Thickness(0, 0, 0, 12) };

        public int DeviceId { get { return _device.SelectedIndex - 1; } }
        public string Model { get { return (_model.Text ?? "").Trim(); } }
        public string SpeechLanguage { get { return Languages[_language.SelectedIndex < 0 ? 0 : _language.SelectedIndex]; } }
        public bool VoiceCommands { get { return _commands.IsChecked == true; } }

        public DictationSettingsDialog(AppSettings settings)
        {
            Title = "Diktat: Einstellungen";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ChromeBg");
            SetResourceReference(ForegroundProperty, "ChromeFg");
            SourceInitialized += delegate { ThemeManager.ApplyTitleBar(this); };

            _device.Items.Add("Standardgerät (Windows)");
            List<string> devices = AudioRecorder.GetDevices();
            foreach (string d in devices) _device.Items.Add(d);
            _device.SelectedIndex = settings.DictationDevice >= 0 && settings.DictationDevice < devices.Count ? settings.DictationDevice + 1 : 0;

            foreach (string m in WhisperTranscriber.AvailableModels()) _model.Items.Add(m);
            _model.Text = string.IsNullOrWhiteSpace(settings.DictationModel) ? WhisperTranscriber.DefaultModel : settings.DictationModel;
            var modelHint = Label("Modelle im Ordner " + WhisperTranscriber.ModelsDir + ". base = schnell, small = genauer.");
            modelHint.Opacity = 0.7;
            modelHint.Margin = new Thickness(0, 0, 0, 12);

            foreach (string n in LanguageNames) _language.Items.Add(n);
            int li = System.Array.IndexOf(Languages, settings.DictationLanguage);
            _language.SelectedIndex = li < 0 ? 0 : li;

            _commands.Content = Label("Sprachbefehle auswerten („neue Zeile“, „neuer Absatz“, „Aufzählungspunkt“, „neue Aufgabe“, „neue Überschrift“)");
            _commands.IsChecked = settings.DictationVoiceCommands;
            _commands.SetResourceReference(ForegroundProperty, "ChromeFg");

            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90 };
            ok.Click += delegate { DialogResult = true; };
            var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 90, Margin = new Thickness(0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var root = new StackPanel { Margin = new Thickness(14), MaxWidth = 480 };
            root.Children.Add(Label("Mikrofon"));
            root.Children.Add(_device);
            root.Children.Add(Label("Sprachmodell (whisper.cpp)"));
            root.Children.Add(_model);
            root.Children.Add(modelHint);
            root.Children.Add(Label("Sprache"));
            root.Children.Add(_language);
            root.Children.Add(_commands);
            var privacy = Label("Die Spracherkennung läuft vollständig auf diesem Rechner. Aufnahmen werden nur kurz als temporäre Datei gespeichert und danach gelöscht.");
            privacy.Opacity = 0.7;
            privacy.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(privacy);
            root.Children.Add(buttons);
            Content = root;
        }

        static TextBlock Label(string text)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");
            return t;
        }
    }
}
