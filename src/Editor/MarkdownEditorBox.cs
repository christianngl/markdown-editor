using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using MarkdownEditor.Markdown;
using MarkdownEditor.Themes;

namespace MarkdownEditor.Editor
{
    /// <summary>Position im Text: Zeile und Spalte (0-basiert, Zeichen).</summary>
    public struct TextPos
    {
        public readonly int Line;
        public readonly int Col;

        public TextPos(int line, int col)
        {
            Line = line;
            Col = col;
        }
    }

    /// <summary>
    /// WYSIWYM-Markdown-Editor.
    /// Modell: Jede Textzeile ist genau ein <see cref="Paragraph"/>. Der Inhalt bleibt reines Markdown –
    /// Markierungen (#, **, |, …) bleiben sichtbar, werden aber gedimmt, und der Text wird live formatiert.
    /// Rückgängig/Wiederholen ist textbasiert implementiert, damit Formatierungs-Updates
    /// den Undo-Verlauf nicht verschmutzen.
    /// </summary>
    public sealed class MarkdownEditorBox : RichTextBox
    {
        sealed class LineTag { public string Signature; }
        sealed class Snapshot { public string Text; public TextPos Caret; }

        static readonly double[] HeadingScale = { 1.0, 1.9, 1.55, 1.3, 1.15, 1.05, 1.0 };
        static readonly Regex OrderedPrefixRx = new Regex(@"^(\s*)(\d+)([.)]\s+)");
        const int MaxUndo = 300;

        readonly FontFamily _textFont = new FontFamily("Segoe UI, Segoe UI Emoji, Segoe UI Symbol");
        readonly FontFamily _monoFont = new FontFamily("Cascadia Mono, Consolas, Segoe UI Emoji");
        readonly List<Snapshot> _undo = new List<Snapshot>();
        readonly List<Snapshot> _redo = new List<Snapshot>();
        readonly DispatcherTimer _typingTimer;

        Theme _theme;
        LineInfo[] _infos = new LineInfo[0];
        double _fontSize = 15;
        int _styleVersion;
        bool _updating, _typingSession;
        Snapshot _lastState;

        /// <summary>Wird bei jeder inhaltlichen Änderung durch den Benutzer ausgelöst.</summary>
        public event EventHandler TextEdited;

        /// <summary>Datei per Drag &amp; Drop auf den Editor gezogen.</summary>
        public event Action<string> FileDropped;

        public MarkdownEditorBox()
        {
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _typingTimer.Tick += delegate { EndTypingSession(); };

            _updating = true; // Initialisierung löst TextChanged aus – ignorieren
            IsUndoEnabled = false;
            AcceptsReturn = true;
            AcceptsTab = true;
            IsInactiveSelectionHighlightEnabled = true;
            BorderThickness = new Thickness(0);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            FontFamily = _textFont;
            Language = XmlLanguage.GetLanguage("de-DE");
            Document = new FlowDocument(NewParagraph(""));
            Document.FontFamily = _textFont;
            Document.FontSize = _fontSize;

            _updating = false;

            DataObject.AddPastingHandler(this, OnPasting);
            Document.PagePadding = new Thickness(28, 18, 28, 48);
            _lastState = Capture();
        }

        // ================================================================ Eigenschaften

        public LineInfo[] LineInfos { get { return _infos; } }

        public double EditorFontSize
        {
            get { return _fontSize; }
            set
            {
                _fontSize = Math.Max(8, Math.Min(48, value));
                Document.FontSize = _fontSize;
                _styleVersion++;
                Rehighlight();
            }
        }

        public bool SpellCheckEnabled
        {
            get { return this.SpellCheck.IsEnabled; }
            set { this.SpellCheck.IsEnabled = value; }
        }

        public new bool CanUndo { get { return _undo.Count > 0 || _typingSession; } }
        public new bool CanRedo { get { return _redo.Count > 0; } }

        public void ApplyTheme(Theme theme)
        {
            _theme = theme;
            Background = theme.EditorBg;
            Foreground = theme.Text;
            CaretBrush = theme.Text;
            SelectionBrush = theme.Selection;
            Document.Foreground = theme.Text;
            _breakBrush = null;
            _styleVersion++;
            Rehighlight();
        }

        // ================================================================ Text-API

        public string GetText()
        {
            return string.Join("\n", GetLines());
        }

