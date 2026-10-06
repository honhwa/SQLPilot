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
    public sealed class ProcedureArgument
    {
        public string Name
        {
            get; set;
        }
        public string Type
        {
            get; set;
        }
        public bool Output
        {
            get; set;
        }
        public bool ReadOnly
        {
            get; set;
        }
        public bool Optional
        {
            get; set;
        }
    }
    public sealed class CompletionPlaceholder
    {
        public int Offset
        {
            get; set;
        }
        public int Length
        {
            get; set;
        }
    }
    public static partial class Engine
    {
        sealed class OptionalParameters : TSqlFragmentVisitor
        {
            internal readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public override void ExplicitVisit(ProcedureParameter node)
            {
                if (node.Value != null)
                    Names.Add(node.VariableName.Value);
            }
        }
        public static HashSet<string> ProcedureDefaults(string definition)
        {
            var visitor = new OptionalParameters();
            if (!string.IsNullOrWhiteSpace(definition))
            {
                var tree = new TSql180Parser(true).Parse(new StringReader(definition), out var errors);
                if (errors.Count == 0)
                    tree.Accept(visitor);
            }
            return visitor.Names;
        }
        static void ProcedureCall(Candidate item, DbObject procedure, bool hasExec, bool qualified)
        {
            var text = new StringBuilder(hasExec ? item.Insert : "EXEC " + item.Insert);
            var required = procedure.Parameters.Where(p => !p.Optional).ToList();
            foreach (var parameter in required)
            {
                text.Append(text.Length == (hasExec ? item.Insert.Length : item.Insert.Length + 5) ? "\n    " : ",\n    ");
                text.Append(parameter.Name).Append(" = ");
                // Values remain editable placeholders. OUTPUT and table-valued parameters require variables.
                string value = parameter.Output || parameter.ReadOnly ? parameter.Name : "NULL";
                item.Placeholders.Add(new CompletionPlaceholder { Offset = text.Length, Length = value.Length });
                text.Append(value);
                if (parameter.Output)
                    text.Append(" OUTPUT");
                text.Append(" /* ").Append((parameter.Type ?? "value").Replace("*/", "* /"));
                if (parameter.ReadOnly)
                    text.Append("; table variable");
                else if (parameter.Output)
                    text.Append("; output variable");
                text.Append(" */");
            }
            item.Insert = text.ToString();
            item.Detail = procedure.Schema + " · " + required.Count + " required";
        }
    }
}
