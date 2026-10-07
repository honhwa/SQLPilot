using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SqlPilot.Core;
using SqlPilot.Ssms;

internal static class LibraryChecks
{
    static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Children(child))
                yield return nested;
    }
    static T Named<T>(Window window, string name) where T : FrameworkElement => Children(window).OfType<T>().Single(e => e.Name == name);
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "SqlPilot.LibraryChecks", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SqlLibrary(root);
            var alpha = store.Save(new LibraryEntry { Title = "Alpha", Category = "Reports", Sql = "SELECT N'الف';" });
            var beta = store.Save(new LibraryEntry { Title = "Beta", Category = "Reports", Sql = "SELECT 2;" });
            store.MarkInserted(alpha.Id);
            store.MarkInserted(beta.Id);
            var before = store.Load().ToDictionary(e => e.Id, e => e.LastInsertedUtc);
            string inserted = null;
            var window = PersonalDialogs.CreateLibraryWindow(store, insert: text => inserted = text);
            var list = Named<ListBox>(window, "LibraryList");
            var sql = Named<TextBox>(window, "LibrarySql");
            var search = Named<TextBox>(window, "LibrarySearch");
            var button = Named<Button>(window, "LibraryInsert");
            check(((LibraryEntry)list.SelectedItem).Id == beta.Id && sql.Text == beta.Sql, "Library opens with first recent query selected and preview populated");
            check(((string)button.Content).Contains("Ctrl+Enter"), "Library insert button displays its keyboard shortcut");
            var visual = (FrameworkElement)window.Content;
            visual.Measure(new Size(920, 780));
            visual.Arrange(new Rect(0, 0, 920, 780));
            visual.UpdateLayout();
            var label = new FormattedText((string)button.Content, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize, button.Foreground, 1);
            check(button.ActualWidth >= label.WidthIncludingTrailingWhitespace + button.Padding.Left + button.Padding.Right && button.TranslatePoint(new Point(0, button.ActualHeight), visual).Y <= 780, "Library insert shortcut label fits visible dialog layout");
            string artifacts = Environment.GetEnvironmentVariable("SQLPILOT_TEST_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts))
            {
                Directory.CreateDirectory(artifacts);
                var backdrop = new DrawingVisual();
                using (var drawing = backdrop.RenderOpen())
                    drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 920, 780));
                var bitmap = new RenderTargetBitmap(920, 780, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(backdrop);
                bitmap.Render(visual);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(artifacts, "library.png")))
                    png.Save(file);
            }
            check(PersonalDialogs.HandleLibraryKey(Key.Down, ModifierKeys.None, true, list, button) && ((LibraryEntry)list.SelectedItem).Id == alpha.Id && sql.Text == alpha.Sql, "Down selects next query and updates SQL preview");
            PersonalDialogs.HandleLibraryKey(Key.Down, ModifierKeys.None, true, list, button);
            check(((LibraryEntry)list.SelectedItem).Id == alpha.Id, "Down at last library query stays in bounds");
            PersonalDialogs.HandleLibraryKey(Key.Up, ModifierKeys.None, true, list, button);
            check(((LibraryEntry)list.SelectedItem).Id == beta.Id, "Up selects previous library query");
            check(!PersonalDialogs.HandleLibraryKey(Key.Down, ModifierKeys.None, false, list, button) && ((LibraryEntry)list.SelectedItem).Id == beta.Id, "SQL editor arrow keys are not hijacked by library navigation");
            search.Text = "Alpha";
            check(list.Items.Count == 1 && ((LibraryEntry)list.SelectedItem).Id == alpha.Id && sql.Text == alpha.Sql, "Library search automatically selects first matching result");
            search.Text = "no-matching-query";
            check(list.Items.Count == 0 && list.SelectedItem == null && sql.Text == "", "Empty library search clears stale selection and preview");
            PersonalDialogs.HandleLibraryKey(Key.Enter, ModifierKeys.Control, true, list, button);
            check(inserted == null && store.Load().All(e => e.LastInsertedUtc == before[e.Id]), "Empty library results never insert stale SQL or change usage order");
            search.Text = "";
            check(((LibraryEntry)list.SelectedItem).Id == beta.Id, "Clearing library search restores most recently inserted query");
            check(!PersonalDialogs.HandleLibraryKey(Key.Enter, ModifierKeys.None, false, list, button) && !PersonalDialogs.HandleLibraryKey(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, false, list, button) && inserted == null, "Only Ctrl Enter invokes insertion; ordinary editor Enter remains available");
            PersonalDialogs.MoveLibrarySelection(list, Key.Down);
            PersonalDialogs.HandleLibraryKey(Key.Enter, ModifierKeys.Control, false, list, button);
            check(inserted == alpha.Sql && store.Load().First().Id == alpha.Id && store.Load().Single(e => e.Id == beta.Id).LastInsertedUtc == before[beta.Id], "Ctrl Enter inserts selected SQL and persists only that query's usage");
            var reopened = PersonalDialogs.CreateLibraryWindow(store, insert: text => inserted = text);
            check(((LibraryEntry)Named<ListBox>(reopened, "LibraryList").SelectedItem).Id == alpha.Id, "Reopened library selects the last query inserted into the editor");
            reopened.Close();
            var unavailable = PersonalDialogs.CreateLibraryWindow(store);
            var unavailableInsert = Named<Button>(unavailable, "LibraryInsert");
            inserted = null;
            check(!unavailableInsert.IsEnabled && PersonalDialogs.HandleLibraryKey(Key.Enter, ModifierKeys.Control, false, Named<ListBox>(unavailable, "LibraryList"), unavailableInsert), "Library shortcut respects missing target editor");
            unavailable.Close();
            var draft = PersonalDialogs.CreateLibraryWindow(store, "SELECT 42;", "New query");
            check(Named<ListBox>(draft, "LibraryList").SelectedItem == null && Named<TextBox>(draft, "LibrarySql").Text == "SELECT 42;", "Save current query preserves incoming draft instead of overwriting it with first entry");
            Children(draft).OfType<Button>().Single(b => Equals(b.Content, "Save")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(store.Load().First().Id == alpha.Id, "Saving a new library query does not displace last inserted query");
            draft.Close();
            var empty = PersonalDialogs.CreateLibraryWindow(new SqlLibrary(Path.Combine(root, "empty")), insert: text => inserted = text);
            check(Named<ListBox>(empty, "LibraryList").SelectedItem == null && Named<TextBox>(empty, "LibrarySql").Text == "", "Empty library opens without invalid automatic selection");
            empty.Close();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
