using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MarkdownEditor.Dialogs;
using MarkdownEditor.Editor;
using MarkdownEditor.Markdown;
using MarkdownEditor.Services;
using MarkdownEditor.Themes;
using Microsoft.Win32;

namespace MarkdownEditor
{
    /// <summary>
    /// Hauptfenster: Menü, Markdown-Editor, bearbeitbare Vorschau (beide synchron), Seitenleiste
    /// (Gliederung / bekannte Dateien / Ordner) und Statusleiste.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        const string AppName = "Markdown Editor";
        const string FileFilter = "Markdown (*.md;*.markdown)|*.md;*.markdown|Textdateien (*.txt)|*.txt|Alle Dateien (*.*)|*.*";
        static readonly string[] BrowsableExtensions = { ".md", ".markdown", ".txt" };

        enum SideMode { Outline, Files, Folder }

        /// <summary>Welche Bereiche angezeigt werden: Markdown, Vorschau oder beide nebeneinander.</summary>
        enum ViewMode { Both, Markdown, Preview }

        sealed class Cmd
        {
            public string Header;
            public Key Key;
            public ModifierKeys Mods;
            public Action Run;                       // Ausführung im Markdown-Editor
            public Action<PreviewEditorBox> Preview; // Ausführung direkt in der Vorschau (optional)
            public bool Structural;                  // in der Vorschau: über den Markdown-Editor ausführen
            public string Gesture;                   // angezeigtes Kürzel, wenn ein Feld es selbst behandelt (Strg+Z, Tab)
        }

        sealed class SideEntry
        {
            public int Line = -1;
            public string Path;
            public bool IsFolder;
        }

