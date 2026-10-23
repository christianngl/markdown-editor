using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MarkdownEditor.Markdown;

namespace MarkdownEditor.Editor
{
    /// <summary>
    /// Bearbeitbare Vorschau (WYSIWYG). Änderungen werden über <see cref="ContentEdited"/> gemeldet und
    /// vom Hauptfenster per <see cref="MarkdownSerializer.Assemble"/> ins Markdown übertragen.
    /// </summary>
    public sealed class PreviewEditorBox : RichTextBox
    {
        bool _loading;

        public Dictionary<Block, BlockInfo> Map = new Dictionary<Block, BlockInfo>();
        public List<string> SourceLines = new List<string>();
        public PreviewStyle DocStyle;

        /// <summary>Inhalt wurde vom Benutzer in der Vorschau geändert.</summary>
        public event EventHandler ContentEdited;
        public event Action<string> FileDropped;

        public PreviewEditorBox()
        {
            _loading = true;
            IsDocumentEnabled = true;   // Links/Bilder bleiben interaktiv
            AcceptsReturn = true;
            AcceptsTab = true;
            IsInactiveSelectionHighlightEnabled = true;
            BorderThickness = new Thickness(0);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Language = XmlLanguage.GetLanguage("de-DE");
            DataObject.AddPastingHandler(this, OnPasting);
            _loading = false;
        }

        /// <summary>Lädt ein neu gerendertes Dokument; die Scrollposition bleibt erhalten.</summary>
        public void Load(RenderResult r, PreviewStyle style)
        {
            double offset = VerticalOffset;
            _loading = true;
            try
            {
                DocStyle = style;
                Background = style.Theme.EditorBg;
                Foreground = style.Theme.Text;
                CaretBrush = style.Theme.Text;
                SelectionBrush = style.Theme.Selection;
                Document = r.Document;
                Map = r.Map;
                SourceLines = r.SourceLines;
            }
            finally
            {
                _loading = false;
            }
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(delegate { ScrollToVerticalOffset(offset); }));
        }

        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            base.OnTextChanged(e);
            if (_loading) return;
            EventHandler h = ContentEdited;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ================================================================ Struktur-Helfer

        /// <summary>Block auf oberster Ebene, in dem der Cursor steht.</summary>
        public Block TopLevelBlockAtCaret()
        {
            return TopLevelBlock(CaretPosition.Parent as TextElement);
        }

        public static Block TopLevelBlock(TextElement e)
        {
            while (e != null)
            {
                var b = e as Block;
                if (b != null && b.Parent is FlowDocument) return b;
                e = e.Parent as TextElement;
            }
            return null;
        }

        public BlockInfo InfoOf(Block b)
        {
            BlockInfo info;
            return b != null && Map.TryGetValue(b, out info) ? info : null;
        }

        /// <summary>Block, der die Markdown-Zeile <paramref name="line"/> enthält.</summary>
        public Block BlockForLine(int line)
        {
            Block best = null;
            foreach (Block b in Document.Blocks)
            {
                BlockInfo info = InfoOf(b);
                if (info == null || info.Start < 0) continue;
                if (info.Start <= line) best = b;
                if (line < info.End) break;
            }
            return best;
        }

        // ================================================================ Eingabe

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            ModifierKeys mods = Keyboard.Modifiers;
            bool ctrl = (mods & ModifierKeys.Control) != 0;

            if (e.Key == Key.Enter && mods == ModifierKeys.None)
            {
                Paragraph p = CaretPosition.Paragraph;
                BlockInfo info = p != null ? InfoOf(p) : null;
                if (info != null && info.Kind == BlockKind.Code)
                {
                    // Im Codeblock: Zeilenumbruch statt neuem Absatz
                    EditingCommands.EnterLineBreak.Execute(null, this);
                    e.Handled = true;
                    return;
                }
                if (info != null && info.Kind == BlockKind.Heading)
                {
                    // Nach einer Überschrift geht es mit Standardtext weiter
                    EditingCommands.EnterParagraphBreak.Execute(null, this);
                    Paragraph now = CaretPosition.Paragraph;
                    if (now != null)
                    {
                        if (now == p)
                        {
                            // Der Originalabsatz liegt jetzt unten → Verknüpfung auf den oberen Teil übertragen
                            var upper = p.PreviousBlock as Paragraph;
                            if (upper != null) { Map.Remove(p); Map[upper] = info; }
                        }
                        Map.Remove(now);
                        PreviewRenderer.StyleParagraph(now, BlockKind.Paragraph, 0, DocStyle);
                        foreach (Inline i in now.Inlines) { i.ClearValue(TextElement.FontWeightProperty); i.ClearValue(TextElement.FontSizeProperty); }
                    }
                    e.Handled = true;
                    return;
                }
            }
            if (ctrl && !IsAllowedCtrlKey(e.Key))
            {
                // Eingebaute Formatierungen (Ausrichtung, Schriftgröße …) haben kein Markdown-Gegenstück
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(e);
        }

