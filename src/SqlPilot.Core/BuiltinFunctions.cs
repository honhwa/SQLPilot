// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace SqlPilot.Core
{
    public static partial class Engine
    {
        static readonly Dictionary<string, string> FunctionTemplates = BuildFunctions();
        static Dictionary<string, string> BuildFunctions()
        {
            var all = new Dictionary<string, string>();
            foreach (var name in "ROW_NUMBER|RANK|DENSE_RANK".Split('|'))
                all[name] = name + "() OVER (ORDER BY {column})";
            all["NTILE"] = "NTILE({buckets}) OVER (ORDER BY {column})";
            foreach (var name in "LAG|LEAD|FIRST_VALUE|LAST_VALUE".Split('|'))
                all[name] = name + "({expression}) OVER (ORDER BY {column})";
            foreach (var name in "SUM|AVG|MIN|MAX|COUNT|COUNT_BIG|STDEV|STDEVP|VAR|VARP|GROUPING|GROUPING_ID|ABS|CEILING|FLOOR|SQRT|SIGN|LOWER|UPPER|LTRIM|RTRIM|TRIM|LEN|DATALENGTH|REVERSE|ASCII|UNICODE|CHAR|NCHAR|SPACE|YEAR|MONTH|DAY|ISDATE|ISNUMERIC|DB_ID|DB_NAME|OBJECT_ID|OBJECT_NAME|OBJECT_SCHEMA_NAME|SCHEMA_ID|SCHEMA_NAME|TYPE_ID|TYPE_NAME|SUSER_ID|SUSER_SNAME|USER_ID|USER_NAME|ISJSON".Split('|'))
                all[name] = name + "({expression})";
            foreach (var name in "GETDATE|GETUTCDATE|SYSDATETIME|SYSUTCDATETIME|SYSDATETIMEOFFSET|NEWID|PI|RAND".Split('|'))
                all[name] = name + "()";
            all["CURRENT_TIMESTAMP"] = "CURRENT_TIMESTAMP";
            foreach (var name in "SYSTEM_USER|SESSION_USER|CURRENT_USER".Split('|'))
                all[name] = name;
            all["DATEADD"] = "DATEADD({datepart}, {number}, {date})";
            all["DATEDIFF"] = "DATEDIFF({datepart}, {start_date}, {end_date})";
            all["DATEDIFF_BIG"] = "DATEDIFF_BIG({datepart}, {start_date}, {end_date})";
            all["DATEPART"] = "DATEPART({datepart}, {date})";
            all["DATENAME"] = "DATENAME({datepart}, {date})";
            all["EOMONTH"] = "EOMONTH({date})";
            all["DATEFROMPARTS"] = "DATEFROMPARTS({year}, {month}, {day})";
            all["LEFT"] = "LEFT({expression}, {length})";
            all["RIGHT"] = "RIGHT({expression}, {length})";
            all["SUBSTRING"] = "SUBSTRING({expression}, {start}, {length})";
            all["REPLACE"] = "REPLACE({expression}, {pattern}, {replacement})";
            all["CHARINDEX"] = "CHARINDEX({pattern}, {expression})";
            all["PATINDEX"] = "PATINDEX({pattern}, {expression})";
            all["STUFF"] = "STUFF({expression}, {start}, {length}, {replacement})";
            all["REPLICATE"] = "REPLICATE({expression}, {count})";
            all["QUOTENAME"] = "QUOTENAME({expression})";
            all["CONCAT"] = "CONCAT({expression1}, {expression2})";
            all["CONCAT_WS"] = "CONCAT_WS({separator}, {expression1}, {expression2})";
            all["STRING_AGG"] = "STRING_AGG({expression}, {separator})";
            all["FORMAT"] = "FORMAT({expression}, {format})";
            all["ROUND"] = "ROUND({expression}, {precision})";
            all["POWER"] = "POWER({expression}, {power})";
            all["ISNULL"] = "ISNULL({expression}, {replacement})";
            all["NULLIF"] = "NULLIF({expression1}, {expression2})";
            all["COALESCE"] = "COALESCE({expression1}, {expression2})";
            all["IIF"] = "IIF({condition}, {true_value}, {false_value})";
            all["CAST"] = "CAST({expression} AS {data_type})";
            all["TRY_CAST"] = "TRY_CAST({expression} AS {data_type})";
            all["CONVERT"] = "CONVERT({data_type}, {expression})";
            all["TRY_CONVERT"] = "TRY_CONVERT({data_type}, {expression})";
            all["JSON_VALUE"] = "JSON_VALUE({expression}, {path})";
            all["JSON_QUERY"] = "JSON_QUERY({expression}, {path})";
            all["JSON_MODIFY"] = "JSON_MODIFY({expression}, {path}, {value})";
            all["HASHBYTES"] = "HASHBYTES({algorithm}, {expression})";
            return all;
        }
        static Candidate FunctionCandidate(string name, string template, string category = "Function")
        {
            var candidate = new Candidate { Label = name, Category = category, Detail = "Built-in · " + name, Priority = 1 };
            var text = new StringBuilder();
            int previous = 0;
            foreach (Match part in Regex.Matches(template, @"\{([^}]+)\}"))
            {
                text.Append(template.Substring(previous, part.Index - previous));
                candidate.Placeholders.Add(new CompletionPlaceholder { Offset = text.Length, Length = part.Groups[1].Value.Length });
                text.Append(part.Groups[1].Value);
                previous = part.Index + part.Length;
            }
            text.Append(template.Substring(previous));
            candidate.Insert = text.ToString();
            return candidate;
        }
        static void AddFunctions(List<Candidate> items, bool relations, bool allowWindow = true)
        {
            if (relations)
            {
                items.Add(FunctionCandidate("STRING_SPLIT", "STRING_SPLIT({expression}, {separator})", "Table function"));
                items.Add(FunctionCandidate("OPENJSON", "OPENJSON({expression})", "Table function"));
                return;
            }
            items.RemoveAll(c => FunctionTemplates.Keys.Any(name => c.Label == name || c.Label.StartsWith(name + "(", StringComparison.Ordinal)));
            items.AddRange(FunctionTemplates.Where(p => allowWindow || !p.Value.Contains(" OVER ")).Select(p => FunctionCandidate(p.Key, p.Value)));
        }
    }
}
