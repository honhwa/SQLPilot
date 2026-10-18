// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlPilot.Core
{
    public static partial class Engine
    {
        public static TextExpansion StatementAtCaret(string sql, int caret, out string reason)
        {
            reason = "Place the cursor inside a SQL statement.";
            if (sql == null || caret < 0 || caret > sql.Length)
                return null;
            var parser = new TSql180Parser(true);
            var script = parser.Parse(new StringReader(sql), out var errors) as TSqlScript;
            if (script == null)
                return null;
            // An outer statement is the execution unit: do not bypass IF guards,
            // detach CTEs or execute a procedure body instead of its definition.
            var statements = script.Batches.SelectMany(b => b.Statements).ToList();
            var statement = statements.FirstOrDefault(s => caret >= s.StartOffset && caret < s.StartOffset + s.FragmentLength)
                ?? statements.LastOrDefault(s => caret == s.StartOffset + s.FragmentLength);
            if (statement == null || statement.FragmentLength <= 0 || statement.StartOffset + statement.FragmentLength > sql.Length)
                return null;
            string text = sql.Substring(statement.StartOffset, statement.FragmentLength);
            var isolated = parser.Parse(new StringReader(text), out var localErrors) as TSqlScript;
            if (errors.Any(e => e.Offset >= statement.StartOffset && e.Offset <= statement.StartOffset + statement.FragmentLength) ||
                localErrors.Count != 0 || isolated == null || isolated.Batches.Count != 1 || isolated.Batches[0].Statements.Count != 1)
            {
                reason = "The statement has syntax errors. Fix them before executing it.";
                return null;
            }
            reason = null;
            return new TextExpansion { Start = statement.StartOffset, Length = statement.FragmentLength, Text = text };
        }
    }
}
