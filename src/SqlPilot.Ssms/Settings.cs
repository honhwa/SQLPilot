// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    internal sealed class UserSettings
    {
        internal static UserSettings Current = Load();
        internal static event EventHandler Changed;
        internal bool Enabled = true, FuzzyMatching = true, TableAliases = true, QualifyColumns = true, ExclusiveCompletion = true, AutoConvert = true, RestoreSession = true, InlineDiagnostics = true, UseBrackets = true;
        internal string Theme = "Purple";
        internal int FontSize = 12, MaxResults = 200;
        static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "settings.xml");
        internal CompletionOptions Options => new CompletionOptions { FuzzyMatching = FuzzyMatching, TableAliases = TableAliases, QualifyColumns = QualifyColumns, MaxResults = MaxResults, UseBrackets = UseBrackets, Snippets = PersonalDialogs.Snippets };
        static UserSettings Load()
        {
            var s = new UserSettings();
            try
            {
                if (!File.Exists(PathName))
                    return s;
                var root = XElement.Load(PathName);
                s.Enabled = (bool?)root.Element("Enabled") ?? true;
                s.InlineDiagnostics = (bool?)root.Element("InlineDiagnostics") ?? true;
                s.UseBrackets = (bool?)root.Element("UseBrackets") ?? true;
                s.AutoConvert = (bool?)root.Element("AutoConvert") ?? true;
                s.RestoreSession = (bool?)root.Element("RestoreSession") ?? true;
                s.FuzzyMatching = (bool?)root.Element("FuzzyMatching") ?? true;
                s.TableAliases = (bool?)root.Element("TableAliases") ?? true;
                s.QualifyColumns = (bool?)root.Element("QualifyColumns") ?? true;
                s.ExclusiveCompletion = (bool?)root.Element("ExclusiveCompletion") ?? true;
                string theme = (string)root.Element("Theme");
                if (theme == "Dark" || theme == "Light" || theme == "Auto" || theme == "Purple")
                    s.Theme = theme;
                s.FontSize = Math.Max(10, Math.Min(22, (int?)root.Element("FontSize") ?? 12));
                s.MaxResults = Math.Max(20, Math.Min(500, (int?)root.Element("MaxResults") ?? 200));
            }
            catch (Exception ex) { Diagnostics.Write("Settings load failed: " + ex.GetType().Name); }
            return s;
        }
        internal static void Save()
        {
            var dispatcher = Controller.Active?.View.VisualElement.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(new Action(Save));
                return;
            }
            var s = Current;
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            var root = new XElement("SqlPilotSettings", new XElement("Enabled", s.Enabled), new XElement("UseBrackets", s.UseBrackets), new XElement("InlineDiagnostics", s.InlineDiagnostics), new XElement("AutoConvert", s.AutoConvert), new XElement("RestoreSession", s.RestoreSession), new XElement("FuzzyMatching", s.FuzzyMatching),
                new XElement("TableAliases", s.TableAliases), new XElement("QualifyColumns", s.QualifyColumns),
                new XElement("ExclusiveCompletion", s.ExclusiveCompletion), new XElement("Theme", s.Theme),
                new XElement("FontSize", s.FontSize), new XElement("MaxResults", s.MaxResults));
            string temporary = PathName + ".tmp";
            root.Save(temporary);
            if (File.Exists(PathName))
                File.Replace(temporary, PathName, null);
            else
                File.Move(temporary, PathName);
            Changed?.Invoke(null, EventArgs.Empty);
        }
        internal static void Show()
        {
            var s = Current;
            var panel = new StackPanel { Margin = new Thickness(0) };
            panel.Children.Add(new TextBlock { Text = "Completion settings", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
            Func<string, bool, CheckBox> check = (text, value) => { var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 6, 0, 6) }; panel.Children.Add(box); return box; };
            var enabled = check("Enable SqlPilot completion", s.Enabled);
            var diagnostics = check("Show inline errors, warnings and improvement suggestions", s.InlineDiagnostics);
            var convert = check("Auto Convert typed && / || / == / != in SQL conditions", s.AutoConvert);
            var session = check("Remember open SQL tabs and restore their connections", s.RestoreSession);
            var exclusive = check("Suppress other completion providers while SqlPilot is enabled", s.ExclusiveCompletion);
            var fuzzy = check("Flexible matching: initials, contains and ordered letters", s.FuzzyMatching);
            panel.Children.Add(new TextBlock { Text = "Example: ar matches Alarm_Recivers", Margin = new Thickness(22, 0, 0, 8), Foreground = System.Windows.Media.Brushes.DimGray });
            var tables = check("Insert table aliases in FROM / JOIN (AS [ar])", s.TableAliases);
            var brackets = check("Use brackets for generated identifiers (required brackets are always kept)", s.UseBrackets);
            var columns = check("Qualify column suggestions with existing aliases ([ar].[Id])", s.QualifyColumns);
            Func<string, object[], object, ComboBox> choice = (label, values, selected) =>
            {
                panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 14, 0, 5) });
                var combo = new ComboBox { ItemsSource = values, SelectedItem = selected, Width = 170, HorizontalAlignment = HorizontalAlignment.Left };
                panel.Children.Add(combo);
                return combo;
            };
            var theme = choice("Suggestion theme", new object[] { "Purple", "Auto", "Light", "Dark" }, s.Theme);
            var font = choice("Suggestion font size", new object[] { 10, 12, 14, 16, 18, 20, 22 }, s.FontSize);
            var limit = choice("Maximum suggestions", new object[] { 50, 100, 200, 500 }, s.MaxResults);
            var keyboard = new Button { Content = "Edit keyboard shortcuts…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            keyboard.Click += (_, __) => KeyboardShortcuts.Show();
            panel.Children.Add(keyboard);
            var snippets = new Button { Content = "Edit Tab shortcuts…", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 16, 0, 0) };
            snippets.Click += (_, __) => PersonalDialogs.ShowSnippets();
            panel.Children.Add(snippets);
            var body = new DockPanel();
            var window = SqlPilot.UI.Design.Window("Settings", body, 700, 800, "Completion, editing and session preferences");
            window.Icon = Brand.Icon();
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(0, 0, 10, 0) };
            var save = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(18, 7, 18, 7) };
            cancel.Click += (_, __) => window.Close();
            save.Click += (_, __) =>
            {
                bool prior = s.Enabled;
                s.UseBrackets = brackets.IsChecked == true;
                s.InlineDiagnostics = diagnostics.IsChecked == true;
                s.Enabled = enabled.IsChecked == true;
                s.AutoConvert = convert.IsChecked == true;
                s.RestoreSession = session.IsChecked == true;
                s.ExclusiveCompletion = exclusive.IsChecked == true;
                s.FuzzyMatching = fuzzy.IsChecked == true;
                s.TableAliases = tables.IsChecked == true;
                s.QualifyColumns = columns.IsChecked == true;
                s.Theme = (string)theme.SelectedItem;
                s.FontSize = (int)font.SelectedItem;
                s.MaxResults = (int)limit.SelectedItem;
                try
                {
                    Save();
                    CompletionOwnership.ApplySettings();
                    window.Close();
                }
                catch (Exception ex) { s.Enabled = prior; MessageBox.Show("Settings could not be saved: " + ex.GetType().Name, "SqlPilot Settings"); }
            };
            footer.Children.Add(cancel);
            footer.Children.Add(save);
            DockPanel.SetDock(footer, Dock.Bottom);
            body.Children.Add(footer);
            body.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            window.ShowDialog();
        }
    }
}
