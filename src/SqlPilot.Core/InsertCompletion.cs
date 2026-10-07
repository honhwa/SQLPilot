// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Linq;
using System.Text;
using System.Globalization;
namespace SqlPilot.Core
{
    public sealed class InsertColumn
    {
        public string Name
        {
            get; set;
        }
        public string Type
        {
            get; set;
        }
        public string BaseType
        {
            get; set;
        }
        public bool Nullable { get; set; } = true;
        public bool HasDefault
        {
            get; set;
        }
        public string DefaultExpression
        {
            get; set;
        }
    }
    public static partial class Engine
    {
        public static string ColumnType(string name, string schema, bool userDefined, int length, int precision, int scale)
        {
            if (userDefined)
                return DbObject.Quote(schema) + "." + DbObject.Quote(name);
            string number(int value) => value.ToString(CultureInfo.InvariantCulture);
            switch (name.ToLowerInvariant())
            {
                case "varchar":
                case "nvarchar":
                case "char":
                case "nchar":
                case "binary":
                case "varbinary":
                    return name + "(" + (length == -1 ? "max" : number(name.StartsWith("n", StringComparison.OrdinalIgnoreCase) ? length / 2 : length)) + ")";
                case "numeric":
                case "decimal":
                    return name + "(" + number(precision) + "," + number(scale) + ")";
                case "datetime2":
                case "datetimeoffset":
                case "time":
                    return name + "(" + number(scale) + ")";
                case "float":
                    return name + "(" + number(precision) + ")";
                default:
                    return name;
            }
        }
        static string InsertValue(InsertColumn column)
        {
            // DEFAULT invokes the original constraint when its definition is not visible,
            // or for legacy bound defaults. Never substitute an invented literal for it.
            if (column.HasDefault || !string.IsNullOrWhiteSpace(column.DefaultExpression))
                return string.IsNullOrWhiteSpace(column.DefaultExpression) ? "DEFAULT" : column.DefaultExpression.Trim();
            if (column.Nullable)
                return "NULL";
            switch ((column.BaseType ?? "").ToLowerInvariant())
            {
                case "bit":
                case "tinyint":
                case "smallint":
                case "int":
                case "bigint":
                case "decimal":
                case "numeric":
                case "float":
                case "real":
                case "money":
                case "smallmoney":
                case "sql_variant":
                    return "0";
                case "char":
                case "varchar":
                case "text":
                    return "''";
                case "nchar":
                case "nvarchar":
                case "ntext":
                    return "N''";
                case "binary":
                case "varbinary":
                case "image":
                    return "0x";
                case "date":
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                    return "'19000101'";
                case "time":
                    return "'00:00:00'";
                case "datetimeoffset":
                    return "'1900-01-01T00:00:00+00:00'";
                case "uniqueidentifier":
                    return "'00000000-0000-0000-0000-000000000000'";
                case "xml":
                    return "N'<root />'";
                case "geography":
                    return "geography::Point(0, 0, 4326)";
                case "geometry":
                    return "geometry::Point(0, 0, 0)";
                case "hierarchyid":
                    return "hierarchyid::GetRoot()";
                default:
                    // Arbitrary CLR types have no universal non-null value. An explicit
                    // editable variable is safer than silently emitting NULL or DEFAULT.
                    return "@required_value";
            }
        }
        static string InsertComment(string value) => (value ?? "").Replace("/*", "/ *").Replace("*/", "* /").Replace("\r", " ").Replace("\n", " ");
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
            text.Append("\n(\n    ").Append(string.Join(",\n    ", columns.Select(c => DbObject.Quote(c.Name)))).Append("\n)\nVALUES\n(\n");
            for (int i = 0; i < columns.Count; i++)
            {
                text.Append("    ");
                string value = InsertValue(columns[i]);
                item.Placeholders.Add(new CompletionPlaceholder { Offset = text.Length, Length = value.Length });
                text.Append(value).Append(i + 1 < columns.Count ? "," : "").Append(" /* ").Append(InsertComment(columns[i].Name)).Append(" · ").Append(InsertComment(columns[i].Type)).Append(columns[i].Nullable ? " NULL" : " NOT NULL");
                if (value == "@required_value")
                    text.Append("; enter a required value");
                text.Append(" */\n");
            }
            text.Append(")").Append(tail == ";" ? "" : ";");
            item.Insert = text.ToString();
        }
    }
}