        static bool IsAllowedCtrlKey(Key k)
        {
            switch (k)
            {
                case Key.A: case Key.C: case Key.V: case Key.X: case Key.Z: case Key.Y: case Key.Insert:
                case Key.Left: case Key.Right: case Key.Up: case Key.Down:
                case Key.Home: case Key.End: case Key.PageUp: case Key.PageDown:
                case Key.Back: case Key.Delete: case Key.Enter:
                case Key.LeftCtrl: case Key.RightCtrl: case Key.LeftShift: case Key.RightShift:
                    return true;
                default:
                    return false;
            }
        }

        void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            // Nur reinen Text übernehmen – fremde Formatierungen haben kein Markdown-Gegenstück
            string text = e.DataObject.GetDataPresent(DataFormats.UnicodeText) ? e.DataObject.GetData(DataFormats.UnicodeText) as string : null;
            if (string.IsNullOrEmpty(text)) { e.CancelCommand(); return; }
            e.DataObject = new DataObject(DataFormats.UnicodeText, text);
            e.FormatToApply = DataFormats.UnicodeText;
        }

        protected override void OnPreviewDragEnter(DragEventArgs e) { HandleDragOver(e); }
        protected override void OnPreviewDragOver(DragEventArgs e) { HandleDragOver(e); }

        static void HandleDragOver(DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        protected override void OnPreviewDrop(DragEventArgs e)
        {
            e.Handled = true;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            Action<string> h = FileDropped;
            if (files != null && files.Length > 0 && h != null) h(files[0]);
        }

        // ================================================================ Formatierungen in der Vorschau

        public void ToggleBold() { EditingCommands.ToggleBold.Execute(null, this); }
        public void ToggleItalic() { EditingCommands.ToggleItalic.Execute(null, this); }
        public void ToggleUnderline() { EditingCommands.ToggleUnderline.Execute(null, this); }

        public void ToggleStrike()
        {
            var current = Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            bool has = current != null && current.Any(d => d.Location == TextDecorationLocation.Strikethrough);
            Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, has ? null : TextDecorations.Strikethrough);
        }

        public void ToggleCode()
        {
            var family = Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
            bool isCode = family != null && family.Source == PreviewRenderer.MonoFont.Source;
            if (isCode)
            {
                Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, PreviewRenderer.TextFont);
                Selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
                Selection.ApplyPropertyValue(TextElement.ForegroundProperty, DocStyle.Theme.Text);
            }
            else
            {
                Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, PreviewRenderer.MonoFont);
                Selection.ApplyPropertyValue(TextElement.BackgroundProperty, DocStyle.Theme.CodeBg);
                Selection.ApplyPropertyValue(TextElement.ForegroundProperty, DocStyle.Theme.CodeText);
            }
        }

        /// <summary>level 1–6 = Überschrift, 0 = Standardtext – für alle markierten Absätze.</summary>
        public void SetHeading(int level)
        {
            var paragraphs = new List<Paragraph>();
            Block first = TopLevelBlock(Selection.Start.Parent as TextElement);
            Block last = TopLevelBlock(Selection.End.Parent as TextElement);
            for (Block b = first; b != null; b = b.NextBlock)
            {
                var p = b as Paragraph;
                if (p != null) paragraphs.Add(p);
                if (b == last) break;
            }
            foreach (Paragraph p in paragraphs)
            {
                BlockInfo info = InfoOf(p);
                if (info != null && info.Kind == BlockKind.Code) continue;
                if (info == null) { info = new BlockInfo(); Map[p] = info; }
                bool same = info.Kind == BlockKind.Heading && info.Level == level;
                info.Kind = level > 0 && !same ? BlockKind.Heading : BlockKind.Paragraph;
                info.Level = level > 0 && !same ? level : 0;
                info.Baseline = null; // erzwingt Neuschreiben dieses Blocks
                PreviewRenderer.StyleParagraph(p, info.Kind, info.Level, DocStyle);
            }
        }

        public void InsertEmoji(string symbol)
        {
            if (!Selection.IsEmpty) Selection.Text = "";
            Emoji e = EmojiCatalog.Find(symbol);
            TextPointer pos = CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            if (e != null && e.Image != null && pos.Paragraph != null)
            {
                var iuc = new InlineUIContainer(PreviewRenderer.CreateEmojiImage(e, DocStyle), pos)
                {
                    Tag = new InlineTag(symbol),
                    BaselineAlignment = BaselineAlignment.Center
                };
                CaretPosition = iuc.ElementEnd;
            }
            else
            {
                CaretPosition.InsertTextInRun(symbol);
            }
            Focus();
        }

        public void InsertLink(string url)
        {
            if (Selection.IsEmpty)
            {
                TextPointer pos = CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
                var run = new Run("Linktext", pos);
                Selection.Select(run.ContentStart, run.ContentEnd);
            }
            var link = new Hyperlink(Selection.Start, Selection.End);
            PreviewRenderer.AttachLink(link, url, DocStyle);
            Focus();
        }
    }
}
