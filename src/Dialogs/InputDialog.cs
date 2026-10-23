using System.Windows;
using System.Windows.Controls;
using MarkdownEditor.Themes;

namespace MarkdownEditor.Dialogs
{
    /// <summary>Kleiner Eingabedialog (z. B. für Link-Adressen in der Vorschau).</summary>
    public sealed class InputDialog : Window
    {
        readonly TextBox _box = new TextBox { MinWidth = 360, Margin = new Thickness(0, 6, 0, 12) };

        public string Value { get { return _box.Text; } }

        public InputDialog(string title, string label, string value)
        {
            Title = title;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ChromeBg");
            SetResourceReference(ForegroundProperty, "ChromeFg");
            SourceInitialized += delegate { ThemeManager.ApplyTitleBar(this); };

            var text = new TextBlock { Text = label };
            text.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");
            _box.Text = value ?? "";

            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90 };
            ok.Click += delegate { DialogResult = true; };
            var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 90, Margin = new Thickness(0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(text);
            root.Children.Add(_box);
            root.Children.Add(buttons);
            Content = root;
            Loaded += delegate { _box.Focus(); _box.SelectAll(); };
        }
    }
}
