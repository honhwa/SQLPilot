// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
namespace SqlPilot.Ssms
{
    internal sealed class SessionTab
    {
        internal string Title, Path, Sql, Connection;
        internal int Line = 1, Column = 1; internal bool Active, Dirty;
    }
    internal static class SessionFiles
    {
        internal static void Save(string path, IEnumerable<SessionTab> tabs)
        {
            var root = new XElement("Session", new XAttribute("Version", 1), tabs.Select(t => new XElement("Tab",
                new XAttribute("Line", t.Line), new XAttribute("Column", t.Column), new XAttribute("Active", t.Active), new XAttribute("Dirty", t.Dirty),
                new XElement("Title", t.Title ?? "Query.sql"), new XElement("Path", t.Path ?? ""),
                new XElement("Sql", new XAttribute("Encoding", "base64"), Convert.ToBase64String(Encoding.UTF8.GetBytes(t.Sql ?? ""))), new XElement("Connection", t.Connection ?? ""))));
            byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting)), null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path))
                File.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path);
        }
        internal static List<SessionTab> Load(string path)
        {
            if (!File.Exists(path))
                return new List<SessionTab>();
            var root = XElement.Parse(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)));
            if ((int?)root.Attribute("Version") != 1)
                throw new InvalidDataException("Unsupported session format.");
            return root.Elements("Tab").Select(t => new SessionTab { Title = (string)t.Element("Title"), Path = (string)t.Element("Path"), Sql = (string)t.Element("Sql")?.Attribute("Encoding") == "base64" ? Encoding.UTF8.GetString(Convert.FromBase64String((string)t.Element("Sql") ?? "")) : (string)t.Element("Sql"), Connection = (string)t.Element("Connection"), Line = Math.Max(1, (int?)t.Attribute("Line") ?? 1), Column = Math.Max(1, (int?)t.Attribute("Column") ?? 1), Active = (bool?)t.Attribute("Active") ?? false, Dirty = (bool?)t.Attribute("Dirty") ?? true }).ToList();
        }
    }
}