        readonly AppSettings _settings;
        readonly MarkdownEditorBox _editor = new MarkdownEditorBox();
        readonly PreviewEditorBox _preview = new PreviewEditorBox();
        readonly ListBox _sideList = new ListBox();
        readonly Button _tabOutline, _tabFiles, _tabFolder;
        readonly Grid _fileSearchHost = new Grid { Margin = new Thickness(8, 2, 8, 6) };
        readonly TextBox _fileSearch = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        readonly Border _sidebar, _previewHost;
        readonly GridSplitter _sideSplitter, _previewSplitter;
        readonly ColumnDefinition _colSidebar = new ColumnDefinition { Width = new GridLength(250), MinWidth = 0 };
        readonly ColumnDefinition _colEditor = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 };
        readonly ColumnDefinition _colPreview = new ColumnDefinition { Width = new GridLength(0) };
        readonly DockPanel _editorHost = new DockPanel();
        readonly TextBlock _statusLeft = new TextBlock(), _statusRight = new TextBlock();
        readonly Border _findBar;
        readonly TextBox _findBox = new TextBox { Width = 260, VerticalContentAlignment = VerticalAlignment.Center };
        readonly TextBlock _findInfo = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        readonly DispatcherTimer _autoSaveTimer, _refreshTimer, _previewSyncTimer;
        readonly List<Cmd> _commands = new List<Cmd>();

        MenuItem _themeMenu, _recentMenu, _autoSaveItem, _sidebarItem, _fullScreenItem, _spellItem;
        MenuItem _viewBothItem, _viewMarkdownItem, _viewPreviewItem;
        Cmd[] _cHeadings;   // Überschrift 1–6
        Cmd _cNormal, _cBold, _cItalic, _cUnderline, _cStrike, _cCode, _cClear, _cRule, _cTable, _cEmoji, _cImage;
        Cmd _cBullet, _cOrdered, _cTask, _cTaskDone, _cIndent, _cOutdent, _cQuote, _cCodeBlock, _cLink;
        Cmd _cNew, _cOpen, _cSave, _cSaveAs, _cFind, _cFindNext, _cUndo, _cRedo;
        Cmd _cSidebar, _cViewCycle, _cViewBoth, _cViewMarkdown, _cViewPreview, _cFullScreen, _cDark, _cZoomIn, _cZoomOut, _cZoomReset;

        EmojiPicker _emojiPicker;
        bool _emojiToPreview;

        string _path;
        string _browseFolder;
        bool _dirty;
        SideMode _sideMode = SideMode.Outline;
        string _lastSaveInfo = "";
        string _notice = "";
        DocumentFile _file = new DocumentFile { Text = "" };
        int _wordCount;

        // Synchronisation Markdown ↔ Vorschau
        bool _previewActive;          // zuletzt fokussiertes Feld war die Vorschau
        bool _previewDirty;           // Vorschau geändert, noch nicht ins Markdown übertragen
        bool _previewNeedsRerender;   // Vorschau sollte neu aufgebaut werden (Normalisierung)
        bool _applyingFromPreview;
        bool _skipPreviewRender;
        int _lastRevealedLine = -1;

        ViewMode _viewMode = ViewMode.Both;

        // Vollbild (F11): vorheriger Fensterzustand
        bool _fullScreen;
        WindowState _stateBeforeFullScreen;
        WindowStyle _styleBeforeFullScreen;
        ResizeMode _resizeBeforeFullScreen;

        public MainWindow(string fileToOpen)
        {
            _settings = AppSettings.Load();
            _settings.PruneMissingFiles(); // Verknüpfungen zu gelöschten/verschobenen Dateien entfernen
            Title = AppName;
            Width = _settings.Width;
            Height = _settings.Height;
            MinWidth = 560;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (_settings.Maximized) WindowState = WindowState.Maximized;
            SetResourceReference(BackgroundProperty, "ChromeBg");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            InitCommands();
            CommandManager.AddPreviewExecutedHandler(this, OnPreviewCommandExecuted);
            CommandManager.AddPreviewCanExecuteHandler(this, OnPreviewCanExecute);

            // ---------------- Markdown-Editor
            _editor.EditorFontSize = _settings.FontSize;
            _editor.SpellCheckEnabled = _settings.SpellCheck;
            _editor.TextEdited += OnTextEdited;
            _editor.SelectionChanged += delegate { OnEditorCaretMoved(); };
            _editor.FileDropped += files => OnFilesDropped(files, false);
            _editor.ContextMenu = new ContextMenu();
            _editor.ContextMenuOpening += delegate { _previewActive = false; BuildContextMenu(_editor.ContextMenu, false); };
            _editor.PreviewMouseWheel += OnEditorMouseWheel;
            _editor.GotKeyboardFocus += delegate { OnEditorFocused(); };

            // ---------------- Vorschau (bearbeitbar)
            _preview.SpellCheck.IsEnabled = _settings.SpellCheck;
            _preview.ContentEdited += OnPreviewEdited;
            _preview.SelectionChanged += delegate { OnPreviewCaretMoved(); };
            _preview.FileDropped += files => OnFilesDropped(files, true);
            _preview.ContextMenu = new ContextMenu();
            _preview.ContextMenuOpening += delegate { _previewActive = true; BuildContextMenu(_preview.ContextMenu, true); };
            _preview.PreviewMouseWheel += OnEditorMouseWheel;
            _preview.GotKeyboardFocus += delegate { _previewActive = true; UpdateStatus(); };
            _previewHost = new Border { Child = _preview, BorderThickness = new Thickness(1, 0, 0, 0) };
            _previewHost.SetResourceReference(Border.BorderBrushProperty, "ChromeBorder");
            _previewHost.SetResourceReference(Border.BackgroundProperty, "EditorBg");

            // ---------------- Suchleiste
            _findBar = BuildFindBar();
            DockPanel.SetDock(_findBar, Dock.Top);
            _editorHost.Children.Add(_findBar);
            _editorHost.Children.Add(_editor);

            // ---------------- Seitenleiste
            _tabOutline = MakeTab("Gliederung", delegate { SetSideMode(SideMode.Outline); });
            _tabFiles = MakeTab("Dateien", delegate { SetSideMode(SideMode.Files); });
            _tabFolder = MakeTab("Ordner", delegate { SetSideMode(SideMode.Folder); });
            var tabs = new UniformGrid { Rows = 1, Margin = new Thickness(6, 6, 6, 4) };
            tabs.Children.Add(_tabOutline);
            tabs.Children.Add(_tabFiles);
            tabs.Children.Add(_tabFolder);

            var searchHint = new TextBlock { Text = "Dateien filtern …", Margin = new Thickness(7, 4, 0, 0), Opacity = 0.5, IsHitTestVisible = false };
            searchHint.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");
            _fileSearchHost.Children.Add(_fileSearch);
            _fileSearchHost.Children.Add(searchHint);
            _fileSearch.TextChanged += delegate
            {
                searchHint.Visibility = _fileSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                FillKnownFiles();
            };

            var sideDock = new DockPanel();
            DockPanel.SetDock(tabs, Dock.Top);
            DockPanel.SetDock(_fileSearchHost, Dock.Top);
            sideDock.Children.Add(tabs);
            sideDock.Children.Add(_fileSearchHost);
            sideDock.Children.Add(_sideList);
            _sideList.PreviewMouseLeftButtonUp += delegate { ActivateSideItem(); };
            _sideList.KeyDown += (s, e) => { if (e.Key == Key.Enter) { ActivateSideItem(); e.Handled = true; } };
            _sidebar = new Border { Child = sideDock, BorderThickness = new Thickness(0, 0, 1, 0) };
            _sidebar.SetResourceReference(Border.BackgroundProperty, "ChromeBg");
            _sidebar.SetResourceReference(Border.BorderBrushProperty, "ChromeBorder");

            // ---------------- Layout
            var grid = new Grid();
            grid.ColumnDefinitions.Add(_colSidebar);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(_colEditor);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(_colPreview);

            _sideSplitter = MakeSplitter();
            _previewSplitter = MakeSplitter();
            Place(grid, _sidebar, 0);
            Place(grid, _sideSplitter, 1);
            Place(grid, _editorHost, 2);
            Place(grid, _previewSplitter, 3);
            Place(grid, _previewHost, 4);

            var root = new DockPanel();
            Menu menu = BuildMenu();
            DockPanel.SetDock(menu, Dock.Top);
            root.Children.Add(menu);
            Border status = BuildStatusBar();
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);
            root.Children.Add(grid);
            Content = root;

            // ---------------- Timer
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _autoSaveTimer.Tick += delegate { _autoSaveTimer.Stop(); TryAutoSave(); };
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _refreshTimer.Tick += delegate { _refreshTimer.Stop(); RefreshDerivedViews(); };
            _previewSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _previewSyncTimer.Tick += delegate { _previewSyncTimer.Stop(); SyncFromPreview(); };

            // ---------------- Start
            _browseFolder = Directory.Exists(_settings.LastFolder ?? "") ? _settings.LastFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ApplyTheme(ThemeCatalog.Find(_settings.Theme));
            SetSidebarVisible(_settings.ShowSidebar);
            ViewMode startMode;
            if (!Enum.TryParse(_settings.ViewMode, out startMode)) startMode = ViewMode.Both;
            SetViewMode(startMode, false);
            SetSideMode(SideMode.Outline);

            SourceInitialized += delegate { ThemeManager.ApplyTitleBar(this); };
            Deactivated += delegate { SyncFromPreview(); TryAutoSave(); };
            Loaded += delegate
            {
                if (fileToOpen != null) OpenFile(fileToOpen);
                else NewDocument();
                FocusEditor();
            };
        }

        // ================================================================ Befehle & Tastenkürzel

        void InitCommands()
        {
            const ModifierKeys C = ModifierKeys.Control;
            const ModifierKeys CS = ModifierKeys.Control | ModifierKeys.Shift;
            const ModifierKeys N = ModifierKeys.None;

            _cNew = Reg("Neu", C, Key.N, delegate { if (ConfirmDiscard()) NewDocument(); });
            _cOpen = Reg("Öffnen…", C, Key.O, OpenWithDialog);
            _cSave = Reg("Speichern", C, Key.S, delegate { Save(); });
            _cSaveAs = Reg("Speichern unter…", CS, Key.S, delegate { SaveAs(); });

            // Strg+Z / Strg+Y behandeln Editor und Vorschau jeweils selbst; das Menü leitet ins aktive Feld.
            _cUndo = Reg("Rückgängig", C, Key.None, delegate { _editor.Undo(); });
            _cUndo.Preview = pv => pv.Undo();
            _cUndo.Gesture = "Strg+Z";
            _cRedo = Reg("Wiederholen", C, Key.None, delegate { _editor.Redo(); });
            _cRedo.Preview = pv => pv.Redo();
            _cRedo.Gesture = "Strg+Y";
            _cFind = Reg("Suchen…", C, Key.F, ShowFindBar);
            _cFindNext = Reg("Weitersuchen", N, Key.F3, delegate { FindNext(true); });
            Reg("Rückwärts suchen", ModifierKeys.Shift, Key.F3, delegate { FindNext(false); });

            _cHeadings = new Cmd[6];
            for (int i = 1; i <= 6; i++)
            {
                int level = i;
                _cHeadings[i - 1] = Fmt("Überschrift " + level, C, Key.D0 + level, ed => Formatter.SetHeading(ed, level), pv => pv.SetHeading(level));
            }
            _cNormal = Fmt("Standardtext", C, Key.D0, ed => Formatter.SetHeading(ed, 0), pv => pv.SetHeading(0));
            _cBold = Fmt("Fett", C, Key.B, Formatter.Bold, pv => pv.ToggleBold());
            _cItalic = Fmt("Kursiv", C, Key.I, Formatter.Italic, pv => pv.ToggleItalic());
            _cUnderline = Fmt("Unterstrichen", C, Key.U, Formatter.Underline, pv => pv.ToggleUnderline());
            _cStrike = Fmt("Durchgestrichen", CS, Key.X, Formatter.Strike, pv => pv.ToggleStrike());
            _cCode = Fmt("Inline-Code", CS, Key.C, Formatter.InlineCode, pv => pv.ToggleCode());
            _cClear = Fmt("Formatierung entfernen", C, Key.Space, Formatter.ClearFormatting, pv => pv.ClearFormatting());
            _cLink = Fmt("Link", C, Key.K, Formatter.InsertLink, PreviewInsertLink);
            _cEmoji = Reg("Emoji…", CS, Key.E, ShowEmojiPicker);

            // Strukturelle Befehle laufen auch aus der Vorschau heraus über den Markdown-Editor.
            _cRule = Fmt("Trennlinie", CS, Key.H, Formatter.InsertRule, null);
            _cTable = Reg("Tabelle einfügen…", C, Key.T, delegate { Formatter.InsertOrEditTable(_editor, this); FocusEditor(); });
            _cTable.Structural = true;
            _cImage = Reg("Bild…", CS, Key.I, InsertImage);
            _cImage.Structural = true;
            _cBullet = Fmt("Aufzählung", CS, Key.D8, Formatter.ToggleBullet, null);
            _cOrdered = Fmt("Nummerierung", CS, Key.D7, Formatter.ToggleOrdered, null);
            _cTask = Fmt("Aufgabenliste", CS, Key.T, Formatter.ToggleTask, null);
            _cTaskDone = Fmt("Aufgabe erledigt", C, Key.Enter, Formatter.ToggleTaskDone, pv => pv.ToggleTaskDone());
            _cIndent = Fmt("Einrücken", N, Key.None, ed => ed.HandleTab(false), pv => pv.Indent(false));
            _cIndent.Gesture = "Tab";
            _cOutdent = Fmt("Ausrücken", N, Key.None, ed => ed.HandleTab(true), pv => pv.Indent(true));
            _cOutdent.Gesture = "Umschalt+Tab";
            _cQuote = Fmt("Zitat", CS, Key.Q, Formatter.ToggleQuote, null);
            _cCodeBlock = Fmt("Codeblock", CS, Key.K, Formatter.ToggleCodeBlock, null);

            _cSidebar = Reg("Seitenleiste", N, Key.F9, delegate { SetSidebarVisible(_sidebar.Visibility != Visibility.Visible); });
            _cViewCycle = Reg("Ansicht wechseln", N, Key.F12, CycleViewMode);
            _cViewBoth = Reg("Markdown und Vorschau", CS, Key.D1, delegate { SetViewMode(ViewMode.Both, true); });
            _cViewMarkdown = Reg("Nur Markdown", CS, Key.D2, delegate { SetViewMode(ViewMode.Markdown, true); });
            _cViewPreview = Reg("Nur Vorschau", CS, Key.D3, delegate { SetViewMode(ViewMode.Preview, true); });
            _cFullScreen = Reg("Vollbild", N, Key.F11, ToggleFullScreen);
            _cDark = Reg("Dark Mode / Light Mode", CS, Key.D, ToggleDarkMode);
            _cZoomIn = Reg("Vergrößern", C, Key.OemPlus, delegate { Zoom(1); });
            _cZoomOut = Reg("Verkleinern", C, Key.OemMinus, delegate { Zoom(-1); });
            _cZoomReset = Reg("Originalgröße", C, Key.NumPad0, delegate { _editor.EditorFontSize = 15; AfterZoom(); });
            Reg("Vergrößern", C, Key.Add, delegate { Zoom(1); });
            Reg("Verkleinern", C, Key.Subtract, delegate { Zoom(-1); });

            InitDictationCommands();
        }

        Cmd Reg(string header, ModifierKeys mods, Key key, Action run)
        {
            var c = new Cmd { Header = header, Mods = mods, Key = key, Run = run };
            _commands.Add(c);
            return c;
        }

        /// <summary>Formatierungsbefehl: im Markdown-Editor über <paramref name="action"/>, in der Vorschau über <paramref name="preview"/>.</summary>
        Cmd Fmt(string header, ModifierKeys mods, Key key, Action<MarkdownEditorBox> action, Action<PreviewEditorBox> preview)
        {
            Cmd c = Reg(header, mods, key, delegate
            {
                action(_editor);
                FocusEditor();
            });
            c.Preview = preview;
            c.Structural = preview == null;
            return c;
        }

        /// <summary>Führt einen Befehl im aktiven Feld aus (Markdown-Editor oder Vorschau).</summary>
        void Execute(Cmd c)
        {
            bool inPreview = _previewActive && _previewHost.Visibility == Visibility.Visible;
            if (inPreview && c.Preview != null)
            {
                c.Preview(_preview);
                _preview.Focus();
                return;
            }
            if (inPreview && c.Structural)
            {
                // Aktuellen Vorschau-Block im Markdown markieren und den Befehl dort ausführen
                SyncFromPreview();
                Block block = _preview.TopLevelBlockAtCaret();
                BlockInfo info = _preview.InfoOf(block);
                if (info != null && info.Start >= 0 && info.End > info.Start)
                {
                    int last = info.End - 1;
                    _editor.Select(new TextPos(info.Start, 0), new TextPos(last, _editor.LineText(last).Length));
                }
                _previewActive = false;
            }
            c.Run();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys mods = Keyboard.Modifiers;
            foreach (Cmd c in _commands)
            {
                if (c.Key == Key.None || c.Key != key || c.Mods != mods) continue;
                e.Handled = true;
                Execute(c);
                return;
            }
            if (key == Key.Escape && TryCancelDictation())
            {
                e.Handled = true;
                return;
            }
            if (key == Key.Escape && _findBar.Visibility == Visibility.Visible)
            {
                HideFindBar();
                e.Handled = true;
                return;
            }
            if (key == Key.Escape && _fullScreen)
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(e);
        }

        static string GestureText(Cmd c)
        {
            if (c.Gesture != null) return c.Gesture;
            if (c.Key == Key.None) return "";
            string s = "";
            if ((c.Mods & ModifierKeys.Control) != 0) s += "Strg+";
            if ((c.Mods & ModifierKeys.Shift) != 0) s += "Umschalt+";
            if ((c.Mods & ModifierKeys.Alt) != 0) s += "Alt+";
            Key k = c.Key;
            if (k >= Key.D0 && k <= Key.D9) s += ((int)k - (int)Key.D0).ToString();
            else if (k >= Key.NumPad0 && k <= Key.NumPad9) s += "Num " + ((int)k - (int)Key.NumPad0);
            else if (k == Key.OemPlus || k == Key.Add) s += "+";
            else if (k == Key.OemMinus || k == Key.Subtract) s += "−";
            else if (k == Key.Space) s += "Leertaste";
            else if (k == Key.Enter) s += "Enter";
            else s += k.ToString();
            return s;
        }

        MenuItem MI(Cmd c)
        {
            var mi = new MenuItem { Header = c.Header, InputGestureText = GestureText(c) };
            mi.Click += delegate { Execute(c); };
            return mi;
        }

        static MenuItem MI(string header, Action run)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += delegate { run(); };
            return mi;
        }

        // ================================================================ Menüs

        Menu BuildMenu()
        {
            var menu = new Menu();

            var file = new MenuItem { Header = "_Datei" };
            file.Items.Add(MI(_cNew));
            file.Items.Add(MI(_cOpen));
            _recentMenu = new MenuItem { Header = "Zuletzt verwendet" };
            _recentMenu.Items.Add(new MenuItem());   // Platzhalter, damit sich das Untermenü öffnen lässt
            _recentMenu.SubmenuOpened += delegate { FillRecentMenu(); };
            file.Items.Add(_recentMenu);
            file.Items.Add(new Separator());
            file.Items.Add(MI(_cSave));
            file.Items.Add(MI(_cSaveAs));
            _autoSaveItem = new MenuItem { Header = "Auto-Save", IsCheckable = true, IsChecked = _settings.AutoSave };
            _autoSaveItem.Click += delegate { _settings.AutoSave = _autoSaveItem.IsChecked; UpdateStatus(); TryAutoSave(); };
            file.Items.Add(_autoSaveItem);
            file.Items.Add(new Separator());
            file.Items.Add(MI("Speicherort im Explorer öffnen", delegate { ShowInExplorer(_path); }));
            file.Items.Add(new Separator());
            file.Items.Add(MI("Beenden", Close));
            menu.Items.Add(file);

            var edit = new MenuItem { Header = "_Bearbeiten" };
            edit.Items.Add(MI(_cUndo));
            edit.Items.Add(MI(_cRedo));
            edit.Items.Add(new Separator());
            AddClipboardItems(edit.Items, null);
            edit.Items.Add(new Separator());
            edit.Items.Add(MI(_cFind));
            edit.Items.Add(MI(_cFindNext));
            menu.Items.Add(edit);

            // Neu aufbauen beim Öffnen, damit Häkchen und Tabellen-Eintrag zum Cursor passen
            var format = new MenuItem { Header = "_Format" };
            AddFormatItems(format.Items);
            format.SubmenuOpened += (s, e) =>
            {
                if (e.OriginalSource != format) return;
                format.Items.Clear();
                AddFormatItems(format.Items);
            };
            menu.Items.Add(format);

            menu.Items.Add(BuildDictationMenu());

            var view = new MenuItem { Header = "_Ansicht" };
            _viewBothItem = MI(_cViewBoth);
            _viewMarkdownItem = MI(_cViewMarkdown);
            _viewPreviewItem = MI(_cViewPreview);
            var cycle = MI(_cViewCycle);
            cycle.Header = "Nächste Ansicht";
            _sidebarItem = MI(_cSidebar);
            _fullScreenItem = MI(_cFullScreen);
            _spellItem = MI("Rechtschreibprüfung", ToggleSpellCheck);
            view.Items.Add(_viewBothItem);
            view.Items.Add(_viewMarkdownItem);
            view.Items.Add(_viewPreviewItem);
            view.Items.Add(cycle);
            view.Items.Add(new Separator());
            view.Items.Add(_sidebarItem);
            view.Items.Add(_fullScreenItem);
            view.Items.Add(_spellItem);
            view.Items.Add(new Separator());
            view.Items.Add(MI(_cZoomIn));
            view.Items.Add(MI(_cZoomOut));
            view.Items.Add(MI(_cZoomReset));
            view.Items.Add(new Separator());
            view.Items.Add(MI(_cDark));
            menu.Items.Add(view);

            _themeMenu = new MenuItem { Header = "_Theme" };
            foreach (Theme t in ThemeCatalog.All)
            {
                Theme theme = t;
                var item = new MenuItem { Header = t.Name + (t.IsDark ? "  ☾" : "  ☀"), Tag = t };
                item.Click += delegate { ApplyTheme(theme); };
                _themeMenu.Items.Add(item);
            }
            menu.Items.Add(_themeMenu);

            var help = new MenuItem { Header = "_Hilfe" };
            help.Items.Add(MI("Tastenkürzel", ShowShortcuts));
            help.Items.Add(MI("Markdown-Spickzettel einfügen", InsertCheatSheet));
            help.Items.Add(new Separator());
            help.Items.Add(MI("Über " + AppName, delegate
            {
                MessageBox.Show(this,
                    AppName + "\nSchlanker Editor für Markdown-Dateien – Markdown und Vorschau sind beide bearbeitbar.\n\n" +
                    "Emoji-Grafiken: Microsoft Fluent Emoji (MIT-Lizenz) und Twemoji (CC-BY 4.0, © Twitter/X und Mitwirkende).\n" +
                    "Emoji-Namen: Unicode CLDR.\n\n.NET Framework " + Environment.Version, AppName);
            }));
            menu.Items.Add(help);
            return menu;
        }

        void AddClipboardItems(ItemCollection items, IInputElement target)
        {
            items.Add(new MenuItem { Header = "Ausschneiden", Command = ApplicationCommands.Cut, CommandTarget = target, InputGestureText = "Strg+X" });
            items.Add(new MenuItem { Header = "Kopieren", Command = ApplicationCommands.Copy, CommandTarget = target, InputGestureText = "Strg+C" });
            items.Add(new MenuItem { Header = "Einfügen", Command = ApplicationCommands.Paste, CommandTarget = target, InputGestureText = "Strg+V" });
            items.Add(new MenuItem { Header = "Alles auswählen", Command = ApplicationCommands.SelectAll, CommandTarget = target, InputGestureText = "Strg+A" });
        }

        /// <summary>
        /// Formatierungsgruppen für Menüleiste und Kontextmenüs. Häkchen zeigen die Absatzart am Cursor,
        /// Listenbefehle sind nur in Listen aktiv, in Tabellen gibt es „Tabelle bearbeiten…“.
        /// </summary>
        void AddFormatItems(ItemCollection items)
        {
            string kind = CaretBlockKind();
            bool inList = kind == "bullet" || kind == "ordered" || kind == "task" || kind == "done";

            var font = new MenuItem { Header = "Schrift" };
            font.Items.Add(MI(_cBold));
            font.Items.Add(MI(_cItalic));
            font.Items.Add(MI(_cUnderline));
            font.Items.Add(MI(_cStrike));
            font.Items.Add(MI(_cCode));
            font.Items.Add(new Separator());
            font.Items.Add(MI(_cClear));
            items.Add(font);

            var level = new MenuItem { Header = "Textebene" };
            level.Items.Add(Opt(_cNormal, kind == "p"));
            for (int i = 0; i < _cHeadings.Length; i++) level.Items.Add(Opt(_cHeadings[i], kind == "h" + (i + 1)));
            level.Items.Add(new Separator());
            level.Items.Add(Opt(_cQuote, kind == "quote"));
            level.Items.Add(Opt(_cCodeBlock, kind == "code"));
            items.Add(level);

            var lists = new MenuItem { Header = "Listen" };
            lists.Items.Add(Opt(_cBullet, kind == "bullet"));
            lists.Items.Add(Opt(_cOrdered, kind == "ordered"));
            lists.Items.Add(Opt(_cTask, kind == "task" || kind == "done"));
            lists.Items.Add(new Separator());
            MenuItem done = Opt(_cTaskDone, kind == "done");
            done.IsEnabled = kind == "task" || kind == "done";
            lists.Items.Add(done);
            MenuItem indent = MI(_cIndent), outdent = MI(_cOutdent);
            indent.IsEnabled = outdent.IsEnabled = inList;
            lists.Items.Add(indent);
            lists.Items.Add(outdent);
            items.Add(lists);

            var insert = new MenuItem { Header = "Hinzufügen" };
            insert.Items.Add(MI(_cLink));
            insert.Items.Add(MI(_cImage));
            insert.Items.Add(MI(_cEmoji));
            insert.Items.Add(MI(_cRule));
            if (kind != "table") insert.Items.Add(MI(_cTable));
            items.Add(insert);

            if (kind == "table")
            {
                items.Add(BuildTableMenu());
            }
        }

        /// <summary>Zeilen/Spalten an der Zelle am Cursor einfügen oder löschen, dazu der Tabellen-Dialog.</summary>
        MenuItem BuildTableMenu()
        {
            int row, col;
            bool inPreview = _previewActive && _previewHost.Visibility == Visibility.Visible;
            if (!(inPreview ? _preview.TableCellAtCaret(out row, out col) : Formatter.TableCellAt(_editor, out row, out col)))
                row = col = 0;

            var menu = new MenuItem { Header = "Tabelle" };
            menu.Items.Add(TableOp("Zeile oberhalb einfügen", row > 0, t => { t.InsertRow(row); return row; }));
            menu.Items.Add(TableOp("Zeile unterhalb einfügen", true, t => { t.InsertRow(row + 1); return row + 1; }));
            menu.Items.Add(TableOp("Zeile löschen", row > 0, t => { t.RemoveRow(row); return Math.Min(row, t.Rows.Count - 1); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(TableOp("Spalte links einfügen", true, t => { t.InsertColumn(col); return row; }));
            menu.Items.Add(TableOp("Spalte rechts einfügen", true, t => { t.InsertColumn(col + 1); return row; }));
            menu.Items.Add(TableOp("Spalte löschen", true, t => { t.RemoveColumn(Math.Min(col, t.ColumnCount - 1)); return row; }));
            menu.Items.Add(new Separator());
            MenuItem dialog = MI(_cTable);
            dialog.Header = "Tabelle bearbeiten…";
            menu.Items.Add(dialog);
            return menu;
        }

        MenuItem TableOp(string header, bool enabled, Func<MarkdownTable, int> change)
        {
            // Läuft wie die anderen Strukturbefehle aus der Vorschau heraus über das Markdown
            var c = new Cmd { Header = header, Structural = true, Run = delegate { Formatter.ModifyTable(_editor, change); FocusEditor(); } };
            MenuItem mi = MI(c);
            mi.IsEnabled = enabled;
            return mi;
        }

        MenuItem Opt(Cmd c, bool active)
        {
            MenuItem mi = MI(c);
            mi.IsChecked = active;
            return mi;
        }

        /// <summary>Absatzart am Cursor: "p", "h1"…"h6", "quote", "code", "bullet", "ordered", "task", "done" oder "table".</summary>
        string CaretBlockKind()
        {
            if (_previewActive && _previewHost.Visibility == Visibility.Visible)
            {
                BlockInfo info = _preview.InfoOf(_preview.TopLevelBlockAtCaret());
                if (info == null) return "p";
                switch (info.Kind)
                {
                    case BlockKind.Heading: return "h" + info.Level;
                    case BlockKind.Quote: return "quote";
                    case BlockKind.Code: return "code";
                    case BlockKind.Table: return "table";
                    case BlockKind.List:
                        Paragraph p = _preview.CaretPosition.Paragraph;
                        if (p == null) return "bullet";
                        bool done;
                        if (PreviewEditorBox.IsTask(p, out done)) return done ? "done" : "task";
                        var item = p.Parent as ListItem;
                        return item != null && item.List != null && item.List.MarkerStyle == TextMarkerStyle.Decimal ? "ordered" : "bullet";
                    default: return "p";
                }
            }

            int line = _editor.CaretPos().Line;
            LineInfo[] infos = _editor.LineInfos;
            if (line < 0 || line >= infos.Length) return "p";
            LineInfo li = infos[line];
            switch (li.Kind)
            {
                case LineKind.Heading: return "h" + li.Level;
                case LineKind.Quote: return "quote";
                case LineKind.Fence: case LineKind.Code: return "code";
                case LineKind.Ordered: return "ordered";
                case LineKind.TableHeader: case LineKind.TableSeparator: case LineKind.TableRow: return "table";
                case LineKind.Bullet:
                    string s = _editor.LineText(line);
                    string prefix = s.Substring(0, Math.Min(li.PrefixLength, s.Length));
                    if (prefix.Contains("[ ]")) return "task";
                    if (prefix.Contains("[x]") || prefix.Contains("[X]")) return "done";
                    return "bullet";
                default: return "p";
            }
        }

        /// <summary>Bild auswählen und als ![Name](Pfad) einfügen – relativ zur Notiz, wenn sie schon gespeichert ist.</summary>
        void InsertImage()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Bild einfügen",
                Filter = "Bilder|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Alle Dateien|*.*",
                InitialDirectory = CurrentFolder()
            };
            if (dlg.ShowDialog(this) != true) { FocusEditor(); return; }
            InsertImageLinks(new[] { dlg.FileName });
        }

        static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

        static bool IsImageFile(string path)
        {
            return ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        /// <summary>Fügt Bildlinks am Cursor ein (aus der Vorschau heraus am Ende des Blocks).</summary>
        void InsertImageLinks(IEnumerable<string> files)
        {
            Execute(new Cmd
            {
                Structural = true,
                Run = delegate
                {
                    foreach (string f in files) Formatter.InsertImage(_editor, Path.GetFileNameWithoutExtension(f), ImageUrl(f));
                    FocusEditor();
                }
            });
        }

        /// <summary>Pfad für Markdown: relativ zur Notiz (wenn gespeichert), mit / und ohne Leerzeichen/Klammern.</summary>
        string ImageUrl(string file)
        {
            string url = file;
            if (_path != null)
            {
                var baseUri = new Uri(Path.GetDirectoryName(_path).TrimEnd('\\') + "\\");
                Uri rel = baseUri.MakeRelativeUri(new Uri(file));
                if (!rel.IsAbsoluteUri) url = Uri.UnescapeDataString(rel.OriginalString);
            }
            return url.Replace('\\', '/').Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");
        }

        /// <summary>Bilddateien werden verlinkt, alles andere wie bisher geöffnet.</summary>
        void OnFilesDropped(string[] files, bool fromPreview)
        {
            List<string> images = files.Where(IsImageFile).ToList();
            if (images.Count == 0)
            {
                if (ConfirmDiscard()) OpenFile(files[0]);
                return;
            }
            _previewActive = fromPreview;
            InsertImageLinks(images);
        }

        /// <summary>
        /// Strg+V bzw. „Einfügen“ mit einem Bild in der Zwischenablage: Screenshot als PNG in „bilder“ neben der Notiz
        /// speichern und verlinken; im Explorer kopierte Bilddateien werden direkt verlinkt. Text hat Vorrang.
        /// </summary>
        void OnPreviewCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command != ApplicationCommands.Paste || (e.OriginalSource != _editor && e.OriginalSource != _preview)) return;
            IDataObject data;
            try { data = Clipboard.GetDataObject(); }
            catch (Exception) { return; }
            if (data == null || data.GetDataPresent(DataFormats.UnicodeText)) return;

            var dropped = data.GetData(DataFormats.FileDrop) as string[];
            List<string> files = dropped != null ? dropped.Where(IsImageFile).ToList() : new List<string>();
            BitmapSource image = files.Count == 0 ? ClipboardImage(data) : null;
            if (files.Count == 0 && image == null) return;

            e.Handled = true;
            _previewActive = e.OriginalSource == _preview;
            if (image != null)
            {
                string file = SaveImageNextToNote(image);
                if (file == null) return;
                files.Add(file);
                ShowNotice("Bild gespeichert: " + ImageUrl(file).Replace("%20", " "));
            }
            InsertImageLinks(files);
        }

        /// <summary>„Einfügen“ auch dann anbieten, wenn nur ein Bild bzw. Dateien in der Zwischenablage liegen.</summary>
        void OnPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            if (e.Command != ApplicationCommands.Paste || (e.OriginalSource != _editor && e.OriginalSource != _preview)) return;
            try
            {
                if (!Clipboard.ContainsText() && (Clipboard.ContainsImage() || Clipboard.ContainsFileDropList()))
                {
                    e.CanExecute = true;
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                // Zwischenablage gerade belegt
            }
        }

        static BitmapSource ClipboardImage(IDataObject data)
        {
            try
            {
                var png = data.GetData("PNG") as MemoryStream;
                if (png != null) return BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var bmp = data.GetData(DataFormats.Bitmap) as BitmapSource;
                // Bitmap-Daten der Zwischenablage haben oft einen leeren Alphakanal → ohne Alpha übernehmen
                return bmp != null ? new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Speichert als PNG unter „bilder\Notizname-Datum-Uhrzeit.png“; ungespeicherte Notizen werden vorher gespeichert.</summary>
        string SaveImageNextToNote(BitmapSource image)
        {
            if (_path == null)
            {
                if (MessageBox.Show(this, "Bilder werden neben der Notiz im Ordner „bilder“ abgelegt. Dazu muss die Notiz zuerst gespeichert werden.",
                        AppName, MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK || !SaveAs())
                    return null;
            }
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(_path), "bilder");
                Directory.CreateDirectory(dir);
                string stem = Path.GetFileNameWithoutExtension(_path) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string file = Path.Combine(dir, stem + ".png");
                for (int n = 2; File.Exists(file); n++) file = Path.Combine(dir, stem + "-" + n + ".png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (FileStream fs = File.Create(file)) encoder.Save(fs);
                return file;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Bild konnte nicht gespeichert werden:\n" + ex.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        /// <summary>Link am Cursor öffnen, Adresse ändern oder Link entfernen (Text bleibt).</summary>
        void AddLinkItems(ItemCollection items, bool forPreview)
        {
            string url, text;
            Hyperlink link = null;
            if (forPreview)
            {
                link = _preview.LinkAtCaret();
                if (link == null) return;
                url = link.Tag as string ?? (link.NavigateUri != null ? link.NavigateUri.OriginalString : "");
                text = new TextRange(link.ContentStart, link.ContentEnd).Text;
                if (text == url) text = null;   // nackte Adresse
            }
            else if (!Formatter.LinkAt(_editor, out text, out url)) return;

            string baseDir = _path != null ? Path.GetDirectoryName(_path) : null;
            MenuItem open = MI("Link öffnen", delegate { PreviewRenderer.OpenUrl(url, baseDir); });
            open.InputGestureText = forPreview ? "Strg+Klick" : "";
            open.ToolTip = url;
            items.Add(open);
            items.Add(MI("Link bearbeiten…", delegate
            {
                var dlg = new InputDialog("Link bearbeiten", "Adresse (URL oder Dateipfad):", url) { Owner = this };
                if (dlg.ShowDialog() != true || dlg.Value.Trim().Length == 0) return;
                if (link != null) { _preview.SetLinkUrl(link, dlg.Value.Trim()); _preview.Focus(); }
                else { Formatter.SetLinkUrl(_editor, dlg.Value.Trim()); FocusEditor(); }
            }));
            if (text != null)
            {
                items.Add(MI("Link entfernen", delegate
                {
                    if (link != null) { _preview.RemoveLink(link); _preview.Focus(); }
                    else { Formatter.RemoveLink(_editor); FocusEditor(); }
                }));
            }
            items.Add(new Separator());
        }

        void BuildContextMenu(ContextMenu menu, bool forPreview)
        {
            menu.Items.Clear();
            RichTextBox box = forPreview ? (RichTextBox)_preview : _editor;

            if (box.SpellCheck.IsEnabled)
            {
                SpellingError error = box.GetSpellingError(box.CaretPosition);
                if (error != null)
                {
                    int count = 0;
                    foreach (string suggestion in error.Suggestions)
                    {
                        if (count++ >= 5) break;
                        string s = suggestion;
                        var item = new MenuItem { Header = s, FontWeight = FontWeights.SemiBold };
                        item.Click += delegate { error.Correct(s); };
                        menu.Items.Add(item);
                    }
                    if (count == 0) menu.Items.Add(new MenuItem { Header = "(keine Vorschläge)", IsEnabled = false });
                    menu.Items.Add(MI("Ignorieren", delegate { error.IgnoreAll(); }));
                    menu.Items.Add(new Separator());
                }
            }

            AddLinkItems(menu.Items, forPreview);
            AddFormatItems(menu.Items);
            menu.Items.Add(new Separator());
            menu.Items.Add(MI(_cDictate));
            menu.Items.Add(new Separator());
            AddClipboardItems(menu.Items, box);
        }

        void FillRecentMenu()
        {
            _recentMenu.Items.Clear();
            int removed = _settings.PruneMissingFiles();
            if (removed > 0) ShowNotice(removed + " Verknüpfung(en) entfernt – Datei nicht mehr vorhanden");
            if (_settings.KnownFiles.Count == 0)
            {
                _recentMenu.Items.Add(new MenuItem { Header = "(leer)", IsEnabled = false });
                return;
            }
            foreach (KnownFile f in _settings.KnownFiles.Take(12))
            {
                string path = f.Path;
                var item = new MenuItem { Header = Path.GetFileName(path), ToolTip = path, InputGestureText = FriendlyDate(f.LastUsed) };
                item.Click += delegate { OpenKnownFile(path); };
                _recentMenu.Items.Add(item);
            }
            _recentMenu.Items.Add(new Separator());
            _recentMenu.Items.Add(MI("Alle Dateien anzeigen …", delegate { SetSidebarVisible(true); SetSideMode(SideMode.Files); }));
        }

        // ================================================================ Layout-Helfer

        static void Place(Grid grid, UIElement element, int column)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        static GridSplitter MakeSplitter()
        {
            var s = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Columns,
                Focusable = false
            };
            s.SetResourceReference(BackgroundProperty, "ChromeBg");
            return s;
        }

        static Button MakeTab(string text, Action click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 2, 0), Padding = new Thickness(4, 4, 4, 4), BorderThickness = new Thickness(0), Focusable = false };
            b.Click += delegate { click(); };
            return b;
        }

        Border BuildFindBar()
        {
            var panel = new DockPanel { Margin = new Thickness(10, 6, 10, 6) };
            var label = new TextBlock { Text = "Suchen:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");
            var prev = new Button { Content = "▲", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Vorheriger Treffer (Umschalt+F3)" };
            prev.Click += delegate { FindNext(false); };
            var next = new Button { Content = "▼", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Nächster Treffer (Enter / F3)" };
            next.Click += delegate { FindNext(true); };
            var close = new Button { Content = "✕", Padding = new Thickness(8, 2, 8, 2), ToolTip = "Schließen (Esc)", Margin = new Thickness(0) };
            close.Click += delegate { HideFindBar(); };
            DockPanel.SetDock(close, Dock.Right);
            panel.Children.Add(close);
            panel.Children.Add(label);
            panel.Children.Add(_findBox);
            panel.Children.Add(prev);
            panel.Children.Add(next);
            _findInfo.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");
            panel.Children.Add(_findInfo);

            _findBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    FindNext((Keyboard.Modifiers & ModifierKeys.Shift) == 0);
                    e.Handled = true;
                }
            };

            var bar = new Border { Child = panel, BorderThickness = new Thickness(0, 0, 0, 1), Visibility = Visibility.Collapsed };
            bar.SetResourceReference(Border.BackgroundProperty, "ChromeBg");
            bar.SetResourceReference(Border.BorderBrushProperty, "ChromeBorder");
            return bar;
        }

        Border BuildStatusBar()
        {
            var dock = new DockPanel { Margin = new Thickness(12, 3, 12, 4) };
            foreach (TextBlock tb in new[] { _statusLeft, _statusRight })
            {
                tb.FontSize = 12;
                tb.SetResourceReference(TextBlock.ForegroundProperty, "ChromeFg");
                tb.Opacity = 0.8;
                tb.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            DockPanel.SetDock(_statusRight, Dock.Right);
            dock.Children.Add(_statusRight);
            dock.Children.Add(_statusLeft);
            var border = new Border { Child = dock, BorderThickness = new Thickness(0, 1, 0, 0) };
            border.SetResourceReference(Border.BackgroundProperty, "ChromeBg");
            border.SetResourceReference(Border.BorderBrushProperty, "ChromeBorder");
            return border;
        }

        // ================================================================ Datei-Operationen

        void NewDocument()
        {
            _file = new DocumentFile { Text = "" };
            _path = null;
            _editor.SetText("");
            _dirty = false;
            _lastSaveInfo = "";
            UpdateTitle();
            RefreshDerivedViews();
            RenderPreviewIfVisible();
        }

        void OpenWithDialog()
        {
            if (!ConfirmDiscard()) return;
            var dlg = new OpenFileDialog { Filter = FileFilter, InitialDirectory = CurrentFolder() };
            if (dlg.ShowDialog(this) == true) OpenFile(dlg.FileName);
        }

        /// <summary>Öffnet eine Datei aus der Liste; fehlt sie am gespeicherten Ort, wird die Verknüpfung entfernt.</summary>
        void OpenKnownFile(string path)
        {
            if (!File.Exists(path))
            {
                _settings.ForgetFile(path);
                _settings.Save();
                ShowNotice("„" + Path.GetFileName(path) + "“ wurde nicht gefunden – Verknüpfung entfernt");
                if (_sideMode == SideMode.Files) FillKnownFiles();
                return;
            }
            if (string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return;
            if (ConfirmDiscard()) OpenFile(path);
        }

        void OpenFile(string path)
        {
            if (!File.Exists(path))
            {
                _settings.ForgetFile(path);
                ShowNotice("„" + Path.GetFileName(path) + "“ wurde nicht gefunden – Verknüpfung entfernt");
                if (_sideMode == SideMode.Files) FillKnownFiles();
                return;
            }
            try
            {
                _file = DocumentFile.Load(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Die Datei konnte nicht geöffnet werden:\n" + ex.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _path = Path.GetFullPath(path);
            _editor.SetText(_file.Text);
            _dirty = false;
            _lastSaveInfo = "";
            _browseFolder = Path.GetDirectoryName(_path);
            _settings.LastFolder = _browseFolder;
            _settings.TouchFile(_path);
            _settings.Save();
            UpdateTitle();
            RefreshDerivedViews();
            RenderPreviewIfVisible();
            if (_sideMode != SideMode.Outline) RefreshSidebar();
            FocusEditor();
        }

        bool Save()
        {
            SyncFromPreview();
            return _path == null ? SaveAs() : WriteFile(_path, false);
        }

        bool SaveAs()
        {
            SyncFromPreview();
            var dlg = new SaveFileDialog
            {
                Filter = FileFilter,
                DefaultExt = ".md",
                AddExtension = true,
                InitialDirectory = CurrentFolder(),
                FileName = _path != null ? Path.GetFileName(_path) : SuggestFileName()
            };
            if (dlg.ShowDialog(this) != true) return false;
            return WriteFile(dlg.FileName, false);
        }

        bool WriteFile(string path, bool silent)
        {
            try
            {
                _file.Text = _editor.GetText();
                _file.Save(path);
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show(this, "Speichern fehlgeschlagen:\n" + ex.Message, AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                else
                    _lastSaveInfo = "Auto-Save fehlgeschlagen";
                UpdateStatus();
                return false;
            }
            bool pathChanged = !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase);
            _path = Path.GetFullPath(path);
            _dirty = false;
            _lastSaveInfo = (silent ? "Automatisch gespeichert " : "Gespeichert ") + DateTime.Now.ToString("HH:mm");
            _settings.TouchFile(_path);   // Ablageort merken (auch für neu erstellte Dateien)
            _settings.LastFolder = Path.GetDirectoryName(_path);
            _settings.Save();
            if (pathChanged)
            {
                _browseFolder = _settings.LastFolder;
                RenderPreviewIfVisible(); // Basisordner für relative Bilder hat sich geändert
            }
            if (_sideMode != SideMode.Outline) RefreshSidebar();
            UpdateTitle();
            return true;
        }

        void TryAutoSave()
        {
            if (_settings.AutoSave && _dirty && _path != null)
                WriteFile(_path, true);
        }

        /// <summary>Fragt bei ungespeicherten Änderungen nach. false = Vorgang abbrechen.</summary>
        bool ConfirmDiscard()
        {
            SyncFromPreview();
            if (!_dirty) return true;
            if (_settings.AutoSave && _path != null && WriteFile(_path, true)) return true;
            string name = _path != null ? Path.GetFileName(_path) : "Unbenannt";
            MessageBoxResult r = MessageBox.Show(this, "Änderungen an \"" + name + "\" speichern?", AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Save();
            return true;
        }

        string CurrentFolder()
        {
            if (_path != null) return Path.GetDirectoryName(_path);
            return Directory.Exists(_browseFolder ?? "") ? _browseFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        string SuggestFileName()
        {
            // Erste Überschrift als Dateiname vorschlagen
            foreach (string line in _editor.GetLines())
            {
                Match m = Regex.Match(line, @"^#{1,6}\s+(.+)$");
                if (!m.Success) continue;
                string name = Regex.Replace(m.Groups[1].Value, @"[\\/:*?""<>|]", "").Trim();
                if (name.Length > 0) return name.Length > 60 ? name.Substring(0, 60) : name;
            }
            return "Unbenannt";
        }

        void ShowInExplorer(string file)
        {
            try
            {
                if (file != null && File.Exists(file)) Process.Start("explorer.exe", "/select,\"" + file + "\"");
                else Process.Start("explorer.exe", "\"" + CurrentFolder() + "\"");
            }
            catch (Exception)
            {
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!ConfirmDiscard())
            {
                e.Cancel = true;
                return;
            }
            _settings.Maximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _settings.Width = Width;
                _settings.Height = Height;
            }
            _settings.FontSize = _editor.EditorFontSize;
            _settings.Save();
            ShutdownDictation();
            if (_emojiPicker != null) _emojiPicker.Close();
            base.OnClosing(e);
        }

        // ================================================================ Status, Titel, abgeleitete Ansichten

        void OnTextEdited(object sender, EventArgs e)
        {
            if (!_applyingFromPreview) _skipPreviewRender = false; // Markdown wurde direkt bearbeitet
            if (!_dirty)
            {
                _dirty = true;
                UpdateTitle();
            }
            _refreshTimer.Stop();
            _refreshTimer.Start();
            if (_settings.AutoSave)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Start();
            }
        }

        void UpdateTitle()
        {
            string name = _path != null ? Path.GetFileName(_path) : "Unbenannt";
            Title = (_dirty ? "● " : "") + name + " – " + AppName;
            UpdateStatus();
        }

        void ShowNotice(string text)
        {
            _notice = text;
            UpdateStatus();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            t.Tick += delegate { t.Stop(); if (_notice == text) { _notice = ""; UpdateStatus(); } };
            t.Start();
        }

        void UpdateStatus()
        {
            string left = _notice.Length > 0 ? "ⓘ  " + _notice : (_path ?? "Neues Dokument (noch nicht gespeichert)");
            if (_notice.Length == 0 && _lastSaveInfo.Length > 0 && !_dirty) left += "   ·   " + _lastSaveInfo;
            if (_notice.Length == 0 && _settings.AutoSave) left += _path != null ? "   ·   Auto-Save an" : "   ·   Auto-Save nach erstem Speichern";
            _statusLeft.Text = left;

            TextPos caret = _editor.CaretPos();
            string where = _previewActive && _previewHost.Visibility == Visibility.Visible
                ? "Vorschau (Markdown-Zeile " + (caret.Line + 1) + ")"
                : "Zeile " + (caret.Line + 1) + ", Spalte " + (caret.Col + 1);
            _statusRight.Text = where + "   ·   " + _wordCount + " Wörter   ·   " + ThemeManager.Current.Name + "   ·   UTF-8";
        }

        void RefreshDerivedViews()
        {
            List<string> lines = _editor.GetLines();
            int words = 0;
            foreach (string l in lines)
                foreach (string w in l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    if (w.Any(char.IsLetterOrDigit)) words++;
            _wordCount = words;
            UpdateStatus();

            if (_sidebar.Visibility == Visibility.Visible && _sideMode == SideMode.Outline)
                FillOutline(lines);

            // Vorschau nur neu aufbauen, wenn das Markdown direkt bearbeitet wurde
            if (!_skipPreviewRender && !_preview.IsKeyboardFocusWithin)
                RenderPreviewIfVisible(lines);
            else if (_viewMode == ViewMode.Preview && !_previewDirty && _preview.SourceLines != null && !lines.SequenceEqual(_preview.SourceLines))
            {
                // Reine Vorschau (hat immer den Fokus): Markdown wurde über einen Befehl geändert → neu aufbauen
                RenderPreview(lines);
                RevealInPreview(_editor.CaretPos().Line);
            }
            _skipPreviewRender = false;
        }

        // ================================================================ Seitenleiste

        void SetSidebarVisible(bool visible)
        {
            _settings.ShowSidebar = visible;
            _sidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _sideSplitter.Visibility = _sidebar.Visibility;
            if (visible && _colSidebar.Width.Value < 120) _colSidebar.Width = new GridLength(250);
            if (!visible) _colSidebar.Width = new GridLength(0);
            if (_sidebarItem != null) _sidebarItem.IsChecked = visible;
            if (visible) RefreshSidebar();
        }

        void SetSideMode(SideMode mode)
        {
            _sideMode = mode;
            Button[] tabs = { _tabOutline, _tabFiles, _tabFolder };
            for (int i = 0; i < tabs.Length; i++)
            {
                bool active = (int)mode == i;
                tabs[i].SetResourceReference(BackgroundProperty, active ? "MenuHover" : "ChromeBg");
                tabs[i].FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
            _fileSearchHost.Visibility = mode == SideMode.Files ? Visibility.Visible : Visibility.Collapsed;
            RefreshSidebar();
        }

        void RefreshSidebar()
        {
            switch (_sideMode)
            {
                case SideMode.Outline: FillOutline(_editor.GetLines()); break;
                case SideMode.Files: FillKnownFiles(); break;
                default: FillFolderList(); break;
            }
        }

        void FillOutline(List<string> lines)
        {
            _sideList.Items.Clear();
            LineInfo[] infos = _editor.LineInfos;
            for (int i = 0; i < lines.Count && i < infos.Length; i++)
            {
                if (infos[i].Kind != LineKind.Heading) continue;
                string text = lines[i].Substring(Math.Min(infos[i].PrefixLength, lines[i].Length)).Trim();
                text = Regex.Replace(text, @"[*_`~]", "");
                if (text.Length == 0) text = "(leer)";
                _sideList.Items.Add(new ListBoxItem
                {
                    Content = text,
                    Tag = new SideEntry { Line = i },
                    Padding = new Thickness(8 + 14 * (infos[i].Level - 1), 3, 8, 3),
                    FontWeight = infos[i].Level == 1 ? FontWeights.SemiBold : FontWeights.Normal,
                    ToolTip = "Zeile " + (i + 1)
                });
            }
            if (_sideList.Items.Count == 0)
                _sideList.Items.Add(new ListBoxItem { Content = "Keine Überschriften", IsEnabled = false, Opacity = 0.6 });
        }

        /// <summary>Alle mit dem Programm erstellten, geöffneten oder bearbeiteten Dateien (zuletzt verwendet zuerst).</summary>
        void FillKnownFiles()
        {
            _sideList.Items.Clear();
            int removed = _settings.PruneMissingFiles();
            if (removed > 0)
            {
                _settings.Save();
                ShowNotice(removed + " Verknüpfung(en) entfernt – Datei nicht mehr am gespeicherten Ort");
            }

            string filter = _fileSearch.Text.Trim();
            List<KnownFile> files = _settings.KnownFiles
                .Where(f => filter.Length == 0 || f.Path.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .ToList();

            var header = new TextBlock
            {
                Text = files.Count == 0 && filter.Length == 0
                    ? "Noch keine Dateien.\nAlles, was du hier öffnest oder speicherst, erscheint in dieser Liste."
                    : "Zuletzt verwendet  ·  " + files.Count + (files.Count == 1 ? " Datei" : " Dateien"),
                FontSize = 11,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap
            };
            _sideList.Items.Add(new ListBoxItem { Content = header, IsEnabled = false, Padding = new Thickness(6, 2, 6, 4) });

            foreach (KnownFile f in files)
            {
                bool current = string.Equals(f.Path, _path, StringComparison.OrdinalIgnoreCase);
                var name = new TextBlock
                {
                    Text = Path.GetFileName(f.Path),
                    FontWeight = current ? FontWeights.Bold : FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var detail = new TextBlock
                {
                    Text = FriendlyDate(f.LastUsed) + "  ·  " + ShortFolder(Path.GetDirectoryName(f.Path)),
                    FontSize = 11,
                    Opacity = 0.6,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var panel = new StackPanel();
                panel.Children.Add(name);
                panel.Children.Add(detail);

                string path = f.Path;
                var menu = new ContextMenu();
                menu.Items.Add(MI("Öffnen", delegate { OpenKnownFile(path); }));
                menu.Items.Add(MI("Im Explorer anzeigen", delegate { ShowInExplorer(path); }));
                menu.Items.Add(new Separator());
                menu.Items.Add(MI("Aus Liste entfernen", delegate { _settings.ForgetFile(path); _settings.Save(); FillKnownFiles(); }));

                _sideList.Items.Add(new ListBoxItem
                {
                    Content = panel,
                    Tag = new SideEntry { Path = path },
                    ToolTip = path,
                    ContextMenu = menu,
                    Padding = new Thickness(8, 4, 8, 4)
                });
            }
        }

        void FillFolderList()
        {
            _sideList.Items.Clear();
            string folder = _browseFolder;
            if (folder == null || !Directory.Exists(folder)) return;
            try
            {
                DirectoryInfo parent = Directory.GetParent(folder);
                _sideList.Items.Add(new ListBoxItem
                {
                    Content = "📂  " + (Path.GetFileName(folder.TrimEnd('\\')).Length > 0 ? Path.GetFileName(folder.TrimEnd('\\')) : folder),
                    FontWeight = FontWeights.SemiBold,
                    ToolTip = folder,
                    IsEnabled = false
                });
                if (parent != null)
                    _sideList.Items.Add(new ListBoxItem { Content = "⬆  ..", Tag = new SideEntry { Path = parent.FullName, IsFolder = true } });

                foreach (string dir in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
                {
                    var info = new DirectoryInfo(dir);
                    if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    _sideList.Items.Add(new ListBoxItem { Content = "📁  " + info.Name, Tag = new SideEntry { Path = dir, IsFolder = true } });
                }
                foreach (string f in Directory.GetFiles(folder).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (!BrowsableExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                    bool current = string.Equals(f, _path, StringComparison.OrdinalIgnoreCase);
                    _sideList.Items.Add(new ListBoxItem
                    {
                        Content = "📄  " + Path.GetFileName(f),
                        Tag = new SideEntry { Path = f },
                        FontWeight = current ? FontWeights.Bold : FontWeights.Normal,
                        ToolTip = f
                    });
                }
            }
            catch (Exception ex)
            {
                _sideList.Items.Add(new ListBoxItem { Content = "Kein Zugriff: " + ex.Message, IsEnabled = false });
            }
        }

        void ActivateSideItem()
        {
            var item = _sideList.SelectedItem as ListBoxItem;
            if (item == null) return;
            var entry = item.Tag as SideEntry;
            if (entry == null) return;

            if (entry.Line >= 0)
            {
                if (_viewMode == ViewMode.Preview)
                {
                    _editor.RevealLine(entry.Line);
                    RevealInPreview(entry.Line);
                    _preview.Focus();
                }
                else _editor.GoToLine(entry.Line);
            }
            else if (entry.IsFolder)
            {
                _browseFolder = entry.Path;
                Dispatcher.BeginInvoke(new Action(FillFolderList));
            }
            else if (entry.Path != null)
            {
                string p = entry.Path;
                Dispatcher.BeginInvoke(new Action(delegate { OpenKnownFile(p); }));
            }
        }

        static string ShortFolder(string folder)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (folder != null && folder.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                return "~" + folder.Substring(home.Length);
            return folder ?? "";
        }

        static string FriendlyDate(DateTime d)
        {
            DateTime today = DateTime.Today;
            if (d.Date == today) return "heute " + d.ToString("HH:mm");
            if (d.Date == today.AddDays(-1)) return "gestern " + d.ToString("HH:mm");
            return d.ToString("dd.MM.yyyy");
        }

        // ================================================================ Vorschau ↔ Markdown

        /// <summary>Zeigt Markdown, Vorschau oder beide nebeneinander.</summary>
        void SetViewMode(ViewMode mode, bool focus)
        {
            SyncFromPreview();
            bool showEditor = mode != ViewMode.Preview;
            bool showPreview = mode != ViewMode.Markdown;
            _viewMode = mode;
            _settings.ViewMode = mode.ToString();

            _editorHost.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
            _colEditor.MinWidth = showEditor ? 200 : 0;
            _colEditor.Width = showEditor ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _previewHost.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
            _colPreview.Width = showPreview ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _previewSplitter.Visibility = showEditor && showPreview ? Visibility.Visible : Visibility.Collapsed;
            if (!showEditor) HideFindBarSilently();
            UpdateViewMenu();

            if (showPreview) RenderPreview(_editor.GetLines());
            if (!showPreview) _previewActive = false;
            else if (!showEditor) _previewActive = true;
            if (focus) FocusEditor();
            UpdateStatus();
        }

        void CycleViewMode()
        {
            SetViewMode(_viewMode == ViewMode.Both ? ViewMode.Markdown : _viewMode == ViewMode.Markdown ? ViewMode.Preview : ViewMode.Both, true);
        }

        void UpdateViewMenu()
        {
            if (_viewBothItem == null) return;
            _viewBothItem.IsChecked = _viewMode == ViewMode.Both;
            _viewMarkdownItem.IsChecked = _viewMode == ViewMode.Markdown;
            _viewPreviewItem.IsChecked = _viewMode == ViewMode.Preview;
        }

        void HideFindBarSilently()
        {
            _findBar.Visibility = Visibility.Collapsed;
            _findInfo.Text = "";
        }

        /// <summary>
        /// Fokus ins sichtbare Bearbeitungsfeld nach einem Befehl. In der reinen Vorschau wird die Vorschau
        /// aus dem (eventuell gerade geänderten) Markdown neu aufgebaut und der Cursor an die passende Stelle gesetzt.
        /// </summary>
        void FocusEditor()
        {
            if (_viewMode != ViewMode.Preview)
            {
                _previewActive = false;
                _editor.Focus();
                return;
            }
            _previewActive = true;
            SyncFromPreview();
            List<string> lines = _editor.GetLines();
            if (_preview.SourceLines == null || !lines.SequenceEqual(_preview.SourceLines))
            {
                RenderPreview(lines);
                RevealInPreview(_editor.CaretPos().Line);
            }
            _preview.Focus();
        }

        /// <summary>Setzt den Vorschau-Cursor ans Ende des Blocks zur Markdown-Zeile und scrollt dorthin.</summary>
        void RevealInPreview(int line)
        {
            Block b = _preview.BlockForLine(line);
            if (b == null) return;
            try
            {
                TextPointer end = b.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
                if (end != null) _preview.Selection.Select(end, end);
            }
            catch (Exception) { }
            b.BringIntoView();
        }

        void RenderPreviewIfVisible()
        {
            RenderPreviewIfVisible(null);
        }

        void RenderPreviewIfVisible(List<string> lines)
        {
            if (_previewHost.Visibility == Visibility.Visible)
                RenderPreview(lines ?? _editor.GetLines());
        }

        void RenderPreview(List<string> lines)
        {
            var style = new PreviewStyle
            {
                Theme = ThemeManager.Current,
                FontSize = _editor.EditorFontSize,
                BaseDir = _path != null ? Path.GetDirectoryName(_path) : null
            };
            _preview.Load(PreviewRenderer.Render(lines, style), style);
            _previewDirty = false;
            _previewNeedsRerender = false;
        }

        void OnPreviewEdited(object sender, EventArgs e)
        {
            _previewDirty = true;
            _previewSyncTimer.Stop();
            _previewSyncTimer.Start();
        }

        /// <summary>Überträgt Änderungen aus der Vorschau ins Markdown (nur die geänderten Blöcke).</summary>
        void SyncFromPreview()
        {
            _previewSyncTimer.Stop();
            if (!_previewDirty) return;
            _previewDirty = false;

            List<string> current = _editor.GetLines();
            if (!current.SequenceEqual(_preview.SourceLines))
            {
                // Markdown wurde zwischenzeitlich direkt geändert → Vorschau neu aufbauen
                RenderPreview(current);
                return;
            }
            List<string> newLines;
            try
            {
                newLines = MarkdownSerializer.Assemble(_preview.Document, _preview.Map, _preview.SourceLines);
            }
            catch (Exception ex)
            {
                ShowNotice("Vorschau konnte nicht übertragen werden: " + ex.Message);
                RenderPreview(current);
                return;
            }
            _preview.SourceLines = newLines;
            _applyingFromPreview = true;
            _skipPreviewRender = true;
            try
            {
                _editor.ApplyExternalText(newLines);
            }
            finally
            {
                _applyingFromPreview = false;
            }
            _previewNeedsRerender = true;
        }

        void OnEditorFocused()
        {
            _previewActive = false;
            SyncFromPreview();
            // Nach Bearbeitung in der Vorschau: einmal sauber aus dem Markdown neu aufbauen
            if (_previewNeedsRerender) RenderPreviewIfVisible();
            UpdateStatus();
        }

        /// <summary>Cursor in der Vorschau → passende Stelle im Markdown anzeigen.</summary>
        void OnPreviewCaretMoved()
        {
            if (!_preview.IsKeyboardFocusWithin) return;
            BlockInfo info = _preview.InfoOf(_preview.TopLevelBlockAtCaret());
            if (info == null || info.Start < 0 || _previewDirty) { UpdateStatus(); return; }
            if (info.Start != _lastRevealedLine)
            {
                _lastRevealedLine = info.Start;
                _editor.RevealLine(info.Start);
            }
            UpdateStatus();
        }

        /// <summary>Cursor im Markdown → passenden Block in der Vorschau anzeigen.</summary>
        void OnEditorCaretMoved()
        {
            UpdateStatus();
            if (!_editor.IsKeyboardFocusWithin || _previewHost.Visibility != Visibility.Visible) return;
            int line = _editor.CaretPos().Line;
            if (line == _lastRevealedLine) return;
            _lastRevealedLine = line;
            Block b = _preview.BlockForLine(line);
            if (b != null) b.BringIntoView();
        }

        void PreviewInsertLink(PreviewEditorBox pv)
        {
            var dlg = new InputDialog("Link einfügen", "Adresse (URL oder Dateipfad):", "https://") { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Value.Trim().Length > 0) pv.InsertLink(dlg.Value.Trim());
        }

        // ================================================================ Emoji

        void ShowEmojiPicker()
        {
            _emojiToPreview = _previewActive && _previewHost.Visibility == Visibility.Visible;
            RichTextBox target = _emojiToPreview ? (RichTextBox)_preview : _editor;
            if (_emojiPicker == null)
            {
                _emojiPicker = new EmojiPicker(() => _settings.RecentEmojis) { Owner = this };
                _emojiPicker.EmojiChosen += InsertEmoji;
            }

            // Am Cursor öffnen (Bildschirmkoordinaten in DIP)
            Point anchor;
            try
            {
                Rect r = target.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                Point screen = target.PointToScreen(new Point(r.Left, r.Bottom + 4));
                PresentationSource src = PresentationSource.FromVisual(this);
                anchor = src != null ? src.CompositionTarget.TransformFromDevice.Transform(screen) : screen;
            }
            catch (Exception)
            {
                anchor = new Point(Left + 120, Top + 120);
            }
            _emojiPicker.ShowAt(anchor);
        }

        void InsertEmoji(string symbol)
        {
            _settings.TouchEmoji(symbol);
            if (_emojiToPreview)
            {
                _preview.Focus();
                _preview.InsertEmoji(symbol);
            }
            else
            {
                _editor.Focus();
                Formatter.InsertText(_editor, symbol);
                FocusEditor();
            }
        }

        // ================================================================ Suche

        void ShowFindBar()
        {
            // Die Suche arbeitet im Markdown – in der reinen Vorschau dafür beide Bereiche einblenden.
            if (_viewMode == ViewMode.Preview) SetViewMode(ViewMode.Both, true);
            _findBar.Visibility = Visibility.Visible;
            string sel = _editor.GetSelectedText();
            if (!string.IsNullOrEmpty(sel) && !sel.Contains("\n") && sel.Length < 100) _findBox.Text = sel;
            _findBox.Focus();
            _findBox.SelectAll();
        }

        void HideFindBar()
        {
            _findBar.Visibility = Visibility.Collapsed;
            _findInfo.Text = "";
            FocusEditor();
        }

        void FindNext(bool forward)
        {
            if (_findBar.Visibility != Visibility.Visible || _findBox.Text.Length == 0)
            {
                ShowFindBar();
                return;
            }
            SyncFromPreview();
            bool found = _editor.Find(_findBox.Text, forward);
            _findInfo.Text = found ? "" : "Nicht gefunden";
        }

        // ================================================================ Ansicht & Themes

        void ApplyTheme(Theme theme)
        {
            SyncFromPreview();
            ThemeManager.Apply(theme);
            _editor.ApplyTheme(theme);
            _settings.Theme = theme.Name;
            if (theme.IsDark) _settings.LastDarkTheme = theme.Name;
            else _settings.LastLightTheme = theme.Name;
            foreach (object o in _themeMenu.Items)
            {
                var mi = o as MenuItem;
                if (mi != null) mi.IsChecked = mi.Tag == theme;
            }
            RenderPreviewIfVisible();
            UpdateStatus();
        }

        void ToggleDarkMode()
        {
            bool dark = ThemeManager.Current != null && ThemeManager.Current.IsDark;
            ApplyTheme(ThemeCatalog.Find(dark ? _settings.LastLightTheme : _settings.LastDarkTheme));
        }

        /// <summary>Klassisches Vollbild: ohne Titelleiste und Rahmen, über die Taskleiste. Esc oder F11 beendet es.</summary>
        void ToggleFullScreen()
        {
            if (!_fullScreen)
            {
                _stateBeforeFullScreen = WindowState;
                _styleBeforeFullScreen = WindowStyle;
                _resizeBeforeFullScreen = ResizeMode;
                // Erst auf Normal, sonst bleibt ein bereits maximiertes Fenster hinter der Taskleiste
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                _fullScreen = true;
                ShowNotice("Vollbild – F11 oder Esc beendet");
            }
            else
            {
                WindowStyle = _styleBeforeFullScreen;
                ResizeMode = _resizeBeforeFullScreen;
                WindowState = WindowState.Normal;
                if (_stateBeforeFullScreen == WindowState.Maximized) WindowState = WindowState.Maximized;
                _fullScreen = false;
            }
            if (_fullScreenItem != null) _fullScreenItem.IsChecked = _fullScreen;
        }

        void ToggleSpellCheck()
        {
            _editor.SpellCheckEnabled = !_editor.SpellCheckEnabled;
            _preview.SpellCheck.IsEnabled = _editor.SpellCheckEnabled;
            _settings.SpellCheck = _editor.SpellCheckEnabled;
            _spellItem.IsChecked = _editor.SpellCheckEnabled;
        }

        void Zoom(int direction)
        {
            _editor.EditorFontSize += direction;
            AfterZoom();
        }

        void AfterZoom()
        {
            SyncFromPreview();
            _settings.FontSize = _editor.EditorFontSize;
            RenderPreviewIfVisible();
        }

        void OnEditorMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            Zoom(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            _spellItem.IsChecked = _editor.SpellCheckEnabled;
            _sidebarItem.IsChecked = _sidebar.Visibility == Visibility.Visible;
            UpdateViewMenu();
        }

        // ================================================================ Hilfe

        void ShowShortcuts()
        {
            var lines = new List<string>();
            foreach (Cmd c in _commands)
            {
                string g = GestureText(c);
                if (g.Length == 0) continue;
                string entry = g.PadRight(22) + c.Header;
                if (!lines.Contains(entry)) lines.Add(entry);
            }
            lines.Add("Strg+Mausrad".PadRight(22) + "Zoom");
            lines.Add("Strg+Klick".PadRight(22) + "Link in der Vorschau öffnen");
            lines.Add("Rechtsklick".PadRight(22) + "Formatierungsmenü (Markdown und Vorschau)");
            lines.Add("Esc".PadRight(22) + "Diktat abbrechen, Suche schließen, Vollbild beenden");
            MessageBox.Show(this, string.Join("\n", lines), "Tastenkürzel – " + AppName);
        }

        void InsertCheatSheet()
        {
            const string sheet =
                "# Überschrift 1\n## Überschrift 2\n\nStandardtext mit **fett**, *kursiv*, __unterstrichen__, ~~durchgestrichen~~ und `Code`.  \nNeue Zeile im selben Absatz.\n\n" +
                "- Aufzählung\n- [ ] Aufgabe\n1. Nummeriert\n\n> Zitat\n\n[Link](https://example.com) 🚀\n\n---\n\n" +
                "Spalte 1 | Spalte 2 | Spalte 3\n--- | --- | ---\nWert 1 | Wert 2 | Wert 3\n";
            _previewActive = false;
            _editor.InsertText(sheet);
            FocusEditor();
        }
    }
}
