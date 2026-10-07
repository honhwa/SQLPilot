// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SqlPilot.Core
{
    public static class PersonalFiles
    {
        public static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public sealed class SnippetStore
    {
        readonly string path;
        public SnippetStore(string path)
        {
            this.path = path;
        }
        public Dictionary<string, string> Load()
        {
            if (!File.Exists(path))
                return new Dictionary<string, string>(Engine.Snippets, StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in XElement.Load(path).Elements("Snippet"))
                result.Add((string)row.Attribute("Shortcut"), (string)row.Element("Sql") ?? "");
            Validate(result);
            return result;
        }
        public static void Validate(IDictionary<string, string> snippets)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in snippets)
            {
                if (!Regex.IsMatch(row.Key ?? "", @"^[A-Za-z_][A-Za-z0-9_]{0,39}$"))
                    throw new ArgumentException("Shortcuts must start with a letter or underscore and contain up to 40 letters, digits or underscores.");
                if (!used.Add(row.Key))
                    throw new ArgumentException("Each shortcut must be unique (case insensitive).");
                if (string.IsNullOrWhiteSpace(row.Value))
                    throw new ArgumentException("Every shortcut needs SQL text.");
            }
        }
        public void Save(IDictionary<string, string> snippets)
        {
            Validate(snippets);
            PersonalFiles.Write(path, new XElement("SqlPilotSnippets", snippets.OrderBy(r => r.Key).Select(r => new XElement("Snippet", new XAttribute("Shortcut", r.Key), new XElement("Sql", r.Value)))).ToString());
        }
    }
    public sealed class LibraryEntry
    {
        public string Id
        {
            get; set;
        }
        public string Title
        {
            get; set;
        }
        public string Category
        {
            get; set;
        }
        public string Tags
        {
            get; set;
        }
        public string Sql
        {
            get; set;
        }
        public DateTime UpdatedUtc
        {
            get; set;
        }
        public DateTime LastInsertedUtc
        {
            get; set;
        }
        public override string ToString() => Title + "  ·  " + Category;
    }
    public sealed class SqlLibrary
    {
        readonly string root;
        public SqlLibrary(string root)
        {
            this.root = Path.GetFullPath(root);
        }
        string EntryPath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out var value))
                throw new ArgumentException("Invalid library entry.");
            return Path.Combine(root, value.ToString("N") + ".xml");
        }
        public List<LibraryEntry> Load()
        {
            if (!Directory.Exists(root))
                return new List<LibraryEntry>();
            return Directory.GetFiles(root, "*.xml").Select(path =>
            {
                var row = XElement.Load(path);
                var id = Path.GetFileNameWithoutExtension(path);
                EntryPath(id);
                return new LibraryEntry { Id = id, Title = (string)row.Element("Title"), Category = (string)row.Element("Category") ?? "General", Tags = (string)row.Element("Tags") ?? "", Sql = (string)row.Element("Sql") ?? "", UpdatedUtc = (DateTime?)row.Element("UpdatedUtc") ?? DateTime.MinValue, LastInsertedUtc = (DateTime?)row.Element("LastInsertedUtc") ?? DateTime.MinValue };
            }).OrderByDescending(e => e.LastInsertedUtc).ThenByDescending(e => e.UpdatedUtc).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        }
        public LibraryEntry Save(LibraryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Title))
                throw new ArgumentException("Enter a title.");
            if (string.IsNullOrWhiteSpace(entry.Sql))
                throw new ArgumentException("Enter SQL text.");
            string id = entry.Id ?? Guid.NewGuid().ToString("N"), path = EntryPath(id);
            var time = DateTime.UtcNow;
            // Editing an entry must preserve usage order, even when the editor holds a stale copy.
            var lastInserted = File.Exists(path) ? (DateTime?)XElement.Load(path).Element("LastInsertedUtc") ?? DateTime.MinValue : DateTime.MinValue;
            PersonalFiles.Write(path, new XElement("SqlPilotQuery", new XElement("Title", entry.Title.Trim()), new XElement("Category", string.IsNullOrWhiteSpace(entry.Category) ? "General" : entry.Category.Trim()), new XElement("Tags", entry.Tags ?? ""), new XElement("Sql", entry.Sql), new XElement("UpdatedUtc", time), new XElement("LastInsertedUtc", lastInserted)).ToString());
            entry.Id = id;
            entry.UpdatedUtc = time;
            entry.LastInsertedUtc = lastInserted;
            SqlPath(entry);
            return entry;
        }
        public void MarkInserted(string id)
        {
            string path = EntryPath(id);
            var row = XElement.Load(path);
            var newest = Load().Select(e => e.LastInsertedUtc).DefaultIfEmpty(DateTime.MinValue).Max();
            var now = DateTime.UtcNow;
            if (newest >= now && newest < DateTime.MaxValue)
                now = newest.AddTicks(1);
            row.SetElementValue("LastInsertedUtc", now);
            PersonalFiles.Write(path, row.ToString());
        }
        public static IEnumerable<LibraryEntry> Search(IEnumerable<LibraryEntry> entries, string query, string category)
        {
            var words = (query ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return entries.Where(e => (string.IsNullOrEmpty(category) || string.Equals(category, e.Category, StringComparison.OrdinalIgnoreCase)) && words.All(w => string.Join("\n", e.Title, e.Category, e.Tags, e.Sql).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
        }
        public string SqlPath(LibraryEntry entry)
        {
            EntryPath(entry.Id);
            string path = Path.Combine(root, "Files", entry.Id + ".sql");
            PersonalFiles.Write(path, entry.Sql);
            return path;
        }
    }
}
