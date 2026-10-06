// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.SqlClient;
using Process = System.Diagnostics.Process;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio.Text;
namespace SqlPilot.Ssms
{
    internal static class Sessions
    {
        static DTE dte; static DispatcherTimer timer; static DTEEvents events; static bool ready, restoring, stopping;
        static readonly Dictionary<string, string> connections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string folder, ownFile; static int startupTicks;
        static readonly Dictionary<string, string> originalPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly List<SessionTab> pending = new List<SessionTab>();
        static readonly Dictionary<string, Controller> views = new Dictionary<string, Controller>(StringComparer.OrdinalIgnoreCase);
        internal static void Register(Controller controller)
        {
            try
            {
                var doc = dte?.ActiveDocument;
                if (doc != null && IsSql(doc))
                    views[Key(doc)] = controller;
            }
            catch { }
        }
        static TextDocument Automation(Document doc)
        {
            try
            {
                return doc.Object("TextDocument") as TextDocument;
            }
            catch { return null; }
        }
        static string Text(Document doc)
        {
            var automation = Automation(doc);
            if (automation != null)
                return automation.StartPoint.CreateEditPoint().GetText(automation.EndPoint);
            if (views.TryGetValue(Key(doc), out var c) && !c.View.IsClosed)
                return c.View.TextBuffer.CurrentSnapshot.GetText();
            throw new InvalidOperationException("SQL document text is unavailable.");
        }
        static void SetText(Document doc, string sql)
        {
            var automation = Automation(doc);
            if (automation != null)
            {
                var edit = automation.StartPoint.CreateEditPoint();
                edit.Delete(automation.EndPoint);
                edit.Insert(sql);
                return;
            }
            var c = EditorBootstrap.Attach();
            if (c == null)
                throw new InvalidOperationException("SQL editor is unavailable.");
            c.View.TextBuffer.Replace(new Span(0, c.View.TextBuffer.CurrentSnapshot.Length), sql);
        }
        static void MoveCaret(Document doc, int line, int column)
        {
            var automation = Automation(doc);
            if (automation != null)
            {
                automation.Selection.MoveToLineAndOffset(Math.Min(line, automation.EndPoint.Line), Math.Max(1, column));
                return;
            }
            var c = EditorBootstrap.Attach();
            if (c == null)
                return;
            var snapshot = c.View.TextBuffer.CurrentSnapshot;
            var row = snapshot.GetLineFromLineNumber(Math.Max(0, Math.Min(line - 1, snapshot.LineCount - 1)));
            c.View.Caret.MoveTo(new SnapshotPoint(snapshot, row.Start.Position + Math.Max(0, Math.Min(column - 1, row.Length))));
            c.View.Caret.EnsureVisible();
        }
        internal static void Start(DTE host)
        {
            if (dte != null || host == null)
                return;
            dte = host;
            using (var sha = SHA256.Create())
                folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "Sessions", BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Process.GetCurrentProcess().MainModule.FileName))).Replace("-", "").Substring(0, 16));
            ownFile = System.IO.Path.Combine(folder, "session-" + Process.GetCurrentProcess().Id + "-" + DateTime.UtcNow.Ticks + ".bin");
            events = dte.Events.DTEEvents;
            events.OnBeginShutdown += Shutdown;
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, __) =>
            {
                if (stopping || restoring)
                    return;
                if (!ready)
                {
                    if (++startupTicks < 2)
                        return;
                    ready = true;
                    if (UserSettings.Current.RestoreSession)
                    {
                        RestoreLatest();
                        RememberAllConnections();
                    }
                }
                if (UserSettings.Current.RestoreSession)
                    Capture();
            };
            timer.Start();
        }
        static object Factory()
        {
            var a = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == "SqlPackageBase");
            return a?.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache")?.GetProperty("ScriptFactory", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }
        static string Key(Document document) => document.FullName;
        static bool IsSql(Document document) => System.IO.Path.GetExtension(document.FullName).Equals(".sql", StringComparison.OrdinalIgnoreCase);
        static List<Document> Documents() => dte.Documents.Cast<Document>().Where(IsSql).ToList();
        internal static void RememberActive()
        {
            try
            {
                var doc = dte?.ActiveDocument;
                if (doc != null && IsSql(doc))
                {
                    string connection = ActiveConnection.TryGet();
                    if (connection != null)
                        connections[Key(doc)] = connection;
                    else
                        connections.Remove(Key(doc));
                }
            }
            catch { }
        }
        static void RememberAllConnections()
        {
            try
            {
                var active = dte.ActiveWindow;
                foreach (var doc in Documents())
                {
                    doc.Activate();
                    EditorBootstrap.Attach();
                    RememberActive();
                }
                active?.Activate();
            }
            catch (Exception ex) { Diagnostics.Write("Session connections: " + ex.GetType().Name); }
        }
        static void Shutdown()
        {
            // Freeze the complete set before SSMS begins closing individual document windows.
            if (stopping)
                return;
            if (UserSettings.Current.RestoreSession)
            {
                RememberAllConnections();
                Capture();
            }
            stopping = true;
            timer?.Stop();
        }
        internal static void Capture()
        {
            if (stopping || restoring || dte == null)
                return;
            try
            {
                RememberActive();
                var active = dte.ActiveDocument;
                var tabs = new List<SessionTab>();
                foreach (var doc in Documents())
                {
                    var text = Automation(doc);
                    int line = text?.Selection.CurrentLine ?? 1, column = text?.Selection.CurrentColumn ?? 1;
                    if (text == null && views.TryGetValue(Key(doc), out var c) && !c.View.IsClosed)
                    {
                        var caret = c.View.Caret.Position.BufferPosition;
                        var row = caret.GetContainingLine();
                        line = row.LineNumber + 1;
                        column = caret.Position - row.Start.Position + 1;
                    }
                    connections.TryGetValue(Key(doc), out string connection);
                    tabs.Add(new SessionTab { Title = doc.Name, Path = originalPaths.TryGetValue(Key(doc), out string original) ? original : doc.FullName, Sql = Text(doc), Connection = connection, Line = line, Column = column, Dirty = !doc.Saved, Active = active != null && Key(active) == Key(doc) });
                }
                tabs.AddRange(pending);
                SessionFiles.Save(ownFile, tabs);
            }
            catch (Exception ex) { Diagnostics.Write("Session checkpoint failed: " + ex.GetType().Name); }
        }
        static bool Alive(string path)
        {
            if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(path).Split('-')[1], out int pid))
                return true;
            try
            {
                var process = Process.GetProcessById(pid);
                return !process.HasExited && process.ProcessName.Equals("ssms", StringComparison.OrdinalIgnoreCase) && process.StartTime.ToUniversalTime() <= File.GetLastWriteTimeUtc(path);
            }
            catch { return false; }
        }
        internal static void RestoreLatest()
        {
            if (restoring || dte == null)
                return;
            restoring = true;
            int failed = 0;
            try
            {
                if (!Directory.Exists(folder))
                    return;
                var paths = Directory.GetFiles(folder, "session-*.bin").Where(p => p != ownFile && !Alive(p)).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
                if (paths.Count == 0)
                    return;
                List<SessionTab> tabs = null;
                foreach (var path in paths)
                    try
                    {
                        tabs = SessionFiles.Load(path);
                        break;
                    }
                    catch { try { tabs = SessionFiles.Load(path + ".bak"); break; } catch { } }
                if (tabs == null)
                    throw new InvalidDataException("No readable session checkpoint was found.");
                var existing = Documents();
                Document selected = null;
                pending.Clear();
                foreach (var tab in tabs)
                {
                    try
                    {
                        var same = existing.FirstOrDefault(doc => (originalPaths.TryGetValue(Key(doc), out string origin) ? origin : Key(doc)).Equals(tab.Path ?? "", StringComparison.OrdinalIgnoreCase) && Text(doc) == tab.Sql);
                        Document opened = same;
                        if (same == null)
                        {
                            OpenConnected(tab.Connection, existing.Any(doc => Key(doc).Equals(tab.Path ?? "", StringComparison.OrdinalIgnoreCase)) ? null : tab.Path);
                            opened = dte.ActiveDocument;
                            if (opened == null)
                                throw new InvalidOperationException("SSMS did not create a SQL tab.");
                            SetText(opened, tab.Sql ?? "");
                            bool fileUnchanged = File.Exists(tab.Path) && File.ReadAllText(tab.Path) == tab.Sql && Key(opened).Equals(tab.Path, StringComparison.OrdinalIgnoreCase);
                            opened.Saved = !tab.Dirty && fileUnchanged;
                            if (!fileUnchanged)
                                try
                                {
                                    opened.Windows.Item(1).Caption = (tab.Title ?? "SQL query") + " · Restored";
                                }
                                catch { }
                        }
                        else
                            same.Activate();
                        MoveCaret(opened, tab.Line, tab.Column);
                        originalPaths[Key(opened)] = tab.Path ?? Key(opened);
                        existing.Add(opened);
                        if (tab.Active)
                            selected = opened;
                        if (!string.IsNullOrEmpty(tab.Connection))
                            connections[Key(opened)] = tab.Connection;
                    }
                    catch (Exception ex) { pending.Add(tab); failed++; Diagnostics.Write("Session tab restore failed: " + ex.GetType().Name); }
                }
                selected?.Activate();
                if (failed > 0)
                    Notifications.Show(failed + " SQL tab(s) could not be restored. Your encrypted session checkpoint has been kept. Use SqlPilot → Restore last session after the host connection is available.");
            }
            catch (Exception ex) { Diagnostics.Write("Session restore: " + ex.GetType().Name); Notifications.Show("The saved session could not be restored. Your checkpoint has been kept."); }
            finally { restoring = false; }
        }
        static void OpenConnected(string connection, string originalPath)
        {
            var factory = Factory();
            if (factory == null)
                throw new InvalidOperationException("SSMS script factory is unavailable.");
            var contract = factory.GetType().GetInterfaces().First(t => t.Name == "IScriptFactory");
            var method = contract.GetMethods().First(m => m.Name == "CreateNewBlankScript" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType.Name == "UIConnectionInfo");
            var parameters = method.GetParameters();
            object script = Enum.Parse(parameters[0].ParameterType, "Sql");
            if (string.IsNullOrEmpty(connection))
            {
                if (!string.IsNullOrEmpty(originalPath) && File.Exists(originalPath))
                {
                    dte.ItemOperations.OpenFile(originalPath);
                    return;
                }
                var blank = contract.GetMethods().First(m => m.Name == "CreateNewBlankScript" && m.GetParameters().Length == 1);
                blank.Invoke(factory, new[] { script });
                return;
            }
            var b = new SqlConnectionStringBuilder(connection);
            var type = parameters[1].ParameterType;
            var info = Activator.CreateInstance(type);
            Action<string, object> set = (name, value) => type.GetProperty(name)?.SetValue(info, value);
            set("ServerType", new Guid("8c91a03d-f9b4-46c0-a305-b5dcc79ff907"));
            set("ServerName", b.DataSource);
            set("AuthenticationType", b.IntegratedSecurity ? 0 : 1);
            set("UserName", b.UserID);
            set("Password", b.Password);
            set("PersistPassword", false);
            set("ApplicationName", "SqlPilot session");
            var options = (NameValueCollection)type.GetProperty("AdvancedOptions").GetValue(info);
            options["DATABASE"] = b.InitialCatalog;
            options["DATABASE_NAME"] = b.InitialCatalog;
            options["ENCRYPT_CONNECTION"] = b.Encrypt.ToString();
            options["TRUST_SERVER_CERTIFICATE"] = b.TrustServerCertificate.ToString();
            // SSMS establishes the tab connection; no SQL execution command is invoked.
            if (!string.IsNullOrEmpty(originalPath) && File.Exists(originalPath))
            {
                var open = contract.GetMethods().First(m => m.Name == "CreateNewScript" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType.Name == "UIConnectionInfo");
                open.Invoke(factory, new object[] { originalPath, info, null });
            }
            else
                method.Invoke(factory, new[] { script, info, null });
        }
    }
}
