// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    internal static class PersonalDialogs
    {
        static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot");
        internal static readonly SnippetStore SnippetFiles = new SnippetStore(Path.Combine(Root, "snippets.xml"));
        internal static Dictionary<string, string> Snippets = LoadSnippets();
        static Dictionary<string, string> LoadSnippets()
        {
            try
            {
                return SnippetFiles.Load();
            }
            catch (Exception ex) { Diagnostics.Write("Snippet load failed: " + ex.GetType().Name); return new Dictionary<string, string>(Engine.Snippets, StringComparer.OrdinalIgnoreCase); }
        }
        static TextBox Field(StackPanel panel, string caption, bool multiline = false)
        {
            panel.Children.Add(new TextBlock { Text = caption, Margin = new Thickness(0, 8, 0, 4) });
            var box = new TextBox { AcceptsReturn = multiline, AcceptsTab = multiline, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            if (multiline)
            {
                box.Height = 180;
                box.FontFamily = new FontFamily("Consolas");
                box.TextWrapping = TextWrapping.NoWrap;
                box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            panel.Children.Add(box);
            return box;
        }
        static Button Button(Panel panel, string caption, Action action)
        {
            var b = new Button { Content = caption, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 8, 6, 0) };
            b.Click += (_, __) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "SqlPilot", MessageBoxButton.OK, MessageBoxImage.Warning); } };
            panel.Children.Add(b);
            return b;
        }
        static Window Window(string title, object content)
        {
            var w = SqlPilot.UI.Design.Window(title, (UIElement)content);
            w.Icon = Brand.Icon();
            return w;
        }
        internal static void ShowSnippets()
        {
            var local = new Dictionary<string, string>(Snippets, StringComparer.OrdinalIgnoreCase);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new DockPanel { Margin = new Thickness(0, 0, 20, 0) };
            grid.Children.Add(left);
            var caption = new TextBlock { Text = "YOUR SNIPPETS", Foreground = SqlPilot.UI.Design.Muted, FontSize = 11, Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(caption, Dock.Top);
            left.Children.Add(caption);
            var search = new TextBox { Margin = new Thickness(0, 0, 0, 12), ToolTip = "Filter shortcuts" };
            DockPanel.SetDock(search, Dock.Top);
            left.Children.Add(search);
            var list = new ListBox();
            left.Children.Add(list);
            var right = new Grid();
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            foreach (var size in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
                right.RowDefinitions.Add(new RowDefinition { Height = size });
            var intro = new TextBlock { Text = "Type a shortcut in a SQL tab and press Tab.", Foreground = SqlPilot.UI.Design.Muted, Margin = new Thickness(0, 0, 0, 16), TextWrapping = TextWrapping.Wrap };
            right.Children.Add(intro);
            var keyPanel = new StackPanel();
            var key = Field(keyPanel, "Shortcut · for example sf");
            key.MaxLength = 32;
            Grid.SetRow(keyPanel, 1);
            right.Children.Add(keyPanel);
            var label = new TextBlock { Text = "SQL expansion", Margin = new Thickness(0, 16, 0, 7) };
            Grid.SetRow(label, 2);
            right.Children.Add(label);
            var sql = new TextBox { AcceptsReturn = true, AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), MinHeight = 140, VerticalContentAlignment = VerticalAlignment.Top };
            Grid.SetRow(sql, 3);
            right.Children.Add(sql);
            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(actions, 4);
            right.Children.Add(actions);
            var hint = new TextBlock { Text = "Rename ssf to sf, or create your own expansion.\nNewlines and trailing spaces are kept exactly as entered.", Foreground = SqlPilot.UI.Design.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 14) };
            Grid.SetRow(hint, 5);
            right.Children.Add(hint);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(footer, 6);
            right.Children.Add(footer);
            string selected = null;
            bool loading = false;
            bool saved = false;
            Func<bool> discardEdit = () => (selected == null ? key.Text.Length == 0 && sql.Text.Length == 0 : key.Text == selected && sql.Text == local[selected]) || MessageBox.Show("Discard this unapplied shortcut edit?", "SqlPilot Tab Shortcuts", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            Action refresh = () => { loading = true; list.ItemsSource = local.Keys.Where(k => k.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(k => k).ToList(); list.SelectedItem = selected; loading = false; };
            search.TextChanged += (_, __) => refresh();
            list.SelectionChanged += (_, __) => { if (loading) return; if (!discardEdit()) { loading = true; list.SelectedItem = selected; loading = false; return; } selected = list.SelectedItem as string; key.Text = selected ?? ""; sql.Text = selected == null ? "" : local[selected]; };
            Action apply = () => { string next = key.Text.Trim(); var proposed = new Dictionary<string, string>(local, StringComparer.OrdinalIgnoreCase); if (selected != null) proposed.Remove(selected); if (proposed.ContainsKey(next)) throw new ArgumentException("That shortcut already exists. Select it to edit."); proposed.Add(next, sql.Text); SnippetStore.Validate(proposed); local = proposed; selected = next; refresh(); };
            Button(actions, "New", () => { if (!discardEdit()) return; loading = true; selected = null; list.SelectedItem = null; key.Clear(); sql.Clear(); loading = false; key.Focus(); });
            Button(actions, "Apply edit", () => apply());
            Button(actions, "Remove", () => { if (selected == null) return; local.Remove(selected); selected = null; key.Clear(); sql.Clear(); refresh(); });
            var window = Window("Tab Shortcuts", grid);
            window.MinHeight = 640;
            window.Height = 740;
            Button(footer, "Cancel", () => window.Close());
            var save = Button(footer, "Save shortcuts", () => { if (!string.IsNullOrWhiteSpace(key.Text) || !string.IsNullOrWhiteSpace(sql.Text)) apply(); SnippetFiles.Save(local); Snippets = new Dictionary<string, string>(local, StringComparer.OrdinalIgnoreCase); saved = true; window.Close(); });
            save.Background = SqlPilot.UI.Design.Accent;
            save.Foreground = Brushes.White;
            save.IsDefault = true;
            window.Closing += (_, e) => { if (!saved && !local.OrderBy(x => x.Key).SequenceEqual(Snippets.OrderBy(x => x.Key)) && MessageBox.Show("Discard unsaved shortcut changes?", "SqlPilot Tab Shortcuts", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true; };
            refresh();
            window.ShowDialog();
        }
        internal static void ShowLibrary(string initialSql = null, string initialTitle = null, Action<string> insert = null, Action<string> open = null)
        {
            var store = new SqlLibrary(Path.Combine(Root, "Library"));
            Window window;
            try
            {
                window = CreateLibraryWindow(store, initialSql, initialTitle, insert, open);
            }
            catch (Exception ex) { MessageBox.Show("Library could not be loaded: " + ex.Message, "SqlPilot Library"); return; }
            window.ShowDialog();
        }
        internal static Window CreateLibraryWindow(SqlLibrary store, string initialSql = null, string initialTitle = null, Action<string> insert = null, Action<string> open = null)
        {
            var entries = store.Load();
            var grid = new Grid { Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            grid.Children.Add(left);
            left.Children.Add(new TextBlock { Text = "SQL Library", FontSize = 22 });
            var search = Field(left, "Search title, tags and SQL");
            search.Name = "LibrarySearch";
            left.Children.Add(new TextBlock { Text = "Category", Margin = new Thickness(0, 8, 0, 4) });
            var filter = new ComboBox();
            left.Children.Add(filter);
            var list = new ListBox { Name = "LibraryList", Height = 285, Margin = new Thickness(0, 10, 0, 0) };
            left.Children.Add(list);
            var right = new StackPanel();
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            var title = Field(right, "Title");
            var category = Field(right, "Category");
            var tags = Field(right, "Tags");
            var sql = Field(right, "SQL", true);
            sql.Name = "LibrarySql";
            var status = new TextBlock { Text = "↑ / ↓ Select query · Ctrl+Enter Insert into query\nStored locally. Open and Insert never execute SQL.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            right.Children.Add(status);
            LibraryEntry selected = null;
            bool loading = false, dirty = false, draftMode = initialSql != null;
            Func<bool> discard = () => !dirty || MessageBox.Show("Discard unsaved library edits?", "SqlPilot Library", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            Action<LibraryEntry> populate = e => { loading = true; selected = e; title.Text = e?.Title ?? ""; category.Text = e?.Category ?? "General"; tags.Text = e?.Tags ?? ""; sql.Text = e?.Sql ?? ""; loading = false; dirty = false; };
            foreach (var box in new[] { title, category, tags, sql })
                box.TextChanged += (_, __) => { if (!loading) dirty = true; };
            Action refresh = () =>
            {
                loading = true;
                string prior = filter.SelectedItem as string;
                filter.ItemsSource = new[] { "All categories" }.Concat(entries.Select(e => e.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c)).ToList();
                filter.SelectedItem = prior ?? "All categories";
                if (filter.SelectedItem == null)
                    filter.SelectedIndex = 0;
                var visible = SqlLibrary.Search(entries, search.Text, (string)filter.SelectedItem == "All categories" ? null : (string)filter.SelectedItem).ToList();
                list.ItemsSource = visible;
                list.SelectedItem = visible.FirstOrDefault(e => e.Id == selected?.Id) ?? (!draftMode && !dirty ? visible.FirstOrDefault() : null);
                loading = false;
                if (!draftMode && !dirty)
                    populate(list.SelectedItem as LibraryEntry);
            };
            list.SelectionChanged += (_, __) => { if (loading) return; var next = list.SelectedItem as LibraryEntry; if (next == null || next == selected) return; if (!discard()) { loading = true; list.SelectedItem = selected; loading = false; return; } draftMode = false; populate(next); };
            search.TextChanged += (_, __) => { if (!loading) refresh(); };
            filter.SelectionChanged += (_, __) => { if (!loading) refresh(); };
            var actions = new WrapPanel();
            right.Children.Add(actions);
            var window = Window("SqlPilot · SQL Library", grid);
            window.Height = 780;
            window.MinHeight = 700;
            Button(actions, "New", () => { if (discard()) { draftMode = true; populate(null); loading = true; list.SelectedItem = null; loading = false; title.Focus(); } });
            Button(actions, "Save", () => { var entry = new LibraryEntry { Id = selected?.Id, Title = title.Text, Category = category.Text, Tags = tags.Text, Sql = sql.Text }; store.Save(entry); entries = store.Load(); selected = entries.First(e => e.Id == entry.Id); dirty = false; draftMode = false; refresh(); status.Text = "Saved to SQL Library."; });
            Button(actions, "Import .sql", () => { if (!discard()) return; var file = new OpenFileDialog { Filter = "SQL files (*.sql)|*.sql", Multiselect = false }; if (file.ShowDialog() == true) { draftMode = true; populate(null); loading = true; list.SelectedItem = null; loading = false; title.Text = Path.GetFileNameWithoutExtension(file.FileName); sql.Text = File.ReadAllText(file.FileName); dirty = true; } });
            Button(actions, "Export .sql", () => { var file = new SaveFileDialog { Filter = "SQL files (*.sql)|*.sql", DefaultExt = ".sql", FileName = "Query.sql" }; if (file.ShowDialog() == true) File.WriteAllText(file.FileName, sql.Text, new System.Text.UTF8Encoding(false)); });
            var insertButton = Button(actions, "Insert into query (Ctrl+Enter)", () =>
            {
                if (insert == null || string.IsNullOrWhiteSpace(sql.Text) || !discard())
                    return;
                insert(sql.Text);
                if (selected != null)
                {
                    try
                    {
                        store.MarkInserted(selected.Id);
                    }
                    catch (Exception ex) { Diagnostics.Write("Library usage save failed: " + ex.GetType().Name); }
                }
                dirty = false;
                window.Close();
            });
            insertButton.Name = "LibraryInsert";
            insertButton.ToolTip = "Insert selected query · Ctrl+Enter";
            insertButton.IsEnabled = insert != null && !string.IsNullOrWhiteSpace(sql.Text);
            sql.TextChanged += (_, __) => insertButton.IsEnabled = insert != null && !string.IsNullOrWhiteSpace(sql.Text);
            var openButton = Button(actions, "Open SQL tab", () => { if (selected == null || dirty) throw new InvalidOperationException("Save the library entry first."); open(store.SqlPath(selected)); window.Close(); });
            openButton.IsEnabled = open != null;
            Button(actions, "Close", () => window.Close());
            window.Closing += (_, e) => { if (!discard()) e.Cancel = true; };
            window.PreviewKeyDown += (_, e) =>
            {
                e.Handled = HandleLibraryKey(e.Key, Keyboard.Modifiers, search.IsKeyboardFocusWithin || list.IsKeyboardFocusWithin, list, insertButton);
            };
            refresh();
            if (initialSql != null)
            {
                populate(null);
                title.Text = initialTitle ?? "Untitled query";
                sql.Text = initialSql;
                dirty = true;
            }
            window.Loaded += (_, __) => { if (draftMode) title.Focus(); else list.Focus(); };
            return window;
        }
        internal static void MoveLibrarySelection(ListBox list, Key key)
        {
            if (list.Items.Count == 0)
                return;
            int next = list.SelectedIndex < 0 ? 0 : Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + (key == Key.Up ? -1 : 1)));
            list.SelectedIndex = next;
            list.ScrollIntoView(list.SelectedItem);
        }
        internal static bool HandleLibraryKey(Key key, ModifierKeys modifiers, bool browsing, ListBox list, Button insert)
        {
            if (key == Key.Enter && modifiers == ModifierKeys.Control)
            {
                if (insert.IsEnabled)
                    insert.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                return true;
            }
            if (browsing && modifiers == ModifierKeys.None && (key == Key.Up || key == Key.Down))
            {
                MoveLibrarySelection(list, key);
                return true;
            }
            return false;
        }
    }
}
