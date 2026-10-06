// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using EnvDTE;

namespace SqlPilot.Ssms
{
    internal static class ShortcutRouter
    {
        // SSMS can translate accelerators (for example Ctrl+Shift+F / Find in Files)
        // before WPF PreviewKeyDown reaches the SQL text view.
        static CommandEvents events;
        static bool executing; static string lastCommand; static long lastTime;
        internal static void Start(DTE dte)
        {
            if (events != null)
                return;
            try
            {
                var candidate = dte.Events.CommandEvents[null, 0];
                candidate.BeforeExecute += BeforeExecute;
                events = candidate;
            }
            catch (Exception ex) { Diagnostics.Write("Shell shortcut routing initialization failed: " + ex); }
        }
        static void BeforeExecute(string guid, int id, object input, object output, ref bool cancelDefault)
        {
            if (executing)
                return;
            var active = Controller.Active;
            if (active == null || active.View.IsClosed || !active.View.HasAggregateFocus)
                return;
            var command = KeyboardShortcuts.MatchHeld(System.Windows.Input.Keyboard.Modifiers, System.Windows.Input.Keyboard.IsKeyDown);
            if (command == null)
                return;
            cancelDefault = true;
            long now = Environment.TickCount;
            if (command == lastCommand && unchecked((uint)(now - lastTime)) < 350)
                return;
            lastCommand = command;
            lastTime = now;
            executing = true;
            try
            {
                Diagnostics.Write("Shell shortcut routed: " + command);
                MainMenu.Execute(command);
            }
            catch (Exception ex) { Notifications.Show("SqlPilot command failed: " + ex.Message); }
            finally { executing = false; }
        }
    }
    internal static class Notifications
    {
        internal static void Show(string text)
        {
            Diagnostics.Write(text);
            try
            {
                var dte = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
                if (dte != null)
                    dte.StatusBar.Text = text;
            }
            catch (Exception ex) { Diagnostics.Write("Status notification failed: " + ex.GetType().Name); }
        }
    }
}
