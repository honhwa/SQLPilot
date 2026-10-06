// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
namespace SqlPilot.Core
{
    public static partial class Engine
    {
        public static string OptionalBrackets(string sql, bool brackets)
        {
            if (brackets)
                return sql;
            var parser = new TSql180Parser(true);
            var tokens = parser.GetTokenStream(new StringReader(sql), out var ignored);
            foreach (var token in tokens.Reverse())
            {
                if (token.TokenType != TSqlTokenType.QuotedIdentifier || !token.Text.StartsWith("["))
                    continue;
                string name = token.Text.Substring(1, token.Text.Length - 2).Replace("]]", "]");
                var bare = parser.GetTokenStream(new StringReader(name), out var errors).Where(t => t.TokenType != TSqlTokenType.EndOfFile).ToList();
                if (errors.Count != 0 || bare.Count != 1 || bare[0].TokenType != TSqlTokenType.Identifier || bare[0].Text != name)
                    continue;
                sql = sql.Remove(token.Offset, token.Text.Length).Insert(token.Offset, name);
            }
            return sql;
        }
        static Candidate IdentifierStyle(Candidate candidate, bool brackets)
        {
            if (brackets)
                return candidate;
            foreach (var stop in candidate.Placeholders)
                stop.Offset = OptionalBrackets(candidate.Insert.Substring(0, stop.Offset), false).Length;
            candidate.Insert = OptionalBrackets(candidate.Insert, false);
            return candidate;
        }
    }
}
