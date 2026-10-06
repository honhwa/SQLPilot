using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SqlPilot.Ssms;
class WindowsSmoke
{
    static async Task Main(string[] args)
    {
        if (args.Length != 1)
            throw new Exception("Pass the name of a dedicated, newly created LocalDB test instance.");
        string server = "(localdb)\\" + args[0];
        string database = "SqlPilotSmoke_" + Guid.NewGuid().ToString("N");
        var b = new SqlConnectionStringBuilder { DataSource = server, InitialCatalog = "master", IntegratedSecurity = true, ConnectTimeout = 10, Encrypt = true, TrustServerCertificate = true };
        ActiveConnection.ApplyLocalDbPolicy(b);
        if (b.Encrypt || b.TrustServerCertificate)
            throw new Exception("LocalDB transport policy failed");
        var remote = new SqlConnectionStringBuilder { DataSource = "remote.invalid", Encrypt = true, TrustServerCertificate = false };
        ActiveConnection.ApplyLocalDbPolicy(remote);
        if (!remote.Encrypt || remote.TrustServerCertificate)
            throw new Exception("Remote connection policy changed");
        Console.WriteLine("PASS LocalDB transport policy; remote TLS preserved");
        using (var connection = new SqlConnection(b.ConnectionString))
        {
            await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE DATABASE [" + database + "]";
                await command.ExecuteNonQueryAsync();
            }
            try
            {
                b.InitialCatalog = database;
                using (var db = new SqlConnection(b.ConnectionString))
                {
                    await db.OpenAsync();
                    using (var command = db.CreateCommand())
                    {
                        command.CommandText = "CREATE TABLE dbo.Customers (Id int NOT NULL, TenantId int NOT NULL, PRIMARY KEY (Id,TenantId)); CREATE TABLE dbo.Orders (Id int NOT NULL PRIMARY KEY, CustomerId int, TenantId int, CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId,TenantId) REFERENCES dbo.Customers(Id,TenantId));";
                        await command.ExecuteNonQueryAsync();
                    }
                }
                var catalog = await SchemaReader.Scan(b.ConnectionString, CancellationToken.None);
                var orders = catalog.Single(o => o.Name == "Orders");
                if (catalog.Count != 2 || orders.Schema != "dbo" || !orders.Columns.SequenceEqual(new[] { "Id", "CustomerId", "TenantId" }))
                    throw new Exception("Metadata scan failed");
                Console.WriteLine("PASS actual ADO.NET metadata scan / table and column mapping");
                var key = orders.ForeignKeys.Single();
                if (key.Name != "FK_Orders_Customers" || key.Target.Name != "Customers" || key.Columns.Count != 2 || key.Columns[0].Item1 != "CustomerId" || key.Columns[1].Item2 != "TenantId")
                    throw new Exception("Foreign key scan failed");
                string query = "SELECT * FROM dbo.Orders o JOIN dbo.Customers c ON ";
                if (SqlPilot.Core.Engine.Complete(query, query.Length, catalog).First().Insert != "[o].[CustomerId] = [c].[Id] AND [o].[TenantId] = [c].[TenantId]")
                    throw new Exception("Actual metadata ON completion failed");
                Console.WriteLine("PASS actual composite FK metadata and ON completion");
                using (var db = new SqlConnection(b.ConnectionString))
                {
                    await db.OpenAsync();
                    using (var command = db.CreateCommand())
                    {
                        command.CommandText = "CREATE PROCEDURE dbo.SqlPilotNavTest @Id int, @Note nvarchar(50) = NULL, @Total decimal(10,2) OUTPUT AS SELECT @Id AS Id;";
                        await command.ExecuteNonQueryAsync();
                    }
                }
                string modify = await SchemaReader.ModifyScript(b.ConnectionString, new SqlPilot.Core.DbObject { Schema = "dbo", Name = "SqlPilotNavTest", Kind = "SQL_STORED_PROCEDURE" }, CancellationToken.None);
                if (!modify.Contains("ALTER PROCEDURE [dbo].[SqlPilotNavTest] @Id int, @Note nvarchar(50) = NULL, @Total decimal(10,2) OUTPUT AS SELECT @Id AS Id;") || !modify.StartsWith("USE [" + database + "];") || !modify.Contains("SET QUOTED_IDENTIFIER"))
                    throw new Exception("Modify script metadata failed");
                Console.WriteLine("PASS actual procedure metadata to ALTER script with database and session settings");
                var updatedCatalog = await SchemaReader.Scan(b.ConnectionString, CancellationToken.None);
                var procedure = updatedCatalog.Single(o => o.Name == "SqlPilotNavTest");
                if (procedure.Parameters.Count != 3 || procedure.Parameters[1].Type != "nvarchar(50)" || !procedure.Parameters[1].Optional || !procedure.Parameters[2].Output || procedure.Parameters[2].Type != "decimal(10,2)")
                    throw new Exception("Procedure parameter metadata failed");
                string exec = "EXEC SqlPilotN";
                var call = SqlPilot.Core.Engine.Complete(exec, exec.Length, updatedCatalog).Single(c => c.Category == "Procedure");
                if (call.Placeholders.Count != 2 || call.Insert.Contains("@Note") || !call.Insert.Contains("@Id = NULL") || !call.Insert.Contains("@Total = @Total OUTPUT"))
                    throw new Exception("Procedure parameter expansion failed");
                Console.WriteLine("PASS actual procedure parameter order types optional defaults and OUTPUT call expansion");
                b.InitialCatalog = "master";
                var empty = await SchemaReader.Scan(b.ConnectionString, CancellationToken.None);
                if (empty.Any(o => o.Name == "Orders"))
                    throw new Exception("Database isolation failed");
                Console.WriteLine("PASS database isolation");
            }
            finally { using (var command = connection.CreateCommand()) { command.CommandText = "ALTER DATABASE [" + database + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + database + "];"; SqlConnection.ClearAllPools(); await command.ExecuteNonQueryAsync(); } }
        }
        string id = Guid.NewGuid().ToString("N");
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "History", id + ".bin");
        try
        {
            History.Save(id, "smoke.sql", "SELECT 'HISTORY_SMOKE_MARKER';");
            if (Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("HISTORY_SMOKE_MARKER"))
                throw new Exception("Plaintext history");
            if (!History.Load().Any(e => e.Sql == "SELECT 'HISTORY_SMOKE_MARKER';"))
                throw new Exception("History decrypt failed");
            Console.WriteLine("PASS DPAPI encrypted history roundtrip");
            History.Save(id, "smoke.sql", "SELECT 'HISTORY_SMOKE_MARKER';");
            if (History.Load().Count(e => e.Sql == "SELECT 'HISTORY_SMOKE_MARKER';") != 1)
                throw new Exception("Duplicate snapshot");
            for (int i = 0; i < 55; i++)
                History.Save(id, "smoke.sql", "SELECT " + i + " AS HISTORY_RETENTION_SMOKE_MARKER;");
            if (History.Load().Count(e => e.Sql.Contains("HISTORY_RETENTION_SMOKE_MARKER")) != 50)
                throw new Exception("Retention limit");
            Console.WriteLine("PASS duplicate suppression, atomic replacement and 50-version retention");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