        public List<string> GetLines()
        {
            var lines = new List<string>();
            foreach (Block b in Document.Blocks)
                lines.Add(BlockText(b));
            return lines;
        }

        /// <summary>Setzt den gesamten Text (z. B. beim Öffnen) und leert den Undo-Verlauf.</summary>
        public void SetText(string text)
        {
            _typingTimer.Stop();
            _typingSession = false;
            _undo.Clear();
            _redo.Clear();
            SetTextInternal(text);
            SetCaret(new TextPos(0, 0));
            ScrollToHome();
            _lastState = Capture();
        }

        public string LineText(int line)
        {
            return ParaText(ParagraphAt(line));
        }

        public TextPos CaretPos()
        {
            return PosOf(CaretPosition);
        }

        public void GetSelection(out TextPos start, out TextPos end)
        {
            start = PosOf(Selection.Start);
            end = PosOf(Selection.End);
        }

        public string GetSelectedText()
        {
            return Selection.Text;
        }

        public void SetCaret(TextPos pos)
        {
            TextPointer p = PointerOf(pos);
            Selection.Select(p, p);
        }

        public void Select(TextPos start, TextPos end)
        {
            Selection.Select(PointerOf(start), PointerOf(end));
            EnsureCaretVisible();
        }

        public void GoToLine(int line)
        {
            Paragraph p = ParagraphAt(line);
            if (p == null) return;
            Selection.Select(p.ContentStart, p.ContentStart);
            p.BringIntoView();
            Focus();
        }

        /// <summary>Setzt den Cursor an den Zeilenanfang und scrollt dorthin, ohne den Fokus zu übernehmen.</summary>
        public void RevealLine(int line)
        {
            Paragraph p = ParagraphAt(line);
            if (p == null) return;
            _updating = true;
            try { Selection.Select(p.ContentStart, p.ContentStart); }
            finally { _updating = false; }
            p.BringIntoView();
        }

        /// <summary>
        /// Übernimmt einen extern erzeugten Text (z. B. aus der Vorschau). Ersetzt wird nur der tatsächlich
        /// geänderte Zeilenbereich – als ein Undo-Schritt.
        /// </summary>
        public void ApplyExternalText(IList<string> newLines)
        {
            List<string> old = GetLines();
            int prefix = 0;
            while (prefix < old.Count && prefix < newLines.Count && old[prefix] == newLines[prefix]) prefix++;
            if (prefix == old.Count && prefix == newLines.Count) return;
            int suffix = 0;
            while (suffix < old.Count - prefix && suffix < newLines.Count - prefix
                   && old[old.Count - 1 - suffix] == newLines[newLines.Count - 1 - suffix]) suffix++;

            int oldEnd = old.Count - suffix;      // exklusiv
            int newEnd = newLines.Count - suffix;
            var mid = new List<string>();
            for (int i = prefix; i < newEnd; i++) mid.Add(newLines[i]);
            string text = string.Join("\n", mid);

            if (oldEnd > prefix && mid.Count > 0)
                Replace(new TextPos(prefix, 0), new TextPos(oldEnd - 1, old[oldEnd - 1].Length), text);
            else if (oldEnd > prefix)
            {
                // Zeilen entfernt
                if (oldEnd < old.Count) Replace(new TextPos(prefix, 0), new TextPos(oldEnd, 0), "");
                else if (prefix > 0) Replace(new TextPos(prefix - 1, old[prefix - 1].Length), new TextPos(oldEnd - 1, old[oldEnd - 1].Length), "");
                else Replace(new TextPos(0, 0), new TextPos(oldEnd - 1, old[oldEnd - 1].Length), "");
            }
            else
            {
                // Zeilen eingefügt
                if (prefix < old.Count) Replace(new TextPos(prefix, 0), new TextPos(prefix, 0), text + "\n");
                else Replace(new TextPos(prefix - 1, old[prefix - 1].Length), new TextPos(prefix - 1, old[prefix - 1].Length), "\n" + text);
            }
        }

        public void InsertText(string text)
        {
            TextPos a, b;
            GetSelection(out a, out b);
            Replace(a, b, text);
        }

