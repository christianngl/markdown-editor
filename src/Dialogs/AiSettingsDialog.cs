using System.Windows;
using System.Windows.Controls;
using MarkdownEditor.Themes;

namespace MarkdownEditor.Dialogs
{
    /// <summary>API-Schlüssel und Modell für die KI-Befehle.</summary>
    public sealed class AiSettingsDialog : Window
    {
        static readonly string[] Models = { "claude-sonnet-5-5", "claude-haiku-4-5", "claude-opus-5-5" };

        readonly PasswordBox _key = new PasswordBox { MinWidth = 380, Margin = new Thickness(0, 6, 0, 4) };
        readonly ComboBox _model = new ComboBox { IsEditable = true, MinWidth = 380, Margin = new Thickness(0, 6, 0, 12) };

        /// <summary>Neuer Schlüssel; null = unverändert lassen.</summary>
        public string ApiKey { get; private set; }
        public string Model { get { return (_model.Text ?? "").Trim(); } }

        public AiSettingsDialog(bool hasStoredKey, bool hasEnvKey, string model)
        {
            Title = "KI: API-Schlüssel / Modell";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ChromeBg");
            SetResourceReference(ForegroundProperty, "ChromeFg");
            SourceInitialized += delegate { ThemeManager.ApplyTitleBar(this); };

            string keyInfo = hasStoredKey
                ? "Ein Schlüssel ist gespeichert. Leer lassen, um ihn zu behalten."
                : hasEnvKey
                    ? "Es wird ANTHROPIC_API_KEY aus der Umgebung verwendet. Optional hier einen eigenen Schlüssel speichern."
                    : "Schlüssel von console.anthropic.com (beginnt mit „sk-ant-“).";
            var keyLabel = Label("Claude-API-Schlüssel");
            var keyHint = Label(keyInfo);
            keyHint.Opacity = 0.7;
            keyHint.Margin = new Thickness(0, 0, 0, 12);
            var remove = new CheckBox { Content = "Gespeicherten Schlüssel entfernen", Margin = new Thickness(0, 0, 0, 12), Visibility = hasStoredKey ? Visibility.Visible : Visibility.Collapsed };
            remove.SetResourceReference(ForegroundProperty, "ChromeFg");

            foreach (string m in Models) _model.Items.Add(m);
            _model.Text = string.IsNullOrWhiteSpace(model) ? Models[0] : model;

            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90 };
            ok.Click += delegate
            {
                if (remove.IsChecked == true) ApiKey = "";
                else if (_key.Password.Trim().Length > 0) ApiKey = _key.Password.Trim();
                DialogResult = true;
            };
            var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 90, Margin = new Thickness(0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var root = new StackPanel { Margin = new Thickness(14), MaxWidth = 460 };
            root.Children.Add(keyLabel);
            root.Children.Add(_key);
            root.Children.Add(keyHint);
            root.Children.Add(remove);
            root.Children.Add(Label("Modell"));
            root.Children.Add(_model);
            var privacy = Label("Hinweis: Bei KI-Befehlen wird der Notiztext an die Anthropic-API gesendet.");
            privacy.Opacity = 0.7;
            privacy.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(privacy);
            root.Children.Add(buttons);
            Content = root;
            Loaded += delegate { _key.Focus(); };
        }

        static TextBlock Label(string text)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");
            return t;
        }
    }
}
