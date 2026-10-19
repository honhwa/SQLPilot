// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
namespace SqlPilot.Ssms
{
    internal sealed class ShortcutRow
    {
        public string Command
        {
            get; set;
        }
        public string Label
        {
            get; set;
        }
        public string Gesture
        {
            get; set;
        }
    }
    internal static class KeyboardShortcuts
    {
        internal static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>{
            {"QuickFixAlt","Alt+Enter"},{"QuickFix","Ctrl+."},{"Completion","Ctrl+Space"},{"Connect","Ctrl+Shift+C"},{"Refresh","Ctrl+Shift+R"},{"History","Ctrl+Shift+H"},{"Analyze","Ctrl+Shift+A"},{"Format","Ctrl+Shift+F"},{"Help","Ctrl+Shift+P"},{"Reference","F12"},{"Explorer","Ctrl+F12"},
            {"ExecuteStatement","Ctrl+Enter"},{"Library","Ctrl+Shift+L"},{"SaveSql","Ctrl+Shift+S"},{"Snippets",""},{"Settings",""},{"Shortcuts",""},{"RestoreSession",""},{"Checkpoint",""}};
        static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "keyboard.xml");
        static Dictionary<string, string> current = Load();
        internal static Dictionary<string, string> Load(string path = null)
        {
            path = path ?? PathName;
            var values = new Dictionary<string, string>(Defaults);
            try
            {
                bool explicitAlt = false, explicitExecute = false;
                if (File.Exists(path))
                    foreach (var e in XElement.Load(path).Elements("Binding"))
                    {
                        string command = (string)e.Attribute("Command");
                        if (IsKnownCommand(command))
                        {
                            var gesture = (string)e.Attribute("Gesture") ?? "";
                            values[command] = gesture;
                        }
                        if (command == "ExecuteStatement")
                            explicitExecute = true;
                        if (command == "QuickFixAlt")
                            explicitAlt = true;
                    }
                if (!explicitAlt && values.Any(v => v.Key != "QuickFixAlt" && string.Equals(v.Value, "Alt+Enter", StringComparison.OrdinalIgnoreCase)))
                    values["QuickFixAlt"] = "";
                if (!explicitExecute && values.Any(v => v.Key != "ExecuteStatement" && SameGesture(v.Value, values["ExecuteStatement"])))
                    values["ExecuteStatement"] = "";
                Validate(values);
            }
            catch { values = new Dictionary<string, string>(Defaults); }
            return values;
        }
        internal static string LibraryCommand(string id)
        {
            if (!Guid.TryParseExact(id, "N", out var value))
                throw new ArgumentException("Invalid library entry.");
            return "Library:" + value.ToString("N");
        }
        internal static bool IsLibraryCommand(string command) => command != null && command.StartsWith("Library:", StringComparison.Ordinal) && Guid.TryParseExact(command.Substring(8), "N", out _);
        static bool IsKnownCommand(string command) => command != null && (Defaults.ContainsKey(command) || IsLibraryCommand(command));
        internal static Dictionary<string, string> WithLibraryShortcut(string id, string gesture, IDictionary<string, string> source = null)
        {
            var proposed = new Dictionary<string, string>(source ?? current);
            proposed[LibraryCommand(id)] = gesture?.Trim() ?? "";
            Validate(proposed);
            return proposed;
        }
        internal static void SaveLibraryShortcut(string id, string gesture) => Save(WithLibraryShortcut(id, gesture));
        internal static string Gesture(string command) => current.TryGetValue(command, out var g) ? g : "";
        static bool SameGesture(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
                return false;
            var a = Parse(first);
            var b = Parse(second);
            return a.Key == b.Key && a.Modifiers == b.Modifiers;
        }
        static KeyGesture Parse(string gesture) => (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(gesture.EndsWith("+.", StringComparison.Ordinal) ? gesture.Substring(0, gesture.Length - 1) + "OemPeriod" : gesture);
        internal static void Validate(IDictionary<string, string> values)
        {
            var seen = new HashSet<string>();
            foreach (var item in values)
            {
                if (!IsKnownCommand(item.Key))
                    throw new ArgumentException("Unknown command.");
                if (string.IsNullOrWhiteSpace(item.Value))
                    continue;
                var key = Parse(item.Value);
                if (key == null)
                    throw new ArgumentException("Invalid shortcut for " + item.Key);
                if ((key.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 && (key.Key < Key.F1 || key.Key > Key.F24))
                    throw new ArgumentException("Use Ctrl, Alt or a function key for " + item.Key + ".");
                if (key.Modifiers == ModifierKeys.Control && new[] { Key.C, Key.V, Key.X, Key.Z, Key.Y, Key.A, Key.S, Key.F, Key.O, Key.N, Key.W }.Contains(key.Key))
                    throw new ArgumentException("That shortcut is reserved for the SQL editor.");
                if (!seen.Add(key.Modifiers + ":" + key.Key))
                    throw new ArgumentException("Two commands cannot share " + item.Value + ".");
            }
        }
        internal static string Match(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            foreach (var binding in current)
                if (!string.IsNullOrWhiteSpace(binding.Value))
                {
                    var gesture = Parse(binding.Value);
                    if (gesture.Key == key && gesture.Modifiers == Keyboard.Modifiers)
                        return binding.Key;
                }
            return null;
        }
        internal static string MatchHeld(ModifierKeys modifiers, Func<Key, bool> pressed)
        {
            foreach (var binding in current)
                if (!string.IsNullOrWhiteSpace(binding.Value))
                {
                    var gesture = Parse(binding.Value);
                    if (gesture.Modifiers == modifiers && pressed(gesture.Key))
                        return binding.Key;
                }
            return null;
        }
        internal static void Save(IDictionary<string, string> values, string path = null, bool activate = true)
        {
            path = path ?? PathName;
            Validate(values);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var root = new XElement("KeyboardShortcuts", values.Select(k => new XElement("Binding", new XAttribute("Command", k.Key), new XAttribute("Gesture", k.Value ?? ""))));
            string temporary = path + ".tmp";
            root.Save(temporary);
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
            if (activate)
                current = new Dictionary<string, string>(values);
        }
        internal static void Show()
        {
            var titles = new Dictionary<string, string>();
            try
            {
                titles = new SqlPilot.Core.SqlLibrary(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(PathName), "Library")).Load().ToDictionary(e => LibraryCommand(e.Id), e => "Library · " + e.Title);
            }
            catch (Exception ex) { Diagnostics.Write("Library shortcut labels: " + ex.GetType().Name); }
            var rows = current.Select(k => new ShortcutRow { Command = k.Key, Label = titles.TryGetValue(k.Key, out var title) ? title : k.Key, Gesture = k.Value }).ToList();
            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, ItemsSource = rows, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 34, GridLinesVisibility = DataGridGridLinesVisibility.None, Background = System.Windows.Media.Brushes.Transparent };
            grid.Columns.Add(new DataGridTextColumn { Header = "Action", Binding = new System.Windows.Data.Binding("Label"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Keyboard shortcut", Binding = new System.Windows.Data.Binding("Gesture") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, Width = 220 });
            var panel = new DockPanel();
            var hint = new TextBlock { Text = "Edit a shortcut, for example Ctrl+Shift+F. Leave it blank to disable.\nTab / Enter / arrows remain reserved for completion. Tab snippets are edited separately.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(hint, Dock.Top);
            panel.Children.Add(hint);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var reset = new Button { Content = "Restore defaults", Margin = new Thickness(0, 0, 10, 0) };
            var snippets = new Button { Content = "Tab snippets…", Margin = new Thickness(0, 0, 10, 0) };
            var save = SqlPilot.UI.Design.Primary("Save shortcuts");
            footer.Children.Add(snippets);
            footer.Children.Add(reset);
            footer.Children.Add(save);
            DockPanel.SetDock(footer, Dock.Bottom);
            panel.Children.Add(footer);
            panel.Children.Add(grid);
            var window = SqlPilot.UI.Design.Window("Keyboard Shortcuts", panel, 780, 780, "Your preferred keys, saved for every SQL tab");
            window.Icon = Brand.Icon();
            snippets.Click += (_, __) => PersonalDialogs.ShowSnippets();
            reset.Click += (_, __) => { rows = Defaults.Select(k => new ShortcutRow { Command = k.Key, Label = k.Key, Gesture = k.Value }).ToList(); grid.ItemsSource = rows; };
            save.Click += (_, __) => { try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); Save(rows.ToDictionary(k => k.Command, k => k.Gesture)); window.Close(); } catch (Exception ex) { MessageBox.Show(ex.Message, "SqlPilot · Keyboard shortcuts"); } };
            window.ShowDialog();
        }
    }
}
