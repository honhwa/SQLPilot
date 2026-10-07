// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    internal static class Diagnostics
    {
        public static void Write(string message)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot");
                Directory.CreateDirectory(folder);
                BoundedLog.Append(Path.Combine(folder, "extension.log"), DateTime.UtcNow.ToString("O") + " [PID " + System.Diagnostics.Process.GetCurrentProcess().Id + "] " + message + Environment.NewLine);
            }
            catch { }
        }
    }
    internal static class ActiveConnection
    {
        // All SSMS reflection lives here: replace this adapter when the private SSMS API changes.
        public static string TryGet()
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SqlPackageBase");
                var cache = assembly?.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
                var factory = cache?.GetProperty("ScriptFactory", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                var active = Get(factory, "CurrentlyActiveWndConnectionInfo");
                if (active == null || !Convert.ToBoolean(Get(active, "Live") ?? false))
                    return null;
                var info = Get(active, "UIConnectionInfo");
                if (info == null || Convert.ToBoolean(Get(active, "IsMultipleConnection") ?? false))
                    return null;
                var options = Get(info, "AdvancedOptions") as NameValueCollection;
                string database = options?["DATABASE"] ?? options?["DATABASE_NAME"];
                if (string.IsNullOrWhiteSpace(database))
                    return null; // Never silently scan master instead of the selected database.
                int auth = Convert.ToInt32(Get(info, "AuthenticationType"));
                if (auth != 0 && auth != 1)
                    return null; // Token/Entra identities need a separate adapter.
                var b = new SqlConnectionStringBuilder
                {
                    DataSource = Convert.ToString(Get(info, "ServerName")),
                    InitialCatalog = database,
                    IntegratedSecurity = auth == 0,
                    ConnectTimeout = 8,
                    ApplicationName = "SqlPilot metadata",
                    Encrypt = true
                };
                string encryptOption = options?["ENCRYPT_CONNECTION"] ?? options?["Encrypt"] ?? options?["ENCRYPT"] ?? Convert.ToString(Get(info, "EncryptConnection"));
                if (bool.TryParse(encryptOption, out var encrypt))
                    b.Encrypt = encrypt;
                else if (string.Equals(encryptOption, "Optional", StringComparison.OrdinalIgnoreCase))
                    b.Encrypt = false;
                string trustOption = options?["TRUST_SERVER_CERTIFICATE"] ?? options?["TrustServerCertificate"] ?? Convert.ToString(Get(info, "TrustServerCertificate"));
                if (bool.TryParse(trustOption, out var trust))
                    b.TrustServerCertificate = trust;
                ApplyLocalDbPolicy(b);
                if (auth == 1)
                {
                    b.UserID = Convert.ToString(Get(info, "UserName"));
                    b.Password = Convert.ToString(Get(info, "Password"));
                }
                return b.ConnectionString;
            }
            catch { return null; }
        }
        static object Get(object obj, string property) => obj?.GetType().GetProperty(property)?.GetValue(obj);
        internal static void ApplyLocalDbPolicy(SqlConnectionStringBuilder b)
        {
            // Framework SqlClient cannot request TLS from this LocalDB transport (SQL error 20).
            // Limit this exception to LocalDB's local pipe; remote SQL endpoints keep their policy.
            if (b.DataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase) || b.DataSource.StartsWith(@"np:\\.\pipe\LOCALDB#", StringComparison.OrdinalIgnoreCase) || b.DataSource.StartsWith(@"\\.\pipe\LOCALDB#", StringComparison.OrdinalIgnoreCase))
            {
                b.Encrypt = false;
                b.TrustServerCertificate = false;
            }
        }
    }
    internal static class SchemaReader
    {
        internal static async Task<string> ModifyScript(string connectionString, DbObject target, CancellationToken token)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (var command = connection.CreateCommand())
                {
                    command.CommandTimeout = 15;
                    command.CommandText = "SELECT m.definition,m.uses_ansi_nulls,m.uses_quoted_identifier FROM sys.sql_modules m JOIN sys.objects o ON o.object_id=m.object_id JOIN sys.schemas s ON s.schema_id=o.schema_id WHERE s.name=@schema AND o.name=@name";
                    command.Parameters.Add("@schema", System.Data.SqlDbType.NVarChar, 128).Value = target.Schema;
                    command.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 128).Value = target.Name;
                    using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.IsDBNull(0))
                            throw new InvalidOperationException("Definition unavailable. Check VIEW DEFINITION permission or whether the module is encrypted.");
                        return "USE " + DbObject.Quote(connection.Database) + ";\r\nGO\r\nSET ANSI_NULLS " + (reader.GetBoolean(1) ? "ON" : "OFF") + ";\r\nGO\r\nSET QUOTED_IDENTIFIER " + (reader.GetBoolean(2) ? "ON" : "OFF") + ";\r\nGO\r\n" + Engine.ModifyModule(reader.GetString(0), target.Schema, target.Name) + "\r\nGO\r\n";
                    }
                }
            }
        }
        public static async Task<List<DbObject>> Scan(string connectionString, CancellationToken token)
        {
            var objects = new Dictionary<int, DbObject>();
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (var command = connection.CreateCommand())
                {
                    command.CommandTimeout = 15;
                    command.CommandText = @"SELECT o.object_id, s.name, o.name, o.type_desc, c.name
FROM sys.objects AS o
INNER JOIN sys.schemas AS s ON s.schema_id=o.schema_id
LEFT JOIN sys.columns AS c ON c.object_id=o.object_id
WHERE o.is_ms_shipped=0 AND o.type IN ('U','V','P','PC','FN','IF','TF','SN')
ORDER BY o.object_id, c.column_id;";
                    using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            int id = reader.GetInt32(0);
                            if (!objects.TryGetValue(id, out var obj))
                                objects[id] = obj = new DbObject { Schema = reader.GetString(1), Name = reader.GetString(2), Kind = reader.GetString(3) };
                            if (!reader.IsDBNull(4))
                                obj.Columns.Add(reader.GetString(4));
                        }
                }
                using (var command = connection.CreateCommand())
                {
                    command.CommandTimeout = 15;
                    command.CommandText = @"SELECT fk.object_id, fk.name, fk.parent_object_id, fk.referenced_object_id, pc.name, rc.name, fk.is_disabled
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS fc ON fc.constraint_object_id=fk.object_id
JOIN sys.columns AS pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id
JOIN sys.columns AS rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id
ORDER BY fk.object_id, fc.constraint_column_id;";
                    var keys = new Dictionary<int, ForeignKey>();
                    using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            if (!objects.TryGetValue(reader.GetInt32(2), out var source) || !objects.TryGetValue(reader.GetInt32(3), out var target))
                                continue;
                            int id = reader.GetInt32(0);
                            if (!keys.TryGetValue(id, out var key))
                            {
                                keys[id] = key = new ForeignKey { Name = reader.GetString(1), Source = source, Target = target, IsDisabled = reader.GetBoolean(6) };
                                source.ForeignKeys.Add(key);
                            }
                            key.Columns.Add(Tuple.Create(reader.GetString(4), reader.GetString(5)));
                        }
                }
                using (var command = connection.CreateCommand())
                {
                    command.CommandTimeout = 15;
                    command.CommandText = @"SELECT p.object_id,p.name,ts.name,t.name,p.max_length,p.precision,p.scale,p.is_output,p.is_readonly,p.has_default_value,t.is_user_defined,m.definition
FROM sys.parameters p JOIN sys.objects o ON o.object_id=p.object_id
JOIN sys.types t ON t.user_type_id=p.user_type_id JOIN sys.schemas ts ON ts.schema_id=t.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id=p.object_id
WHERE o.type IN ('P','PC') AND p.parameter_id>0 ORDER BY p.object_id,p.parameter_id;";
                    var defaults = new Dictionary<int, HashSet<string>>();
                    using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            int id = reader.GetInt32(0);
                            if (!objects.TryGetValue(id, out var procedure))
                                continue;
                            if (!defaults.TryGetValue(id, out var optional))
                                defaults[id] = optional = Engine.ProcedureDefaults(reader.IsDBNull(11) ? null : reader.GetString(11));
                            string type = reader.GetString(3);
                            bool userType = reader.GetBoolean(10);
                            if (userType)
                                type = DbObject.Quote(reader.GetString(2)) + "." + DbObject.Quote(type);
                            else if (new[] { "varchar", "nvarchar", "char", "nchar", "varbinary", "binary" }.Contains(type))
                            {
                                int length = reader.GetInt16(4);
                                type += "(" + (length == -1 ? "max" : (type.StartsWith("n") ? length / 2 : length).ToString()) + ")";
                            }
                            else if (type == "decimal" || type == "numeric")
                                type += "(" + reader.GetByte(5) + "," + reader.GetByte(6) + ")";
                            else if (type == "datetime2" || type == "datetimeoffset" || type == "time")
                                type += "(" + reader.GetByte(6) + ")";
                            string name = reader.GetString(1);
                            procedure.Parameters.Add(new ProcedureArgument { Name = name, Type = type, Output = reader.GetBoolean(7), ReadOnly = reader.GetBoolean(8), Optional = reader.GetBoolean(9) || optional.Contains(name) });
                        }
                }
            }
            return objects.Values.ToList();
        }
    }
    internal sealed class HistoryEntry
    {
        public string File
        {
            get; set;
        }
        public DateTime Time
        {
            get; set;
        }
        public string Sql
        {
            get; set;
        }
        public override string ToString() => Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " · " + File;
    }
    internal static class History
    {
        static readonly object Gate = new object();
        static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "History");
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SqlPilot-history-v1");
        public static void Save(string id, string file, string sql)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, id + ".bin");
                var entries = ReadFile(path);
                if (entries.LastOrDefault()?.Sql == sql)
                    return;
                entries.Add(new HistoryEntry { File = file, Sql = sql, Time = DateTime.UtcNow });
                var xml = new XElement("history", entries.Skip(Math.Max(0, entries.Count - 50)).Select(e => new XElement("entry", new XAttribute("file", e.File), new XAttribute("time", e.Time.ToString("O")), new XElement("sql", e.Sql))));
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(xml.ToString()), Entropy, DataProtectionScope.CurrentUser);
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, data);
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
                foreach (var old in Directory.GetFiles(Folder, "*.bin").Select(p => new FileInfo(p)).Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30)))
                    old.Delete();
            }
        }
        static List<HistoryEntry> ReadFile(string path)
        {
            try
            {
                return XElement.Parse(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser)))
                    .Elements("entry").Select(e => new HistoryEntry { File = (string)e.Attribute("file"), Time = DateTime.Parse((string)e.Attribute("time"), null, System.Globalization.DateTimeStyles.RoundtripKind), Sql = (string)e.Element("sql") }).ToList();
            }
            catch { return new List<HistoryEntry>(); }
        }
        public static List<HistoryEntry> Load()
        {
            lock (Gate)
                return Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.bin").SelectMany(ReadFile).OrderByDescending(e => e.Time).Take(1000).ToList() : new List<HistoryEntry>();
        }
    }
}