        /// <summary>
        /// Zentrale Bearbeitungsoperation: ersetzt den Bereich [start, end) durch <paramref name="text"/>
        /// (Zeilenumbrüche erlaubt), erzeugt einen Undo-Schritt und setzt den Cursor hinter den eingefügten Text.
        /// </summary>
        public TextPos Replace(TextPos start, TextPos end, string text)
        {
            text = NormalizeNewlines(text ?? "");
            BeginEdit();

            List<Paragraph> paras = Paragraphs();
            int l0 = Clamp(start.Line, 0, paras.Count - 1);
            int l1 = Clamp(end.Line, l0, paras.Count - 1);
            string first = ParaText(paras[l0]);
            string last = l1 == l0 ? first : ParaText(paras[l1]);
            int c0 = Clamp(start.Col, 0, first.Length);
            int c1 = Clamp(end.Col, 0, last.Length);
            if (l0 == l1 && c1 < c0) c1 = c0;

            string prefix = first.Substring(0, c0);
            string suffix = last.Substring(c1);
            string[] newLines = (prefix + text + suffix).Split('\n');

            _updating = true;
            try
            {
                foreach (string nl in newLines)
                    Document.Blocks.InsertBefore(paras[l0], NewParagraph(nl));
                for (int i = l0; i <= l1; i++)
                    Document.Blocks.Remove(paras[i]);
            }
            finally
            {
                _updating = false;
            }
            Rehighlight();

            int lastBreak = text.LastIndexOf('\n');
            TextPos result = lastBreak < 0
                ? new TextPos(l0, c0 + text.Length)
                : new TextPos(l0 + CountChar(text, '\n'), text.Length - lastBreak - 1);
            SetCaret(result);
            EnsureCaretVisible();
            EndEdit();
            return result;
        }

        /// <summary>Ersetzt komplette Zeilen [firstLine..lastLine] durch neue Zeilen.</summary>
        public TextPos ReplaceLines(int firstLine, int lastLine, IList<string> lines)
        {
            string lastText = LineText(lastLine);
            return Replace(new TextPos(firstLine, 0), new TextPos(lastLine, lastText.Length), string.Join("\n", lines));
        }

        // ================================================================ Suche

        public bool Find(string query, bool forward)
        {
            if (string.IsNullOrEmpty(query)) return false;
            List<string> lines = GetLines();
            string text = string.Join("\n", lines);
            if (text.Length == 0) return false;

            TextPos a, b;
            GetSelection(out a, out b);
            int idx;
            if (forward)
            {
                int from = Math.Min(Offset(lines, b), text.Length);
                idx = text.IndexOf(query, from, StringComparison.CurrentCultureIgnoreCase);
                if (idx < 0) idx = text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
            }
            else
            {
                int from = Offset(lines, a) - 1;
                idx = from >= 0 ? text.LastIndexOf(query, from, StringComparison.CurrentCultureIgnoreCase) : -1;
                if (idx < 0) idx = text.LastIndexOf(query, StringComparison.CurrentCultureIgnoreCase);
            }
            if (idx < 0) return false;
            Select(PosFromOffset(lines, idx), PosFromOffset(lines, idx + query.Length));
            return true;
        }

        static int Offset(List<string> lines, TextPos pos)
        {
            int off = 0;
            for (int i = 0; i < pos.Line && i < lines.Count; i++)
                off += lines[i].Length + 1;
            return off + pos.Col;
        }

