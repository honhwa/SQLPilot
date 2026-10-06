// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlPilot.Core
{
    public static partial class Engine
    {
        static void OperatorIssues(string sql, List<Issue> issues)
        {
            foreach (Match match in Regex.Matches(sql, @"&&|\|\||=="))
            {
                if (!IsCode(sql, match.Index) || !IsCode(sql, match.Index + match.Length))
                    continue;
                string prefix = sql.Substring(0, match.Index + match.Length) + " ";
                var fix = AutoConversion(prefix, prefix.Length);
                if (fix == null)
                    continue;
                string replacement = fix.Replacement;
                if (match.Index + match.Length < sql.Length && !char.IsWhiteSpace(sql[match.Index + match.Length]))
                    replacement += " ";
                issues.Add(new Issue
                {
                    Code = "SQL-OPERATOR",
                    Offset = match.Index,
                    Length = match.Length,
                    Replacement = replacement,
                    Message = "'" + match.Value + "' is not the T-SQL operator for this condition. Use '" + replacement.Trim() + "' instead."
                });
            }
        }
        static List<Tuple<int, int>> RecoverySelects(string sql)
        {
            var tokens = ContextTokens(sql);
            var parts = new List<Tuple<int, int>>();
            int depth = 0, cases = 0, start = -1;
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Text == "(")
                    depth++;
                if (t.Text == ")")
                    depth = Math.Max(0, depth - 1);
                if (depth != 0)
                    continue;
                if (t.TokenType == TSqlTokenType.Case)
                    cases++;
                bool closing = t.TokenType == TSqlTokenType.End && cases == 0;
                if (t.TokenType == TSqlTokenType.End && cases > 0)
                    cases--;
                if (t.TokenType == TSqlTokenType.Go || t.Text == ";" || closing || t.TokenType == TSqlTokenType.Else)
                {
                    if (start >= 0)
                        parts.Add(Tuple.Create(start, t.Offset + (t.Text == ";" ? 1 : 0)));
                    start = -1;
                    continue;
                }
                if (t.TokenType != TSqlTokenType.Select)
                    continue;
                bool union = i > 0 && new[] { "UNION", "EXCEPT", "INTERSECT" }.Contains(tokens[i - 1].Text.ToUpperInvariant()) ||
                    i > 1 && tokens[i - 1].Text.Equals("ALL", StringComparison.OrdinalIgnoreCase) && tokens[i - 2].Text.Equals("UNION", StringComparison.OrdinalIgnoreCase);
                if (start >= 0 && !union)
                    parts.Add(Tuple.Create(start, t.Offset));
                if (start < 0 || !union)
                    start = t.Offset;
            }
            if (start >= 0)
                parts.Add(Tuple.Create(start, sql.Length));
            return parts;
        }
        static void RecoverFindings(string sql, List<Issue> issues, VariableInventory variables)
        {
            var parts = RecoverySelects(sql);
            if (parts.Count <= 1)
                return;
            foreach (var piece in parts)
            {
                int start = piece.Item1, end = piece.Item2;
                string part = sql.Substring(start, end - start);
                var tree = new TSql180Parser(true).Parse(new StringReader(part), out var errors);
                if (errors.Count > 0)
                    issues.RemoveAll(i => i.Code == "SYNTAX" && i.Offset >= start && i.Offset <= end);
                foreach (var error in errors)
                    issues.Add(new Issue
                    {
                        Code = "SYNTAX",
                        Message = error.Message,
                        Offset = start + Math.Max(0, Math.Min(part.TrimEnd().Length - 1, error.Offset)),
                        Length = 0,
                        SectionStart = start,
                        SectionEnd = end
                    });
                var local = new List<Issue>();
                tree?.Accept(new ReviewVisitor(local));
                foreach (var issue in local)
                {
                    issue.Offset += start;
                    if (issue.AnchorOffset.HasValue)
                        issue.AnchorOffset += start;
                    issue.SectionStart = start;
                    issue.SectionEnd = end;
                    issues.Add(issue);
                }
                variables.BaseOffset = start;
                tree?.Accept(variables);
            }
        }
        sealed class VariableInventory : TSqlFragmentVisitor
        {
            internal int BaseOffset;
            readonly List<Tuple<string, int>> declared = new List<Tuple<string, int>>();
            readonly HashSet<int> labels = new HashSet<int>();
            public override void ExplicitVisit(DeclareVariableElement node)
            {
                declared.Add(Tuple.Create(node.VariableName.Value, BaseOffset + node.VariableName.StartOffset));
                base.ExplicitVisit(node);
            }
            public override void ExplicitVisit(ProcedureParameter node)
            {
                declared.Add(Tuple.Create(node.VariableName.Value, BaseOffset + node.VariableName.StartOffset));
                base.ExplicitVisit(node);
            }
            public override void ExplicitVisit(DeclareTableVariableBody node)
            {
                declared.Add(Tuple.Create(node.VariableName.Value, BaseOffset + node.VariableName.StartOffset));
                base.ExplicitVisit(node);
            }
            public override void ExplicitVisit(ExecuteParameter node)
            {
                if (node.Variable != null)
                    labels.Add(BaseOffset + node.Variable.StartOffset);
                base.ExplicitVisit(node);
            }
            internal void AddIssues(string sql, List<Issue> issues)
            {
                var tokens = ContextTokens(sql);
                int batch = 0;
                foreach (var token in tokens)
                {
                    if (token.TokenType == TSqlTokenType.Go)
                    {
                        batch = token.Offset + token.Text.Length;
                        continue;
                    }
                    if (token.TokenType != TSqlTokenType.Variable || token.Text.StartsWith("@@", StringComparison.Ordinal) || labels.Contains(token.Offset))
                        continue;
                    if (declared.Any(d => d.Item2 >= batch && d.Item2 <= token.Offset && string.Equals(d.Item1, token.Text, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    issues.Add(new Issue { Code = "VARIABLE-UNDECLARED", Message = "Variable '" + token.Text + "' is not declared in this batch. Declare it before use and choose its SQL data type.", Offset = token.Offset, Length = token.Text.Length, SectionStart = batch });
                }
            }
        }
    }
}
