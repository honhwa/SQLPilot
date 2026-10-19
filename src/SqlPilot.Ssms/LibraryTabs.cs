// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using EnvDTE;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    internal static class LibraryTabs
    {
        static DocumentEvents events;
        static readonly HashSet<string> owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static void Open(DTE dte, string id)
        {
            if (dte == null)
                throw new InvalidOperationException("The SSMS editor is unavailable.");
            var store = new SqlLibrary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "Library"));
            var entry = store.Load().SingleOrDefault(e => e.Id == id);
            if (entry == null)
                throw new InvalidOperationException("This saved query no longer exists. Edit its keyboard shortcut.");
            if (events == null)
            {
                events = dte.Events.DocumentEvents;
                // An explicit save makes the copy user-owned. Never delete saved edits.
                events.DocumentSaved += document => owned.Remove(document.FullName);
                events.DocumentClosing += document =>
                {
                    string path = document.FullName;
                    if (!owned.Contains(path))
                        return;
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        try
                        {
                            if (dte.Documents.Cast<Document>().Any(d => string.Equals(d.FullName, path, StringComparison.OrdinalIgnoreCase)))
                                return;
                            File.Delete(path);
                            owned.Remove(path);
                        }
                        catch (Exception ex) { Diagnostics.Write("Library tab cleanup: " + ex.GetType().Name); }
                    }));
                };
            }
            // Unique files force a separate editor tab and isolate edits from the saved query.
            // Closed, unmodified copies are deleted above; explicitly saved copies survive.
            string title = new string(entry.Title.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').Take(40).ToArray());
            string pathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "Definitions", "Library-" + title + "-" + Guid.NewGuid().ToString("N") + ".sql");
            PersonalFiles.Write(pathName, entry.Sql);
            owned.Add(pathName);
            try
            {
                dte.ItemOperations.OpenFile(pathName);
            }
            catch { owned.Remove(pathName); File.Delete(pathName); throw; }
            try
            {
                store.MarkInserted(entry.Id);
            }
            catch (Exception ex) { Diagnostics.Write("Library usage save: " + ex.GetType().Name); }
        }
    }
}
