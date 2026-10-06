// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlPilot.Core
{
    public static partial class Engine
    {
        static bool NameToken(TSqlParserToken t) => t.TokenType == TSqlTokenType.Identifier || t.TokenType == TSqlTokenType.QuotedIdentifier;
        static string Unquote(string text) => text.StartsWith("[") ? text.Substring(1, text.Length - 2).Replace("]]", "]") : text.StartsWith("\"") ? text.Substring(1, text.Length - 2).Replace("\"\"", "\"") : text;
        public static List<DbObject> ReferenceAt(string sql, int caret, IEnumerable<DbObject> catalog, string database)
        {
            if (caret < 0 || caret > sql.Length || !IsCode(sql, caret))
                return new List<DbObject>();
            var tokens = ContextTokens(sql);
            int index = tokens.FindIndex(t => NameToken(t) && caret >= t.Offset && caret < t.Offset + t.Text.Length);
            if (index < 0)
                index = tokens.FindIndex(t => NameToken(t) && caret == t.Offset + t.Text.Length);
            if (index < 0)
                return new List<DbObject>();
            int first = index, last = index;
            while (first >= 2 && tokens[first - 1].Text == "." && NameToken(tokens[first - 2]))
                first -= 2;
            while (last + 2 < tokens.Count && tokens[last + 1].Text == "." && NameToken(tokens[last + 2]))
                last += 2;
            var parts = new List<string>();
            for (int i = first; i <= last; i += 2)
                parts.Add(Unquote(tokens[i].Text));
            if (parts.Count > 3)
                throw new InvalidOperationException("Navigation to another server is not supported. Open a query connected to that server.");
            if (parts.Count == 3)
            {
                if (!string.Equals(parts[0], database, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Connect the query to database " + parts[0] + " before navigating to this reference.");
                parts.RemoveAt(0);
            }
            var objects = catalog.ToList();
            int scope = SelectScopeStart(sql, tokens[first].Offset);
            var bound = Bindings(sql.Substring(scope), objects);
            if (parts.Count == 1)
            {
                var alias = bound.Where(b => string.Equals(b.Item2, parts[0], StringComparison.OrdinalIgnoreCase)).Select(b => b.Item1).Distinct().ToList();
                if (alias.Count > 0)
                    return alias;
            }
            if (parts.Count == 2)
            {
                var alias = bound.Where(b => string.Equals(b.Item2, parts[0], StringComparison.OrdinalIgnoreCase) && b.Item1.Columns.Contains(parts[1], StringComparer.OrdinalIgnoreCase)).Select(b => b.Item1).Distinct().ToList();
                if (alias.Count > 0)
                    return alias;
            }
            return objects.Where(o => string.Equals(o.Name, parts.Last(), StringComparison.OrdinalIgnoreCase) && (parts.Count == 1 || string.Equals(o.Schema, parts[0], StringComparison.OrdinalIgnoreCase))).ToList();
        }
        public static string ModifyModule(string definition, string schema, string name)
        {
            var tokens = ContextTokens(definition);
            int start = tokens.FindIndex(t => t.Text.Equals("CREATE", StringComparison.OrdinalIgnoreCase) || t.Text.Equals("ALTER", StringComparison.OrdinalIgnoreCase));
            if (start < 0)
                throw new InvalidOperationException("The module definition has no supported CREATE or ALTER header.");
            int kind = start + 1;
            if (kind + 1 < tokens.Count && tokens[kind].Text.Equals("OR", StringComparison.OrdinalIgnoreCase) && tokens[kind + 1].Text.Equals("ALTER", StringComparison.OrdinalIgnoreCase))
                kind += 2;
            if (kind + 1 >= tokens.Count || !new[] { "PROC", "PROCEDURE", "VIEW", "FUNCTION" }.Contains(tokens[kind].Text.ToUpperInvariant()) || !NameToken(tokens[kind + 1]))
                throw new InvalidOperationException("This module type cannot be opened for modification.");
            int end = kind + 1;
            while (end + 2 < tokens.Count && tokens[end + 1].Text == "." && NameToken(tokens[end + 2]))
                end += 2;
            int length = tokens[end].Offset + tokens[end].Text.Length - tokens[start].Offset;
            return definition.Substring(0, tokens[start].Offset) + "ALTER " + tokens[kind].Text.ToUpperInvariant() + " " + DbObject.Quote(schema) + "." + DbObject.Quote(name) + definition.Substring(tokens[start].Offset + length);
        }
        public static string ObjectUrn(string server, string database, DbObject obj)
        {
            Func<string, string> escape = s => s.Replace("'", "''");
            string type = Relation(obj) && obj.Kind.IndexOf("VIEW", StringComparison.OrdinalIgnoreCase) < 0 && obj.Kind.IndexOf("SYNONYM", StringComparison.OrdinalIgnoreCase) < 0 ? "Table" : Procedure(obj) ? "StoredProcedure" : obj.Kind.IndexOf("VIEW", StringComparison.OrdinalIgnoreCase) >= 0 ? "View" : "UserDefinedFunction";
            return "Server[@Name='" + escape(server) + "']/Database[@Name='" + escape(database) + "']/" + type + "[@Name='" + escape(obj.Name) + "' and @Schema='" + escape(obj.Schema) + "']";
        }
    }
}
