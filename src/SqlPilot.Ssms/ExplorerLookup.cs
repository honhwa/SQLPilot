// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
namespace SqlPilot.Ssms
{
    internal static class ExplorerLookup
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        internal static object Property(object value, string name) => value?.GetType().GetProperty(name, Flags)?.GetValue(value);
        internal static string Attribute(string urn, string name)
        {
            var m = Regex.Match(urn ?? "", "@" + name + "=\"((?:[^\"]|\"\")*)\"|@" + name + "='((?:[^']|'')*)'", RegexOptions.IgnoreCase);
            return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value.Replace("\"\"", "\"") : m.Groups[2].Value.Replace("''", "'")) : null;
        }
        internal static IEnumerable<object> Roots(object service)
        {
            object owner = service;
            for (Type type = service.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField("owner", Flags | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    owner = field.GetValue(service);
                    break;
                }
            }
            var tree = Property(owner, "Tree");
            var hierarchies = Property(tree, "Hierarchies") as IEnumerable;
            if (hierarchies == null)
                yield break;
            foreach (var pair in hierarchies)
            {
                var root = Property(Property(pair, "Value"), "Root");
                var item = Property(root, "ContainedItem");
                if (item != null)
                    yield return item;
            }
        }
        internal static IEnumerable<object> Children(object item)
        {
            var method = item.GetType().GetMethods(Flags).FirstOrDefault(m => m.Name == "GetChildren" && m.GetParameters().Length == 1);
            if (method == null)
                yield break;
            var scope = Enum.Parse(method.GetParameters()[0].ParameterType, "Any");
            var children = method.Invoke(item, new[] { scope }) as IEnumerable;
            if (children != null)
                foreach (var child in children)
                    yield return child;
        }
        internal static bool ServerMatches(object item, string server)
        {
            var context = Property(item, "Context");
            var urn = Convert.ToString(Property(context, "Context"));
            var connection = Property(context, "Connection");
            string actual = Convert.ToString(Property(connection, "ServerInstance") ?? Property(connection, "ServerName"));
            return EqualServer(server, actual) || EqualServer(server, Attribute(urn, "Name"));
        }
        static bool EqualServer(string a, string b) => !string.IsNullOrWhiteSpace(b) && string.Equals((a ?? "").Trim().Replace("tcp:", ""), (b ?? "").Trim().Replace("tcp:", ""), StringComparison.OrdinalIgnoreCase);
        internal static object Find(object parent, string kind, string name, string schema = null, int folderDepth = 3)
        {
            var children = Children(parent).ToList();
            foreach (var child in children)
            {
                var context = Property(child, "Context");
                string urn = Convert.ToString(Property(context, "Context"));
                string last = urn?.Substring(urn.LastIndexOf('/') + 1);
                if (last != null && last.StartsWith(kind + "[", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Attribute(last, "Name"), name, StringComparison.OrdinalIgnoreCase) &&
                   (schema == null || string.Equals(Attribute(last, "Schema"), schema, StringComparison.OrdinalIgnoreCase)))
                    return child;
            }
            if (folderDepth == 0)
                return null;
            foreach (var child in children)
            {
                var context = Property(child, "Context");
                string childUrn = Convert.ToString(Property(context, "Context"));
                string parentUrn = Convert.ToString(Property(Property(parent, "Context"), "Context"));
                if (childUrn != parentUrn)
                    continue; // folders share the parent's SMO context
                string invariant = Convert.ToString(Property(context, "InvariantName"));
                string label = Convert.ToString(Property(context, "Name"));
                string wanted = kind == "Database" ? "Databases" : kind == "Table" ? "Tables" : kind == "View" ? "Views" : kind == "StoredProcedure" ? "Stored Procedures" : "Functions";
                string hint = null;
                try
                {
                    hint = Convert.ToString(context.GetType().GetProperty("Item")?.GetValue(context, new object[] { "QueryHint" }));
                }
                catch { }
                if (!(string.Equals(invariant, wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(label, wanted, StringComparison.OrdinalIgnoreCase) ||
                     (hint ?? "").IndexOf(kind, StringComparison.OrdinalIgnoreCase) >= 0 || kind != "Database" && (label == "Programmability" || invariant == "Programmability")))
                    continue;
                var found = Find(child, kind, name, schema, folderDepth - 1);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
