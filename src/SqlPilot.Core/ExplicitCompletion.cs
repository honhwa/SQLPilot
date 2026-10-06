// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
namespace SqlPilot.Core
{
    public sealed class CompletionRequest
    {
        public int Start
        {
            get; set;
        }
        public int Length
        {
            get; set;
        }
        public List<Candidate> Candidates { get; set; } = new List<Candidate>();
    }
    public static partial class Engine
    {
        public static CompletionRequest RequestCompletion(string sql, int caret, IEnumerable<DbObject> catalog, bool explicitRequest, CompletionOptions options = null)
        {
            caret = Math.Max(0, Math.Min(sql.Length, caret));
            int start = WordStart(sql, caret);
            var result = new CompletionRequest { Start = start, Length = caret - start };
            if (!IsCode(sql, caret))
                return result;
            result.Candidates = Complete(sql, caret, catalog, !explicitRequest && start == caret, options);
            if (!explicitRequest)
                return result;
            // Repair an identifier being revisited after whitespace, or an unmatched prefix.
            var tokens = ContextTokens(sql.Substring(0, caret));
            var token = tokens.LastOrDefault();
            if (token == null || !(token.TokenType == TSqlTokenType.Identifier || token.TokenType == TSqlTokenType.QuotedIdentifier))
                return result;
            int nameIndex = tokens.Count - 1;
            while (nameIndex >= 2 && tokens[nameIndex - 1].Text == ".")
                nameIndex -= 2;
            bool unknownRelation = catalog.All(o => !string.Equals(o.Name, Unquote(token.Text), StringComparison.OrdinalIgnoreCase)) &&
               nameIndex > 0 && new[] { "FROM", "JOIN", "APPLY", "INTO", "UPDATE" }.Contains(tokens[nameIndex - 1].Text.ToUpperInvariant());
            bool unknownColumn = nameIndex > 0 && new[] { "SELECT", "DISTINCT", "WHERE", "ON", "AND", "OR", "BY", "SET", "," }.Contains(tokens[nameIndex - 1].Text.ToUpperInvariant()) &&
               !catalog.Any(o => o.Columns.Any(c => string.Equals(c, Unquote(token.Text), StringComparison.OrdinalIgnoreCase)));
            if (result.Candidates.Count > 0 && (start < caret || !unknownRelation && !unknownColumn))
            {
                var current = ContextTokens(sql).FirstOrDefault(t => t.Offset == start);
                if (current != null && start < caret)
                    result.Length = current.Text.Length;
                return result;
            }
            start = token.Offset;
            result.Candidates = Complete(sql, token.Offset + token.Text.Length, catalog, false, options);
            if (result.Candidates.Count == 0)
                result.Candidates = Complete(sql, start, catalog, false, options);
            if (result.Candidates.Count == 0)
                return result;
            int end = caret;
            // Replace the whole current identifier, preserving any whitespace after it.
            var full = ContextTokens(sql).FirstOrDefault(t => t.Offset == start);
            if (full != null)
                end = full.Offset + full.Text.Length;
            result.Start = start;
            result.Length = end - start;
            return result;
        }
    }
}
