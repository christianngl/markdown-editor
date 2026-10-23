using System;
using System.Collections.Generic;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using MarkdownEditor.Markdown;
using MarkdownEditor.Themes;

namespace MarkdownEditor.Dialogs
{
    /// <summary>
    /// Visueller Tabellen-Editor. Die Tabelle wird als Raster bearbeitet und
    /// anschließend als Markdown-Fließtext in das Dokument geschrieben.
    /// </summary>
    public sealed class TableDialog : Window
    {
        readonly DataGrid _grid = new DataGrid();
        readonly List<ColumnAlign> _aligns;
        readonly Style _editStyle;
        DataTable _table;

        public MarkdownTable Result { get; private set; }

        public TableDialog(MarkdownTable source, bool editing)
        {
            Title = editing ? "Tabelle bearbeiten" : "Tabelle einfügen";
            Width = 760;
            Height = 460;
            MinWidth = 460;
            MinHeight = 280;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ChromeBg");
            SetResourceReference(ForegroundProperty, "ChromeFg");
            SourceInitialized += delegate { ThemeManager.ApplyTitleBar(this); };

            _aligns = new List<ColumnAlign>(source.Aligns);
            if (_aligns.Count == 0) _aligns.Add(ColumnAlign.None);

            _editStyle = new Style(typeof(TextBox));
            _editStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("EditorBg")));
            _editStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ChromeFg")));
            _editStyle.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, new DynamicResourceExtension("ChromeFg")));
            _editStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            _editStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 2, 1)));

            var root = new DockPanel { Margin = new Thickness(12) };

            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            toolbar.Children.Add(MakeButton("+ Zeile", AddRow));
            toolbar.Children.Add(MakeButton("− Zeile", RemoveRow));
            toolbar.Children.Add(MakeButton("+ Spalte", AddColumn));
            toolbar.Children.Add(MakeButton("− Spalte", RemoveColumn));
            toolbar.Children.Add(new Border { Width = 14 });
            toolbar.Children.Add(MakeButton("⇤ Links", delegate { SetAlign(ColumnAlign.Left); }));
            toolbar.Children.Add(MakeButton("↔ Zentriert", delegate { SetAlign(ColumnAlign.Center); }));
            toolbar.Children.Add(MakeButton("Rechts ⇥", delegate { SetAlign(ColumnAlign.Right); }));
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            var hint = new TextBlock
            {
                Text = "Die erste Zeile ist die Kopfzeile. Die Ausrichtung gilt für die markierte Spalte.",
                Opacity = 0.65,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(hint, Dock.Top);
            root.Children.Add(hint);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = editing ? "Übernehmen" : "Einfügen", IsDefault = true, MinWidth = 96 };
            ok.Click += delegate { Accept(); };
            var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 96, Margin = new Thickness(0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.CanUserDeleteRows = false;
            _grid.CanUserSortColumns = false;
            _grid.CanUserReorderColumns = false;
            _grid.CanUserResizeColumns = false;
            _grid.CanUserResizeRows = false;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.SelectionUnit = DataGridSelectionUnit.Cell;
            _grid.SelectionMode = DataGridSelectionMode.Single;
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
            _grid.RowHeight = 28;
            _grid.SetResourceReference(BackgroundProperty, "EditorBg");
            _grid.SetResourceReference(DataGrid.RowBackgroundProperty, "EditorBg");
            _grid.SetResourceReference(ForegroundProperty, "ChromeFg");
            _grid.SetResourceReference(BorderBrushProperty, "ChromeBorder");
            _grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "ChromeBorder");
            _grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, "ChromeBorder");
            _grid.LoadingRow += (s, e) =>
            {
                bool header = e.Row.GetIndex() == 0;
                e.Row.FontWeight = header ? FontWeights.Bold : FontWeights.Normal;
                e.Row.SetResourceReference(BackgroundProperty, header ? "MenuHover" : "EditorBg");
            };
            root.Children.Add(_grid);

            Content = root;
            Load(source.Rows);
            Loaded += delegate { _grid.Focus(); };
        }

        static Button MakeButton(string text, Action action)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 4, 10, 4) };
            b.Click += delegate { action(); };
            return b;
        }

        void Load(List<List<string>> rows)
        {
            int cols = _aligns.Count;
            _table = new DataTable();
            for (int c = 0; c < cols; c++)
                _table.Columns.Add("c" + c, typeof(string));
            foreach (List<string> row in rows)
            {
                DataRow dr = _table.NewRow();
                for (int c = 0; c < cols; c++)
                    dr[c] = c < row.Count ? row[c] : "";
                _table.Rows.Add(dr);
            }

            _grid.Columns.Clear();
            for (int c = 0; c < cols; c++)
            {
                _grid.Columns.Add(new DataGridTextColumn
                {
                    Header = ColumnHeader(c),
                    Binding = new Binding("c" + c) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    EditingElementStyle = _editStyle
                });
            }
            _grid.ItemsSource = _table.DefaultView;
        }

        string ColumnHeader(int c)
        {
            string name = "Spalte " + (c + 1);
            switch (_aligns[c])
            {
                case ColumnAlign.Left: return name + "  ⇤";
                case ColumnAlign.Center: return name + "  ↔";
                case ColumnAlign.Right: return name + "  ⇥";
                default: return name;
            }
        }

        List<List<string>> ReadRows()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            var rows = new List<List<string>>();
            foreach (DataRow dr in _table.Rows)
            {
                var row = new List<string>();
                for (int c = 0; c < _table.Columns.Count; c++)
                    row.Add(dr[c] as string ?? "");
                rows.Add(row);
            }
            return rows;
        }

        int CurrentRow()
        {
            return _grid.CurrentItem != null ? _grid.Items.IndexOf(_grid.CurrentItem) : -1;
        }

        int CurrentColumn()
        {
            return _grid.CurrentColumn != null ? _grid.CurrentColumn.DisplayIndex : -1;
        }

        void AddRow()
        {
            List<List<string>> rows = ReadRows();
            int idx = CurrentRow();
            var row = new List<string>();
            for (int c = 0; c < _aligns.Count; c++) row.Add("");
            rows.Insert(idx >= 0 ? idx + 1 : rows.Count, row);
            Load(rows);
        }

        void RemoveRow()
        {
            List<List<string>> rows = ReadRows();
            if (rows.Count <= 1) return;
            int idx = CurrentRow();
            rows.RemoveAt(idx >= 0 ? idx : rows.Count - 1);
            Load(rows);
        }

        void AddColumn()
        {
            List<List<string>> rows = ReadRows();
            int idx = CurrentColumn();
            int at = idx >= 0 ? idx + 1 : _aligns.Count;
            _aligns.Insert(at, ColumnAlign.None);
            for (int r = 0; r < rows.Count; r++)
                rows[r].Insert(at, r == 0 ? "Spalte " + _aligns.Count : "");
            Load(rows);
        }

        void RemoveColumn()
        {
            if (_aligns.Count <= 1) return;
            List<List<string>> rows = ReadRows();
            int idx = CurrentColumn();
            int at = idx >= 0 ? idx : _aligns.Count - 1;
            _aligns.RemoveAt(at);
            foreach (List<string> row in rows) row.RemoveAt(at);
            Load(rows);
        }

        void SetAlign(ColumnAlign align)
        {
            int idx = CurrentColumn();
            if (idx < 0) idx = 0;
            _aligns[idx] = _aligns[idx] == align ? ColumnAlign.None : align;
            _grid.Columns[idx].Header = ColumnHeader(idx);
        }

        void Accept()
        {
            var result = new MarkdownTable();
            result.Aligns.AddRange(_aligns);
            result.Rows.AddRange(ReadRows());
            Result = result;
            DialogResult = true;
        }
    }
}
