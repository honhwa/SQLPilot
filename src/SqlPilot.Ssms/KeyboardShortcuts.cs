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
        public string Gesture
        {
            get; set;
        }
    }
    internal static class KeyboardShortcuts
    {
        internal static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>{
            {"QuickFixAlt","Alt+Enter"},{"QuickFix","Ctrl+."},{"Completion","Ctrl+Space"},{"Connect","Ctrl+Shift+C"},{"Refresh","Ctrl+Shift+R"},{"History","Ctrl+Shift+H"},{"Analyze","Ctrl+Shift+A"},{"Format","Ctrl+Shift+F"},{"Help","Ctrl+Shift+P"},{"Reference","F12"},{"Explorer","Ctrl+F12"},
            {"Library","Ctrl+Shift+L"},{"SaveSql","Ctrl+Shift+S"},{"Snippets",""},{"Settings",""},{"Shortcuts",""},{"RestoreSession",""},{"Checkpoint",""}};
        static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "keyboard.xml");
        static Dictionary<string, string> current = Load();
        static Dictionary<string, string> Load()
        {
            var values = new Dictionary<string, string>(Defaults);
            try
            {
                bool explicitAlt = false;
                if (File.Exists(PathName))
                    foreach (var e in XElement.Load(PathName).Elements("Binding"))
                    {
                        string command = (string)e.Attribute("Command");
                        if (values.ContainsKey(command))
                        {
                            var gesture = (string)e.Attribute("Gesture") ?? "";
                            values[command] = gesture;
                        }
                        if (command == "QuickFixAlt")
                            explicitAlt = true;
                    }
                if (!explicitAlt && values.Any(v => v.Key != "QuickFixAlt" && string.Equals(v.Value, "Alt+Enter", StringComparison.OrdinalIgnoreCase)))
                    values["QuickFixAlt"] = "";
                Validate(values);
            }
            catch { values = new Dictionary<string, string>(Defaults); }
            return values;
        }
        internal static string Gesture(string command) => current.TryGetValue(command, out var g) ? g : "";
        static KeyGesture Parse(string gesture) => (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(gesture.EndsWith("+.", StringComparison.Ordinal) ? gesture.Substring(0, gesture.Length - 1) + "OemPeriod" : gesture);
        internal static void Validate(IDictionary<string, string> values)
        {
            var seen = new HashSet<string>();
            foreach (var item in values)
            {
                if (!Defaults.ContainsKey(item.Key))
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
        internal static void Save(IDictionary<string, string> values)
        {
            Validate(values);
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            var root = new XElement("KeyboardShortcuts", values.Select(k => new XElement("Binding", new XAttribute("Command", k.Key), new XAttribute("Gesture", k.Value ?? ""))));
            string temporary = PathName + ".tmp";
            root.Save(temporary);
            if (File.Exists(PathName))
                File.Replace(temporary, PathName, null);
            else
                File.Move(temporary, PathName);
            current = new Dictionary<string, string>(values);
        }
        internal static void Show()
        {
            var rows = current.Select(k => new ShortcutRow { Command = k.Key, Gesture = k.Value }).ToList();
            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, ItemsSource = rows, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 34, GridLinesVisibility = DataGridGridLinesVisibility.None, Background = System.Windows.Media.Brushes.Transparent };
            grid.Columns.Add(new DataGridTextColumn { Header = "Action", Binding = new System.Windows.Data.Binding("Command"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
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
            reset.Click += (_, __) => { rows = Defaults.Select(k => new ShortcutRow { Command = k.Key, Gesture = k.Value }).ToList(); grid.ItemsSource = rows; };
            save.Click += (_, __) => { try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); Save(rows.ToDictionary(k => k.Command, k => k.Gesture)); window.Close(); } catch (Exception ex) { MessageBox.Show(ex.Message, "SqlPilot · Keyboard shortcuts"); } };
            window.ShowDialog();
        }
    }
}
