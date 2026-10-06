// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlPilot.Core
{
    public static partial class Engine
    {
        enum CompletionRole
        {
            Legacy, Columns, Keywords, None
        }
        sealed class ContextDecision
        {
            internal CompletionRole Role;
            internal string[] Words = Array.Empty<string>();
            internal bool JoinPredicate = true;
        }
        static readonly string[] SourceContinuations = { "WHERE", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN", "CROSS APPLY", "OUTER APPLY", "GROUP BY", "ORDER BY" };
        static ContextDecision ContextWords(params string[] words) => new ContextDecision { Role = CompletionRole.Keywords, Words = words };
        static ContextDecision ContextColumns(params string[] words) => new ContextDecision { Role = CompletionRole.Columns, Words = words };
        static bool CompleteScalar(string sql)
        {
            var parser = new TSql180Parser(true);
            var expression = parser.ParseExpression(new StringReader(sql), out var errors, 0, 1, 1);
            return expression != null && errors.Count == 0;
        }
        static bool CompletePredicate(string sql)
        {
            var parser = new TSql180Parser(true);
            var expression = parser.ParseBooleanExpression(new StringReader(sql), out var errors, 0, 1, 1);
            return expression != null && errors.Count == 0;
        }
        static void CrossJoinIssues(string sql, List<Issue> issues)
        {
            var tokens = ContextTokens(sql);
            var crosses = new Dictionary<int, TSqlParserToken>();
            int depth = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                string word = tokens[i].Text.ToUpperInvariant();
                if (word == "(")
                {
                    depth++;
                    continue;
                }
                if (word == ")")
                {
                    crosses.Remove(depth);
                    depth = Math.Max(0, depth - 1);
                    continue;
                }
                if (word == ";" || word == "GO")
                {
                    crosses.Clear();
                    depth = 0;
                    continue;
                }
                if (word == "FROM" || word == "WHERE" || word == "APPLY")
                    crosses.Remove(depth);
                if (word == "JOIN")
                {
                    crosses.Remove(depth);
                    if (i > 0 && tokens[i - 1].Text.Equals("CROSS", StringComparison.OrdinalIgnoreCase))
                        crosses[depth] = tokens[i - 1];
                }
                if (word == "ON" && crosses.TryGetValue(depth, out var cross))
                {
                    issues.Add(new Issue { Code = "JOIN-ON", Message = "CROSS JOIN cannot have ON. Change CROSS to INNER for a conditional join, or remove ON for a Cartesian product.", Offset = cross.Offset, Length = cross.Text.Length, Replacement = "INNER" });
                    crosses.Remove(depth);
                }
            }
        }
        static ContextDecision DecideContext(string statement)
        {
            var raw = ContextTokens(statement);
            var top = new List<TSqlParserToken>();
            int depth = 0;
            foreach (var token in raw)
            {
                if (token.Text == "(")
                    depth++;
                if (depth == 0)
                    top.Add(token);
                if (token.Text == ")")
                    depth = Math.Max(0, depth - 1);
            }
            int clause = top.FindLastIndex(t => new[] { "SELECT", "FROM", "JOIN", "APPLY", "WHERE", "ON", "HAVING", "SET", "GROUP", "ORDER", "VALUES", "EXEC", "EXECUTE", "INTO", "UPDATE" }.Contains(t.Text.ToUpperInvariant()));
            if (clause < 0)
                return new ContextDecision();
            string kind = top[clause].Text.ToUpperInvariant();
            bool selectStatement = top.Any(t => t.Text.Equals("SELECT", StringComparison.OrdinalIgnoreCase));
            int join = top.FindLastIndex(t => t.Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase));
            bool cross = join > 0 && top[join - 1].Text.Equals("CROSS", StringComparison.OrdinalIgnoreCase);
            string body = statement.Substring(top[clause].Offset + top[clause].Text.Length).Trim();
            var bodyTokens = ContextTokens(body);
            string last = bodyTokens.LastOrDefault()?.Text.ToUpperInvariant() ?? "";
            if (kind == "ON" && cross)
                return new ContextDecision { Role = CompletionRole.None, JoinPredicate = false };
            if (kind == "WHERE" || kind == "ON" || kind == "HAVING")
            {
                if (bodyTokens.Count == 0)
                    return ContextColumns("EXISTS", "NOT EXISTS");
                if (CompletePredicate(body))
                    return kind == "ON" ? ContextWords("AND", "OR", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN", "WHERE", "GROUP BY", "ORDER BY") :
                    kind == "HAVING" ? ContextWords("AND", "OR", "ORDER BY") : selectStatement ? ContextWords("AND", "OR", "GROUP BY", "ORDER BY") : ContextWords("AND", "OR");
                if (last == "IS")
                    return ContextWords("NULL", "NOT NULL");
                if (last == "NOT" && bodyTokens.Count > 1 && bodyTokens[bodyTokens.Count - 2].Text.Equals("IS", StringComparison.OrdinalIgnoreCase))
                    return ContextWords("NULL");
                if (last == "NOT")
                    return ContextWords("IN", "LIKE", "BETWEEN", "EXISTS");
                // BETWEEN's AND belongs to the range, not to a second Boolean predicate.
                int atomStart = 0;
                bool rangeAnd = false;
                for (int i = 0; i < bodyTokens.Count; i++)
                {
                    string word = bodyTokens[i].Text.ToUpperInvariant();
                    if (word == "BETWEEN")
                        rangeAnd = true;
                    else if (word == "AND" && rangeAnd)
                        rangeAnd = false;
                    else if (word == "AND" || word == "OR")
                    {
                        atomStart = i + 1;
                        rangeAnd = false;
                    }
                }
                string atom = atomStart < bodyTokens.Count ? body.Substring(bodyTokens[atomStart].Offset).Trim() : "";
                while (atom.StartsWith("(", StringComparison.Ordinal))
                    atom = atom.Substring(1).TrimStart();
                var atomTokens = ContextTokens(atom);
                int between = atomTokens.FindLastIndex(t => t.Text.Equals("BETWEEN", StringComparison.OrdinalIgnoreCase));
                if (between >= 0)
                {
                    var range = atomTokens.Skip(between + 1).ToList();
                    if (!range.Any(t => t.Text.Equals("AND", StringComparison.OrdinalIgnoreCase)) && range.Count > 0 && CompleteScalar(atom.Substring(range[0].Offset)))
                        return ContextWords("AND");
                    if (last == "AND")
                        return ContextColumns("NULL");
                }
                if (atom.Length > 0 && CompletePredicate(atom))
                    return ContextWords("AND", "OR", ")");
                if (atom.Length > 0 && CompleteScalar(atom))
                    return ContextWords("=", "<>", ">", "<", ">=", "<=", "IS NULL", "IS NOT NULL", "LIKE", "IN", "BETWEEN", "NOT IN", "NOT LIKE");
                if (last == "AND" || last == "OR")
                    return ContextColumns("EXISTS", "NOT EXISTS");
                if (last == "(" && bodyTokens.Count > 1 && bodyTokens[bodyTokens.Count - 2].Text.Equals("EXISTS", StringComparison.OrdinalIgnoreCase))
                    return ContextWords("SELECT");
                return ContextColumns("NULL");
            }
            if (kind == "FROM" || kind == "JOIN" || kind == "APPLY")
            {
                if (bodyTokens.Count == 0 || last == "," || last == ".")
                    return new ContextDecision { JoinPredicate = !cross };
                if (last == "AS")
                    return new ContextDecision();
                var parser = new TSql180Parser(true);
                parser.Parse(new StringReader("SELECT * FROM " + body), out var errors);
                if (errors.Count != 0)
                    return new ContextDecision { Role = CompletionRole.None, JoinPredicate = !cross };
                bool alias = bodyTokens.Count > 1 && (bodyTokens.Any(t => t.Text.Equals("AS", StringComparison.OrdinalIgnoreCase)) ||
                    (bodyTokens.Last().TokenType == TSqlTokenType.Identifier || bodyTokens.Last().TokenType == TSqlTokenType.QuotedIdentifier) && bodyTokens[bodyTokens.Count - 2].Text != ".");
                if (kind == "JOIN" && !cross)
                    return alias ? ContextWords("ON") : ContextWords("AS", "ON");
                if (!alias && selectStatement)
                {
                    var named = ContextWords(new[] { "AS" }.Concat(SourceContinuations).ToArray());
                    named.JoinPredicate = !cross;
                    return named;
                }
                if (!selectStatement && kind == "FROM")
                    return ContextWords("WHERE", "OUTPUT");
                var decision = ContextWords(SourceContinuations);
                decision.JoinPredicate = !cross;
                return decision;
            }
            if (kind == "ORDER" || kind == "GROUP")
            {
                if (bodyTokens.Count == 0)
                    return ContextWords("BY");
                if (!bodyTokens[0].Text.Equals("BY", StringComparison.OrdinalIgnoreCase))
                    return new ContextDecision { Role = CompletionRole.None };
                string expression = body.Substring(bodyTokens[0].Offset + bodyTokens[0].Text.Length).Trim();
                if (expression.Length == 0 || last == ",")
                    return ContextColumns();
                // Only the current list item determines whether it is still being written.
                int comma = bodyTokens.FindLastIndex(t => t.Text == ",");
                if (comma >= 0)
                    expression = body.Substring(bodyTokens[comma].Offset + 1).Trim();
                if (kind == "ORDER" && (last == "ASC" || last == "DESC"))
                    return ContextWords(",", "OFFSET");
                if (CompleteScalar(expression))
                    return kind == "ORDER" ? ContextWords("ASC", "DESC", ",", "OFFSET") : ContextWords(",", "HAVING", "ORDER BY");
                return ContextColumns();
            }
            if (kind == "SELECT")
            {
                if (body.Length == 0 || last == "DISTINCT" || last == "ALL" || last == "," || body.StartsWith("TOP", StringComparison.OrdinalIgnoreCase))
                    return new ContextDecision();
                int comma = bodyTokens.FindLastIndex(t => t.Text == ",");
                string expression = comma >= 0 ? body.Substring(bodyTokens[comma].Offset + 1).Trim() : body;
                if (expression == "*" || expression.EndsWith(".*", StringComparison.Ordinal))
                    return ContextWords("FROM");
                if (CompleteScalar(expression))
                    return ContextWords("AS", "FROM", ",");
                if (new[] { "+", "-", "*", "/", "%", "(" }.Contains(last))
                    return ContextColumns("NULL", "CASE", "COUNT(*)", "SUM", "AVG", "MIN", "MAX");
            }
            if (kind == "SET")
            {
                if (body.Length == 0 || last == ",")
                    return ContextColumns();
                int assignment = bodyTokens.FindLastIndex(t => t.Text == "=");
                if (assignment < 0 && CompleteScalar(body))
                    return ContextWords("=");
                if (assignment >= 0)
                {
                    string value = body.Substring(bodyTokens[assignment].Offset + 1).Trim();
                    if (CompleteScalar(value))
                        return top.Any(t => t.Text.Equals("UPDATE", StringComparison.OrdinalIgnoreCase)) ? ContextWords(",", "WHERE", "FROM", "OUTPUT") : new ContextDecision { Role = CompletionRole.None };
                    return ContextColumns("NULL", "DEFAULT");
                }
            }
            return new ContextDecision();
        }
    }
}
