// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
namespace SqlPilot.Core
{
    public static partial class Engine
    {
        public static string AcceptedText(string text, string following = "")
        {
            if (string.IsNullOrEmpty(text) || char.IsWhiteSpace(text[text.Length - 1]) || text.EndsWith(".") || text.EndsWith("("))
                return text;
            if (following.Length > 0 && ((following[0] == ' ' || following[0] == '\t') || ".,);".Contains(following[0])))
                return text;
            return text + " ";
        }
        // Convert only the operator just completed by a delimiter, never old text or pasted documents.
        public static Issue AutoConversion(string sql, int caret)
        {
            if (caret < 1 || caret > sql.Length || !char.IsWhiteSpace(sql[caret - 1]))
                return null;
            int end = caret - 1;
            while (end > 0 && char.IsWhiteSpace(sql[end - 1]))
                end--;
            foreach (var pair in new[] { Tuple.Create("&&", "AND"), Tuple.Create("||", "OR"), Tuple.Create("==", "="), Tuple.Create("!=", "<>") })
            {
                int start = end - pair.Item1.Length;
                if (start < 0 || sql.Substring(start, pair.Item1.Length) != pair.Item1)
                    continue;
                if (!IsCode(sql, start) || !IsCode(sql, end))
                    return null;
                var prior = ContextTokens(sql.Substring(0, start));
                string clause = prior.LastOrDefault(t => new[] { "WHERE", "ON", "HAVING", "IF", "WHILE", "WHEN" }.Contains(t.Text.ToUpperInvariant()))?.Text.ToUpperInvariant();
                if (clause == null)
                    return null; // SELECT string concatenation and assignment syntax are intentionally untouched.
                string before = sql.Substring(0, start);
                int scope = SelectScopeStart(before, before.Length);
                var decision = DecideContext(before.Substring(scope));
                if (pair.Item1 == "&&" || pair.Item1 == "||")
                {
                    if (!decision.Words.Contains("AND"))
                        return null;
                }
                else if (!decision.Words.Contains("="))
                    return null;
                string prefix = start > 0 && !char.IsWhiteSpace(sql[start - 1]) ? " " : "";
                return new Issue { Code = "AUTO-CONVERT", Offset = start, Length = pair.Item1.Length, Replacement = prefix + pair.Item2, Message = pair.Item1 + " → " + pair.Item2 };
            }
            return null;
        }
        static string PreserveComments(string original, string formatted)
        {
            var parser = new TSql180Parser(true);
            var source = parser.GetTokenStream(new StringReader(original), out var ignored);
            var comments = source.Where(t => t.TokenType == TSqlTokenType.SingleLineComment || t.TokenType == TSqlTokenType.MultilineComment).ToList();
            if (comments.Count == 0)
                return formatted;
            var target = ContextTokens(formatted);
            var significant = ContextTokens(original);
            var positions = new Dictionary<int, int>();
            int output = 0;
            foreach (var token in significant)
            {
                if (token.Text == ";")
                {
                    if (output < target.Count && target[output].Text == ";")
                        output++;
                    continue;
                }
                while (output < target.Count && target[output].Text == ";")
                    output++;
                if (output >= target.Count || !string.Equals(token.Text, target[output].Text, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Formatting was skipped because comments could not be preserved safely. Your SQL is unchanged.");
                positions[token.Offset] = target[output++].Offset;
            }
            var inserts = new SortedDictionary<int, StringBuilder>();
            foreach (var comment in comments)
            {
                var next = significant.FirstOrDefault(t => t.Offset > comment.Offset && positions.ContainsKey(t.Offset));
                int offset = next == null ? formatted.Length : positions[next.Offset];
                if (!inserts.TryGetValue(offset, out var text))
                    inserts[offset] = text = new StringBuilder();
                // A line comment must always end before the following generated SQL token.
                text.Append(comment.Text).Append(comment.TokenType == TSqlTokenType.SingleLineComment ? "\r\n" : " ");
            }
            foreach (var item in inserts.Reverse())
            {
                string prefix = item.Key > 0 && !char.IsWhiteSpace(formatted[item.Key - 1]) ? " " : "";
                formatted = formatted.Insert(item.Key, prefix + item.Value);
            }
            return formatted;
        }
    }
}
