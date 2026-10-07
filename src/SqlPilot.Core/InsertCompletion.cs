// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Linq;
using System.Text;
namespace SqlPilot.Core
{
    public static partial class Engine
    {
        static void InsertBody(Candidate item, DbObject table, string following)
        {
            // Preserve existing continuations and comments without guessing intent.
            string tail = following.Trim();
            if ((tail.Length > 0 && tail != ";") || table.InsertColumns == null || !Relation(table))
                return;
            var columns = table.InsertColumns;
            if (columns.Count == 0)
            {
                item.Insert += "\nDEFAULT VALUES" + (tail == ";" ? "" : ";");
                return;
            }
            var text = new StringBuilder(item.Insert);
            text.Append("\n(\n    ").Append(string.Join(",\n    ", columns.Select(DbObject.Quote))).Append("\n)\nVALUES\n(\n");
            for (int i = 0; i < columns.Count; i++)
            {
                text.Append("    ");
                item.Placeholders.Add(new CompletionPlaceholder { Offset = text.Length, Length = 4 });
                text.Append("NULL").Append(i + 1 < columns.Count ? "," : "").Append(" /* ").Append(columns[i].Replace("/*", "/ *").Replace("*/", "* /").Replace("\r", " ").Replace("\n", " ")).Append(" */\n");
            }
            text.Append(")").Append(tail == ";" ? "" : ";");
            item.Insert = text.ToString();
        }
    }
}
