// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlPilot.Core
{
    public enum IssueSeverity
    {
        Suggestion, Warning, Error
    }
    public sealed class InlineIssue
    {
        public int Offset
        {
            get; set;
        }
        public int Length
        {
            get; set;
        }
        public IssueSeverity Severity
        {
            get; set;
        }
        public List<Issue> Issues
        {
            get; set;
        }
    }
    public static partial class Engine
    {
        public static IssueSeverity Severity(Issue issue) => issue.Code == "SYNTAX" || issue.Code == "JOIN-ON" || issue.Code == "VARIABLE-UNDECLARED" || issue.Code == "SQL-OPERATOR" ? IssueSeverity.Error :
            issue.Code == "SELECT-STAR" ? IssueSeverity.Suggestion : IssueSeverity.Warning;

        // Display spans are separate from fix spans: highlighting never changes a replacement target.
        public static List<InlineIssue> InlineIssues(string sql)
        {
            sql = sql ?? "";
            var parser = new TSql180Parser(true);
            var tokens = parser.GetTokenStream(new StringReader(sql), out var errors)
                .Where(t => t.TokenType != TSqlTokenType.WhiteSpace && t.TokenType != TSqlTokenType.SingleLineComment &&
                    t.TokenType != TSqlTokenType.MultilineComment && t.TokenType != TSqlTokenType.EndOfFile).ToList();
            var marks = new List<InlineIssue>();
            foreach (var issue in Analyze(sql))
            {
                int index = tokens.FindLastIndex(t => t.Offset <= (issue.AnchorOffset ?? issue.Offset));
                if (index < 0)
                    continue;
                int anchor = index;
                if (!issue.AnchorOffset.HasValue && (issue.Code == "SYNTAX" || issue.Code == "NULL-COMPARE" || issue.Code == "SELECT-STAR" || issue.Code == "VARIABLE-UNDECLARED" || issue.Code == "SQL-OPERATOR"))
                {
                    for (int i = index; i >= 0; i--)
                    {
                        var token = tokens[i];
                        if (token.Offset < issue.SectionStart)
                            break;
                        if (token.TokenType == TSqlTokenType.Semicolon || token.TokenType == TSqlTokenType.Go)
                        {
                            if (i == index)
                                continue;
                            break;
                        }
                        string word = token.Text.ToUpperInvariant();
                        if (issue.Code == "SELECT-STAR" ? word == "SELECT" :
                            new[] { "SELECT", "FROM", "WHERE", "ON", "HAVING", "SET", "UPDATE", "DELETE", "INSERT", "JOIN", "ORDER", "GROUP", "EXEC", "CREATE", "ALTER", "AND", "OR", "WHEN", "VALUES", "PARTITION" }.Contains(word))
                        {
                            anchor = i;
                            break;
                        }
                    }
                }
                var first = tokens[anchor];
                if (first.Offset < 0 || first.Offset >= sql.Length)
                    continue;
                marks.Add(new InlineIssue
                {
                    Offset = first.Offset,
                    Length = Math.Min(first.Text.Length, sql.Length - first.Offset),
                    Severity = Severity(issue),
                    Issues = new List<Issue> { issue }
                });
            }
            return marks.GroupBy(m => new { m.Offset, m.Length }).Select(g => new InlineIssue
            {
                Offset = g.Key.Offset,
                Length = g.Key.Length,
                Severity = g.Max(m => m.Severity),
                Issues = g.SelectMany(m => m.Issues).GroupBy(i => i.Code + i.Message).Select(i => i.First()).OrderByDescending(Severity).ToList()
            }).OrderBy(m => m.Offset).ToList();
        }
    }
}
