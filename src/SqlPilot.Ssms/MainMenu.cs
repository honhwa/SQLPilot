// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EnvDTE;
namespace SqlPilot.Ssms
{
    internal static class MainMenu
    {
        internal static void Execute(string command)
        {
            var dispatcher = Controller.Active?.View.VisualElement.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => Execute(command)));
                return;
            }
            if (command == "Settings")
            {
                UserSettings.Show();
                return;
            }
            if (command == "Updates")
            {
                Updates.Show();
                return;
            }
            if (command == "Shortcuts")
            {
                KeyboardShortcuts.Show();
                return;
            }
            if (command == "Snippets")
            {
                PersonalDialogs.ShowSnippets();
                return;
            }
            if (command == "RestoreSession")
            {
                Sessions.RestoreLatest();
                return;
            }
            if (command == "Checkpoint")
            {
                Sessions.Capture();
                return;
            }
            var dte = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
            var active = EditorBootstrap.Attach() ?? Controller.Active;
            if (command == "Library")
            {
                PersonalDialogs.ShowLibrary(insert: active == null || active.View.IsClosed ? (Action<string>)null : active.InsertLibrarySql, open: p => dte.ItemOperations.OpenFile(p));
                return;
            }
            if (command == "Help")
            {
                Help(active);
                return;
            }
            if (active == null || active.View.IsClosed)
            {
                Notifications.Show("Open a SQL query tab first.");
                return;
            }
            switch (command)
            {
                case "Completion":
                    active.CompletionCommand();
                    break;
                case "Format":
                    active.Format();
                    break;
                case "Analyze":
                    active.AnalyzeDialog();
                    break;
                case "QuickFix":
                case "QuickFixAlt":
                    active.QuickFixCommand();
                    break;
                case "Refresh":
                    active.RefreshCommand();
                    break;
                case "Connect":
                    active.Connect();
                    break;
                case "History":
                    active.HistoryDialog();
                    break;
                case "SaveSql":
                    active.SaveToLibrary(p => dte.ItemOperations.OpenFile(p));
                    break;
                case "Reference":
                    active.NavigateReference(false);
                    break;
                case "Explorer":
                    active.NavigateReference(true);
                    break;
            }
        }
        internal static void Show(DTE dte)
        {
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            Func<string, string, MenuItem> item = (label, command) => { var m = new MenuItem { Header = label, InputGestureText = KeyboardShortcuts.Gesture(command) }; m.Click += (_, __) => Execute(command); return m; };
            var completion = new MenuItem { Header = "Enable completion", IsCheckable = true, IsChecked = CompletionOwnership.Enabled };
            completion.Click += (_, __) => CompletionOwnership.Toggle();
            menu.Items.Add(completion);
            menu.Items.Add(item("Show suggestions", "Completion"));
            menu.Items.Add(new Separator());
            var editing = new MenuItem { Header = "SQL editing" };
            editing.Items.Add(item("Format SQL", "Format"));
            editing.Items.Add(item("Analyze and fix", "Analyze"));
            editing.Items.Add(item("Quick fixes at cursor", "QuickFixAlt"));
            editing.Items.Add(item("Tab shortcuts…", "Snippets"));
            var convert = new MenuItem { Header = "Auto Convert operators", IsCheckable = true, IsChecked = UserSettings.Current.AutoConvert };
            convert.Click += (_, __) => { UserSettings.Current.AutoConvert = convert.IsChecked; UserSettings.Save(); };
            editing.Items.Add(convert);
            menu.Items.Add(editing);
            var files = new MenuItem { Header = "SQL files & history" };
            files.Items.Add(item("Save SQL to Library…", "SaveSql"));
            files.Items.Add(item("SQL Library…", "Library"));
            files.Items.Add(item("Query history…", "History"));
            menu.Items.Add(files);
            var database = new MenuItem { Header = "Database" };
            database.Items.Add(item("Refresh schema", "Refresh"));
            database.Items.Add(item("Metadata connection…", "Connect"));
            database.Items.Add(item("Go to reference / Modify", "Reference"));
            database.Items.Add(item("Locate in Object Explorer", "Explorer"));
            menu.Items.Add(database);
            var session = new MenuItem { Header = "Session" };
            session.Items.Add(item("Save session now", "Checkpoint"));
            session.Items.Add(item("Restore last session", "RestoreSession"));
            menu.Items.Add(session);
            menu.Items.Add(new Separator());
            menu.Items.Add(item("Keyboard shortcuts…", "Shortcuts"));
            menu.Items.Add(item("Settings…", "Settings"));
            menu.Items.Add(item(Updates.MenuLabel, "Updates"));
            menu.Items.Add(item("About & help", "Help"));
            menu.IsOpen = true;
        }
        internal static void Help(Controller active) => AboutDialog.Show(active?.Status);
    }
}
