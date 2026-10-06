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
        public static TextExpansion DiagnosticFix(string sql, Issue issue, IEnumerable<DbObject> catalog, bool useBrackets = true)
        {
            if (sql == null || issue == null || issue.Offset < 0 || issue.Length < 0 || issue.Offset + issue.Length > sql.Length)
                return null;
            // Never trust a finding from a previous buffer revision or an unrelated query.
            var current = Analyze(sql).FirstOrDefault(i => i.Code == issue.Code && i.Offset == issue.Offset && i.Length == issue.Length);
            if (current == null)
                return null;
            issue = current;
            string newline = sql.Contains("\r\n") ? "\r\n" : "\n";
            if (CanConfigureDiagnosticFix(issue))
            {
                string insertion = newline + (issue.Code == "TOP-ORDER" ? "ORDER BY " : "WHERE ") + newline;
                return new TextExpansion { Start = issue.Offset + issue.Length, Length = 0, Text = insertion, CaretOffset = insertion.Length - newline.Length };
            }
            if (issue.Code == "VARIABLE-UNDECLARED")
            {
                var header = ContextTokens(sql.Substring(issue.SectionStart, issue.Offset - issue.SectionStart));
                if (header.Any(t => t.TokenType == TSqlTokenType.Procedure || t.TokenType == TSqlTokenType.Function))
                    return null;
                string name = sql.Substring(issue.Offset, issue.Length);
                string declaration = newline + "DECLARE " + name + " ;" + newline;
                return new TextExpansion { Start = issue.SectionStart, Length = 0, Text = declaration, CaretOffset = declaration.IndexOf(';') };
            }
            var parser = new TSql180Parser(true);
            var tokens = parser.GetTokenStream(new StringReader(sql.Substring(issue.Offset, issue.Length)), out var errors);
            // A generated expression replacement must not silently discard comments.
            if (tokens.Any(t => t.TokenType == TSqlTokenType.SingleLineComment || t.TokenType == TSqlTokenType.MultilineComment))
                return null;
            if (issue.Code == "SELECT-STAR")
                return ExpandStar(sql, issue.Offset + issue.Length - 1, catalog ?? Enumerable.Empty<DbObject>(), useBrackets);
            if (issue.Replacement == null)
                return null;
            return new TextExpansion { Start = issue.Offset, Length = issue.Length, Text = issue.Replacement };
        }
        public static bool CanConfigureDiagnosticFix(Issue issue) => issue != null && (issue.Code == "UPDATE-WHERE" || issue.Code == "DELETE-WHERE" || issue.Code == "TOP-ORDER");
        public static TextExpansion ConfigureDiagnosticFix(string sql, Issue issue, string expression)
        {
            if (sql == null || !CanConfigureDiagnosticFix(issue) || string.IsNullOrWhiteSpace(expression))
                return null;
            var current = Analyze(sql).FirstOrDefault(i => i.Code == issue.Code && i.Offset == issue.Offset && i.Length == issue.Length);
            if (current == null)
                return null;
            bool order = issue.Code == "TOP-ORDER";
            string clause = order ? "ORDER BY " : "WHERE ";
            expression = expression.Trim();
            var parser = new TSql180Parser(true);
            var probe = parser.Parse(new StringReader("SELECT 1 " + clause + expression + ";"), out var errors) as TSqlScript;
            if (errors.Count != 0 || probe == null || probe.Batches.Count != 1 || probe.Batches[0].Statements.Count != 1)
                return null;
            var query = (probe.Batches[0].Statements[0] as SelectStatement)?.QueryExpression as QuerySpecification;
            if (query == null || query.FromClause != null || query.GroupByClause != null || query.HavingClause != null ||
                (order ? query.OrderByClause == null || query.WhereClause != null : query.WhereClause == null || query.OrderByClause != null))
                return null;
            string newline = sql.Contains("\r\n") ? "\r\n" : "\n";
            var fix = new TextExpansion { Start = current.Offset + current.Length, Length = 0, Text = newline + clause + expression + newline };
            var candidate = sql.Insert(fix.Start, fix.Text);
            var findings = Analyze(candidate);
            if (findings.Any(i => i.Code == "SYNTAX" || i.Code == issue.Code && i.Offset == issue.Offset))
                return null;
            return fix;
        }
        public static string DiagnosticFixHint(Issue issue)
        {
            switch (issue.Code)
            {
                case "SELECT-STAR":
                    return "Load metadata for all referenced tables to expand the columns. Unresolved tables and derived sources require manual review.";
                case "UPDATE-WHERE":
                case "DELETE-WHERE":
                    return "Choose the intended rows and add a WHERE condition. SqlPilot cannot infer a safe condition.";
                case "TOP-ORDER":
                    return "Apply fix inserts ORDER BY and places the caret there. Choose the sorting columns in the SQL editor.";
                case "VARIABLE-UNDECLARED":
                    return "Declare the variable in the current batch or procedure and choose its SQL data type. Batch variables are not visible after GO.";
                case "NULL-COMPARE":
                    return "Review the comparison manually if it contains comments; comments will not be discarded by an automatic fix.";
                default:
                    return "Review this section in the editor; an automatic correction is not available.";
            }
        }
    }
}
