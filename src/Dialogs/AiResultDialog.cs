using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MarkdownEditor.Themes;

namespace MarkdownEditor.Dialogs
{
    public enum AiResultAction { Discard, Replace, Insert }

    /// <summary>Zeigt das Ergebnis eines KI-Befehls; der Text kann vor dem Übernehmen noch angepasst werden.</summary>
    public sealed class AiResultDialog : Window
    {
        readonly TextBox _box = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            Padding = new Thickness(6),
            Margin = new Thickness(0, 6, 0, 12)
        };

        public AiResultAction Action { get; private set; }

        /// <summary>Ergebnistext (ggf. vom Benutzer angepasst), Zeilenumbrüche als \n.</summary>
        public string ResultText { get { return _box.Text.Replace("\r\n", "\n"); } }

        public AiResultDialog(string title, string text, bool truncated, bool forSelection)
        {
            Title = title + " – Ergebnis von Claude";
            Width = 760;
            Height = 580;
            MinWidth = 420;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ChromeBg");
            SetResourceReference(ForegroundProperty, "ChromeFg");
            SourceInitialized += delegate { ThemeManager.ApplyTitleBar(this); };

            string info = truncated
                ? "Achtung: Das Ergebnis wurde am Längenlimit abgeschnitten. Der Text kann vor dem Übernehmen bearbeitet werden."
                : "Der Text kann vor dem Übernehmen bearbeitet werden. Übernehmen lässt sich mit Strg+Z rückgängig machen.";
            var label = new TextBlock { Text = info, TextWrapping = TextWrapping.Wrap };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");
            _box.Text = (text ?? "").Replace("\n", "\r\n");

            var replace = new Button { Content = forSelection ? "Auswahl ersetzen" : "Notiz ersetzen", IsDefault = true, MinWidth = 120 };
            replace.Click += delegate { Action = AiResultAction.Replace; DialogResult = true; };
            var insert = new Button { Content = forSelection ? "Nach der Auswahl einfügen" : "Unten anhängen", MinWidth = 120 };
            insert.Click += delegate { Action = AiResultAction.Insert; DialogResult = true; };
            var copy = new Button { Content = "Kopieren", MinWidth = 90 };
            copy.Click += delegate
            {
                try { Clipboard.SetText(_box.Text); copy.Content = "Kopiert ✓"; }
                catch (System.Exception) { copy.Content = "Fehler beim Kopieren"; }
            };
            var discard = new Button { Content = "Verwerfen", IsCancel = true, MinWidth = 90, Margin = new Thickness(0) };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(replace);
            buttons.Children.Add(insert);
            buttons.Children.Add(copy);
            buttons.Children.Add(discard);

            var root = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(label, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(label);
            root.Children.Add(buttons);
            root.Children.Add(_box);
            Content = root;
            Loaded += delegate { replace.Focus(); };
        }
    }
}
