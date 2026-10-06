// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    internal static class ReferenceNavigation
    {
        internal static void Explorer(string connectionString, DbObject target)
        {
            var connection = new System.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SqlWorkbench.Interfaces") ?? Assembly.Load("SqlWorkbench.Interfaces");
            var contract = assembly.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.IObjectExplorerService", true);
            object service = ServiceProvider.GlobalProvider.GetService(contract);
            if (service == null)
            {
                var cacheAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SqlPackageBase");
                var provider = cacheAssembly?.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache")?.GetProperty("ServiceProvider", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IServiceProvider;
                service = provider?.GetService(contract);
            }
            if (service == null)
                throw new InvalidOperationException("Object Explorer navigation is unavailable in this SSMS host.");
            // FindNode only searches already-built contexts with exact URN string keys.
            // Use the existing connected hierarchy and materialize only the target path.
            string kind = Engine.ObjectUrn(connection.DataSource, connection.InitialCatalog, target).Split('/').Last().Split('[')[0];
            var roots = ExplorerLookup.Roots(service).Where(r => ExplorerLookup.ServerMatches(r, connection.DataSource)).ToList();
            object node = null;
            foreach (var root in roots)
            {
                var database = ExplorerLookup.Find(root, "Database", connection.InitialCatalog);
                if (database == null)
                    continue;
                var found = ExplorerLookup.Find(database, kind, target.Name, target.Schema);
                if (found != null)
                    node = ExplorerLookup.Property(found, "Context");
                if (node != null)
                    break;
            }
            if (roots.Count == 0)
                node = contract.GetMethod("FindNode").Invoke(service, new object[] { Engine.ObjectUrn(connection.DataSource, connection.InitialCatalog, target) });
            if (node == null)
                throw new InvalidOperationException(roots.Count == 0 ?
                "No matching Object Explorer server hierarchy was found. Your query connection may still be active." :
                "The object could not be located in Object Explorer. Check its schema, folder filters and metadata permissions.");
            contract.GetMethod("SynchronizeTree").Invoke(service, new[] { node });
        }
        internal static void OpenModify(string sql, DbObject target)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "Definitions");
            string path = Path.Combine(folder, "Modify-" + Guid.NewGuid().ToString("N") + ".sql");
            PersonalFiles.Write(path, sql);
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            if (dte == null)
                throw new InvalidOperationException("The SSMS SQL editor is unavailable.");
            dte.ItemOperations.OpenFile(path);
        }
    }
}