        static TextPos PosFromOffset(List<string> lines, int offset)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (offset <= lines[i].Length) return new TextPos(i, offset);
                offset -= lines[i].Length + 1;
            }
            int lastLine = Math.Max(0, lines.Count - 1);
            return new TextPos(lastLine, lines.Count > 0 ? lines[lastLine].Length : 0);
        }

        // ================================================================ Undo / Redo

        public new void Undo()
        {
            EndTypingSession();
            if (_undo.Count == 0) return;
            _redo.Add(Capture());
            Snapshot s = Pop(_undo);
            Restore(s);
        }

        public new void Redo()
        {
            EndTypingSession();
            if (_redo.Count == 0) return;
            _undo.Add(Capture());
            Snapshot s = Pop(_redo);
            Restore(s);
        }

        void Restore(Snapshot s)
        {
            SetTextInternal(s.Text);
            SetCaret(s.Caret);
            EnsureCaretVisible();
            _lastState = new Snapshot { Text = s.Text, Caret = s.Caret };
            RaiseEdited();
        }

        static Snapshot Pop(List<Snapshot> stack)
        {
            Snapshot s = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return s;
        }

        void PushUndo(Snapshot s)
        {
            _undo.Add(s);
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        }

        Snapshot Capture()
        {
            return new Snapshot { Text = GetText(), Caret = CaretPos() };
        }

        void BeginEdit()
        {
            EndTypingSession();
            PushUndo(Capture());
            _redo.Clear();
        }

        void EndEdit()
        {
            _lastState = Capture();
            RaiseEdited();
        }

        void EndTypingSession()
        {
            _typingTimer.Stop();
            if (!_typingSession) return;
            _typingSession = false;
            _lastState = Capture();
        }

        void RaiseEdited()
        {
            EventHandler h = TextEdited;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ================================================================ Eingabe

        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            base.OnTextChanged(e);
            if (_updating) return;

            // Tippen wird zu Undo-Schritten gebündelt (Pause von 800 ms beendet einen Schritt).
            if (!_typingSession)
            {
                PushUndo(_lastState);
                _redo.Clear();
                _typingSession = true;
            }
            _typingTimer.Stop();
            _typingTimer.Start();

            Rehighlight();
            RaiseEdited();
        }

        protected override void OnSelectionChanged(RoutedEventArgs e)
        {
            base.OnSelectionChanged(e);
            if (!_updating && !_typingSession && _lastState != null)
                _lastState.Caret = CaretPos();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            ModifierKeys mods = Keyboard.Modifiers;
            bool ctrl = (mods & ModifierKeys.Control) != 0;
            bool shift = (mods & ModifierKeys.Shift) != 0;
            bool alt = (mods & ModifierKeys.Alt) != 0;

            if (e.Key == Key.Enter && !alt)
            {
                e.Handled = true;
                HandleEnter();
                return;
            }
            if (e.Key == Key.Tab && !ctrl && !alt)
            {
                e.Handled = true;
                HandleTab(shift);
                return;
            }
            if (ctrl && !alt)
            {
                if (e.Key == Key.Z && !shift) { e.Handled = true; Undo(); return; }
                if (e.Key == Key.Y || (e.Key == Key.Z && shift)) { e.Handled = true; Redo(); return; }
                // Eingebaute RichTextBox-Formatierungen (Strg+E/L/R/J, Strg+[ ...) sperren:
                // Der Inhalt soll ausschließlich Markdown bleiben.
                if (!IsAllowedCtrlKey(e.Key)) { e.Handled = true; return; }
            }
            base.OnPreviewKeyDown(e);
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            // Zeilenumbrüche laufen ausschließlich über HandleEnter. Ein "\r" kann hier verspätet ankommen,
            // z. B. wenn Enter einen Dialog geschlossen hat – würde sonst das Zeilenmodell umgehen.
            if (e.Text == "\r" || e.Text == "\n" || e.Text == "\r\n")
            {
                e.Handled = true;
                return;
            }
            base.OnPreviewTextInput(e);
        }

        static bool IsAllowedCtrlKey(Key k)
        {
            switch (k)
            {
                case Key.A: case Key.C: case Key.V: case Key.X: case Key.Insert:
                case Key.Left: case Key.Right: case Key.Up: case Key.Down:
                case Key.Home: case Key.End: case Key.PageUp: case Key.PageDown:
                case Key.Back: case Key.Delete:
                case Key.LeftCtrl: case Key.RightCtrl: case Key.LeftShift: case Key.RightShift:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Enter: neue Zeile, Listen und Zitate werden automatisch fortgesetzt.</summary>
        internal void HandleEnter()
        {
            TextPos a, b;
            GetSelection(out a, out b);
            string line = LineText(a.Line);
            LineInfo info = InfoAt(a.Line);
            string continuation = "";

            if (a.Line == b.Line && (info.Kind == LineKind.Bullet || info.Kind == LineKind.Ordered || info.Kind == LineKind.Quote))
            {
                string prefix = line.Substring(0, Math.Min(info.PrefixLength, line.Length));
                if (line.Trim() == prefix.Trim() && a.Col >= line.Length)
                {
                    // Leerer Listenpunkt + Enter beendet die Liste.
                    Replace(new TextPos(a.Line, 0), new TextPos(a.Line, line.Length), "");
                    return;
                }
                if (a.Col >= info.PrefixLength)
                    continuation = ContinuePrefix(prefix, info.Kind);
            }

            // Fließtext/Zitat: Markdown-Zeilenumbruch ("  " am Zeilenende), damit der folgende Text
            // auch in der Vorschau (und in jedem anderen Markdown-Viewer) in einer neuen Zeile beginnt.
            string hardBreak = "";
            if (a.Line == b.Line && (info.Kind == LineKind.Normal || info.Kind == LineKind.Quote))
            {
                string left = line.Substring(0, Math.Min(a.Col, line.Length));
                bool hasContent = left.Substring(Math.Min(info.PrefixLength, left.Length)).Trim().Length > 0;
                if (hasContent && !left.EndsWith("  ") && !left.EndsWith("\\"))
                    hardBreak = left.EndsWith(" ") ? " " : "  ";
            }
            Replace(a, b, hardBreak + "\n" + continuation);
        }

        static string ContinuePrefix(string prefix, LineKind kind)
        {
            if (kind == LineKind.Ordered)
            {
                Match m = OrderedPrefixRx.Match(prefix);
                long n;
                if (m.Success && long.TryParse(m.Groups[2].Value, out n))
                    return m.Groups[1].Value + (n + 1) + m.Groups[3].Value;
            }
            if (kind == LineKind.Bullet)
                return Regex.Replace(prefix, @"\[[xX]\]", "[ ]");
            return prefix;
        }

        /// <summary>Tab: in Listen bzw. bei Mehrzeilenauswahl einrücken, sonst Tabulator.</summary>
        void HandleTab(bool outdent)
        {
            TextPos a, b;
            GetSelection(out a, out b);
            LineInfo info = InfoAt(a.Line);
            bool list = info.Kind == LineKind.Bullet || info.Kind == LineKind.Ordered;
            if (!list && a.Line == b.Line && !outdent)
            {
                Replace(a, b, "\t");
                return;
            }

            string unit = info.Kind == LineKind.Ordered ? "   " : list ? "  " : "    ";
            var lines = new List<string>();
            int firstDelta = 0;
            for (int i = a.Line; i <= b.Line; i++)
            {
                string s = LineText(i);
                string n;
                if (!outdent) n = unit + s;
                else if (s.StartsWith("\t")) n = s.Substring(1);
                else
                {
                    int k = 0;
                    while (k < unit.Length && k < s.Length && s[k] == ' ') k++;
                    n = s.Substring(k);
                }
                if (i == a.Line) firstDelta = n.Length - s.Length;
                lines.Add(n);
            }
            ReplaceLines(a.Line, b.Line, lines);
            if (a.Line == b.Line)
                SetCaret(new TextPos(a.Line, Math.Max(0, a.Col + firstDelta)));
            else
                Select(new TextPos(a.Line, 0), new TextPos(b.Line, lines[lines.Count - 1].Length));
        }

        void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            // Immer als reiner Text einfügen – keine fremden Formatierungen im Dokument.
            string text = e.DataObject.GetDataPresent(DataFormats.UnicodeText)
                ? e.DataObject.GetData(DataFormats.UnicodeText) as string
                : null;
            e.CancelCommand();
            if (string.IsNullOrEmpty(text)) return;
            Dispatcher.BeginInvoke(new Action(delegate { InsertText(text); }));
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
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            Action<string> h = FileDropped;
            if (files != null && files.Length > 0 && h != null) h(files[0]);
        }

        public void EnsureCaretVisible()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                Rect r = Selection.End.GetCharacterRect(LogicalDirection.Forward);
                if (r.IsEmpty) return;
                if (r.Bottom > ViewportHeight - 8)
                    ScrollToVerticalOffset(VerticalOffset + r.Bottom - ViewportHeight + 40);
                else if (r.Top < 0)
                    ScrollToVerticalOffset(VerticalOffset + r.Top - 40);
            }));
        }

        // ================================================================ Darstellung

        /// <summary>
        /// Klassifiziert alle Zeilen und baut nur die Absätze neu auf, deren Text oder Typ sich geändert hat.
        /// </summary>
        public void Rehighlight()
        {
            if (_theme == null) return;

            foreach (Block b in Document.Blocks)
            {
                if (!(b is Paragraph))
                {
                    // Fremde Blockstruktur (sollte nie vorkommen) → zurück in das Zeilenmodell.
                    SetTextInternal(GetText());
                    return;
                }
            }

            List<Paragraph> paras = Paragraphs();
            var texts = new List<string>(paras.Count);
            foreach (Paragraph p in paras) texts.Add(ParaText(p));
            _infos = MarkdownSyntax.Classify(texts);

            TextPointer selStart = Selection.Start, selEnd = Selection.End;
            Paragraph sp = selStart.Paragraph, ep = selEnd.Paragraph;
            int sc = sp != null ? ColOf(sp, selStart) : 0;
            int ec = ep != null ? ColOf(ep, selEnd) : 0;
            bool selectionTouched = false;

            _updating = true;
            try
            {
                for (int i = 0; i < paras.Count; i++)
                {
                    string sig = _styleVersion + "|" + (int)_infos[i].Kind + "|" + _infos[i].Level + "|" + texts[i];
                    var tag = paras[i].Tag as LineTag;
                    if (tag != null && tag.Signature == sig) continue;
                    BuildParagraph(paras[i], texts[i], _infos[i]);
                    paras[i].Tag = new LineTag { Signature = sig };
                    if (paras[i] == sp || paras[i] == ep) selectionTouched = true;
                }
                if (selectionTouched && sp != null && ep != null)
                    Selection.Select(PointerIn(sp, sc), PointerIn(ep, ec));
            }
            finally
            {
                _updating = false;
            }
        }

        void BuildParagraph(Paragraph p, string text, LineInfo info)
        {
            Theme t = _theme;
            p.Inlines.Clear();
            p.Margin = new Thickness(0, 1, 0, 1);
            p.Padding = new Thickness(0);
            p.BorderThickness = new Thickness(0);
            p.BorderBrush = null;
            p.Background = null;
            p.FontFamily = _textFont;
            p.FontSize = _fontSize;
            p.FontWeight = FontWeights.Normal;
            p.FontStyle = FontStyles.Normal;
            p.Foreground = t.Text;

            int pre = Math.Min(info.PrefixLength, text.Length);
            switch (info.Kind)
            {
                case LineKind.Heading:
                    p.FontSize = _fontSize * HeadingScale[Clamp(info.Level, 0, 6)];
                    p.FontWeight = FontWeights.SemiBold;
                    p.Foreground = t.Heading;
                    p.Margin = new Thickness(0, info.Level <= 2 ? _fontSize * 0.8 : _fontSize * 0.4, 0, 3);
                    if (info.Level <= 2)
                    {
                        p.BorderBrush = t.Border;
                        p.BorderThickness = new Thickness(0, 0, 0, 1);
                        p.Padding = new Thickness(0, 0, 0, 3);
                    }
                    AddRun(p, text.Substring(0, pre), t.Dim, false);
                    AddInline(p, text.Substring(pre));
                    break;

                case LineKind.Rule:
                    p.Foreground = t.Dim;
                    p.BorderBrush = t.Border;
                    p.BorderThickness = new Thickness(0, 0, 0, 2);
                    p.Margin = new Thickness(0, 2, 0, 8);
                    AddRun(p, text, null, false);
                    break;

                case LineKind.Quote:
                    p.BorderBrush = t.Accent;
                    p.BorderThickness = new Thickness(3, 0, 0, 0);
                    p.Padding = new Thickness(10, 1, 0, 1);
                    p.Margin = new Thickness(0);
                    p.Foreground = t.Quote;
                    p.FontStyle = FontStyles.Italic;
                    AddRun(p, text.Substring(0, pre), t.Dim, false);
                    AddInlineWithBreak(p, text.Substring(pre));
                    break;

                case LineKind.Bullet:
                case LineKind.Ordered:
                    AddRun(p, text.Substring(0, pre), t.Accent, true);
                    AddInline(p, text.Substring(pre));
                    break;

                case LineKind.Fence:
                    CodeStyle(p);
                    p.Foreground = t.Dim;
                    AddRun(p, text, null, false);
                    break;

                case LineKind.Code:
                    CodeStyle(p);
                    p.Foreground = t.CodeText;
                    AddRun(p, text, null, false);
                    break;

                case LineKind.TableHeader:
                case LineKind.TableRow:
                    TableStyle(p);
                    if (info.Kind == LineKind.TableHeader) p.FontWeight = FontWeights.Bold;
                    AddTableCells(p, text);
                    break;

                case LineKind.TableSeparator:
                    TableStyle(p);
                    p.Foreground = t.Dim;
                    AddRun(p, text, null, false);
                    break;

                default:
                    AddInlineWithBreak(p, text);
                    break;
            }
            if (p.Inlines.Count == 0) p.Inlines.Add(new Run(""));
        }

        /// <summary>Wie AddInline; ein Zeilenumbruch ("  " am Ende) wird dezent markiert, damit er nicht unsichtbar ist.</summary>
        void AddInlineWithBreak(Paragraph p, string text)
        {
            int end = text.Length;
            while (end > 0 && text[end - 1] == ' ') end--;
            if (text.Length - end < 2 || end == 0)
            {
                AddInline(p, text);
                return;
            }
            AddInline(p, text.Substring(0, end));
            var marker = new Run(text.Substring(end))
            {
                Background = _breakBrush ?? (_breakBrush = MakeBreakBrush()),
                ToolTip = "Zeilenumbruch (zwei Leerzeichen am Zeilenende)"
            };
            p.Inlines.Add(marker);
        }

        Brush _breakBrush;

        Brush MakeBreakBrush()
        {
            var dim = _theme.Dim as SolidColorBrush;
            var b = new SolidColorBrush(dim != null ? dim.Color : Colors.Gray) { Opacity = 0.28 };
            b.Freeze();
            return b;
        }

        void CodeStyle(Paragraph p)
        {
            p.FontFamily = _monoFont;
            p.FontSize = _fontSize * 0.92;
            p.Background = _theme.CodeBg;
            p.Margin = new Thickness(0);
            p.Padding = new Thickness(10, 1, 10, 1);
        }

        void TableStyle(Paragraph p)
        {
            p.FontFamily = _monoFont;
            p.FontSize = _fontSize * 0.92;
            p.Background = _theme.CodeBg;
            p.BorderBrush = _theme.Border;
            p.BorderThickness = new Thickness(0, 0, 0, 1);
            p.Margin = new Thickness(0);
            p.Padding = new Thickness(10, 3, 10, 3);
        }

        static void AddRun(Paragraph p, string s, Brush fg, bool bold)
        {
            if (s.Length == 0) return;
            var r = new Run(s);
            if (fg != null) r.Foreground = fg;
            if (bold) r.FontWeight = FontWeights.Bold;
            p.Inlines.Add(r);
        }

        void AddTableCells(Paragraph p, string text)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                bool pipe = text[i] == '|' && (i == 0 || text[i - 1] != '\\');
                if (!pipe) { sb.Append(text[i]); continue; }
                if (sb.Length > 0) { AddInline(p, sb.ToString()); sb.Clear(); }
                AddRun(p, "|", _theme.Accent, false);
            }
            if (sb.Length > 0) AddInline(p, sb.ToString());
        }

        void AddInline(Paragraph p, string s)
        {
            foreach (InlineSegment seg in MarkdownSyntax.ParseInline(s))
            {
                var r = new Run(seg.Text);
                if (seg.Has(InlineStyle.Bold)) r.FontWeight = FontWeights.Bold;
                if (seg.Has(InlineStyle.Italic)) r.FontStyle = FontStyles.Italic;

                var deco = new TextDecorationCollection();
                if (seg.Has(InlineStyle.Underline) || (seg.Has(InlineStyle.Link) && !seg.Has(InlineStyle.Marker)))
                    foreach (TextDecoration d in TextDecorations.Underline) deco.Add(d);
                if (seg.Has(InlineStyle.Strike))
                    foreach (TextDecoration d in TextDecorations.Strikethrough) deco.Add(d);
                if (seg.Has(InlineStyle.Marker)) deco.Clear();
                if (deco.Count > 0) r.TextDecorations = deco;

                if (seg.Has(InlineStyle.Marker))
                {
                    r.Foreground = _theme.Dim;
                }
                else if (seg.Has(InlineStyle.Code))
                {
                    r.FontFamily = _monoFont;
                    r.Background = _theme.CodeBg;
                    r.Foreground = _theme.CodeText;
                }
                else if (seg.Has(InlineStyle.Link))
                {
                    r.Foreground = _theme.Link;
                }
                p.Inlines.Add(r);
            }
        }

        // ================================================================ Interne Helfer

        void SetTextInternal(string text)
        {
            string[] lines = NormalizeNewlines(text).Split('\n');
            _updating = true;
            try
            {
                Document.Blocks.Clear();
                foreach (string l in lines)
                    Document.Blocks.Add(NewParagraph(l));
            }
            finally
            {
                _updating = false;
            }
            Rehighlight();
        }

        LineInfo InfoAt(int line)
        {
            return line >= 0 && line < _infos.Length ? _infos[line] : new LineInfo(LineKind.Normal, 0, 0);
        }

        List<Paragraph> Paragraphs()
        {
            var list = new List<Paragraph>();
            foreach (Block b in Document.Blocks)
            {
                var p = b as Paragraph;
                if (p != null) list.Add(p);
            }
            if (list.Count == 0)
            {
                Paragraph p = NewParagraph("");
                _updating = true;
                Document.Blocks.Add(p);
                _updating = false;
                list.Add(p);
            }
            return list;
        }

        Paragraph ParagraphAt(int line)
        {
            int i = 0;
            Paragraph last = null;
            foreach (Block b in Document.Blocks)
            {
                var p = b as Paragraph;
                if (p == null) continue;
                if (i == line) return p;
                last = p;
                i++;
            }
            return line <= 0 ? Paragraphs()[0] : last;
        }

        TextPos PosOf(TextPointer tp)
        {
            Paragraph p = tp.Paragraph;
            if (p == null)
            {
                TextPointer ins = tp.GetInsertionPosition(LogicalDirection.Backward);
                if (ins != null && ins.Paragraph != null)
                {
                    tp = ins;
                    p = ins.Paragraph;
                }
            }
            if (p == null)
            {
                var lastPara = Document.Blocks.LastBlock as Paragraph;
                int lastLine = Math.Max(0, Document.Blocks.Count - 1);
                return new TextPos(lastLine, lastPara != null ? ParaText(lastPara).Length : 0);
            }
            int line = 0;
            foreach (Block b in Document.Blocks)
            {
                if (b == p) break;
                line++;
            }
            return new TextPos(line, ColOf(p, tp));
        }

        TextPointer PointerOf(TextPos pos)
        {
            return PointerIn(ParagraphAt(pos.Line), pos.Col);
        }

        static int ColOf(Paragraph p, TextPointer tp)
        {
            if (tp.CompareTo(p.ContentStart) <= 0) return 0;
            return new TextRange(p.ContentStart, tp).Text.Length;
        }

        static TextPointer PointerIn(Paragraph p, int col)
        {
            if (col <= 0) return p.ContentStart;
            int acc = 0;
            TextPointer found = FindInRuns(p.Inlines, col, ref acc);
            return found ?? p.ContentEnd;
        }

        static TextPointer FindInRuns(InlineCollection inlines, int col, ref int acc)
        {
            foreach (Inline inline in inlines)
            {
                var run = inline as Run;
                if (run != null)
                {
                    int len = run.Text.Length;
                    if (col <= acc + len) return run.ContentStart.GetPositionAtOffset(col - acc);
                    acc += len;
                    continue;
                }
                var span = inline as Span;
                if (span != null)
                {
                    TextPointer t = FindInRuns(span.Inlines, col, ref acc);
                    if (t != null) return t;
                }
            }
            return null;
        }

        static string BlockText(Block b)
        {
            var p = b as Paragraph;
            if (p != null) return ParaText(p);
            return new TextRange(b.ContentStart, b.ContentEnd).Text.Replace("\r\n", "\n");
        }

        static string ParaText(Paragraph p)
        {
            var sb = new StringBuilder();
            AppendInlines(p.Inlines, sb);
            return sb.ToString();
        }

        static void AppendInlines(InlineCollection inlines, StringBuilder sb)
        {
            foreach (Inline inline in inlines)
            {
                var run = inline as Run;
                if (run != null) { sb.Append(run.Text); continue; }
                var span = inline as Span;
                if (span != null) AppendInlines(span.Inlines, sb);
            }
        }

        static Paragraph NewParagraph(string text)
        {
            return new Paragraph(new Run(text)) { Margin = new Thickness(0, 1, 0, 1) };
        }

        static string NormalizeNewlines(string s)
        {
            return s.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        static int CountChar(string s, char c)
        {
            int n = 0;
            foreach (char ch in s) if (ch == c) n++;
            return n;
        }

        static int Clamp(int v, int min, int max)
        {
            return v < min ? min : v > max ? max : v;
        }
    }
}
