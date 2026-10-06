using System;
using System.Collections.Generic;
using System.Linq;
using SqlPilot.Ssms;
static class ExplorerChecks
{
    public enum Scope
    {
        Any
    }
    public class NodeContext
    {
        public string ContextUrn; public string Context => ContextUrn; public string Name
        {
            get; set;
        }
        public string InvariantName => Name; public string Hint; public object this[string n] => Hint;
    }
    public class Item
    {
        public NodeContext Context
        {
            get; set;
        }
        public List<Item> Items = new List<Item>(); public int Calls; public List<Item> GetChildren(Scope scope)
        {
            Calls++;
            return Items;
        }
    }
    static Item I(string urn, string name, string hint = null) => new Item { Context = new NodeContext { ContextUrn = urn, Name = name, Hint = hint } };
    public class Hierarchy
    {
        public TreeNode Root
        {
            get; set;
        }
    }
    public class TreeNode
    {
        public Item ContainedItem
        {
            get; set;
        }
    }
    public class Tree
    {
        public Dictionary<string, Hierarchy> Hierarchies
        {
            get; set;
        }
    }
    public class Service
    {
        internal Tree Tree
        {
            get; set;
        }
    }
    public class ProxyBase
    {
        protected Service owner; public ProxyBase(Service service)
        {
            owner = service;
        }
    }
    public class Proxy : ProxyBase
    {
        public Proxy(Service service) : base(service) { }
    }
    internal static void Run(Action<bool, string> check)
    {
        string server = "Server[@Name='example']", db = server + "/Database[@Name='app']";
        var root = I(server, "example");
        var databases = I(server, "Databases", "Database");
        var security = I(server, "Security", "Login");
        root.Items.AddRange(new[] { databases, security });
        var database = I(db, "app");
        databases.Items.Add(database);
        var tables = I(db, "Tables", "Table");
        database.Items.Add(tables);
        var wrong = I(db + "/Table[@Schema='other' and @Name='Orders']", "Orders");
        var wanted = I(db + "/Table[@Schema='dbo' and @Name='Orders']", "Orders");
        tables.Items.AddRange(new[] { wrong, wanted });
        check(ReferenceEquals(ExplorerLookup.Find(root, "Database", "app"), database), "navigation loads only target database folder");
        check(ReferenceEquals(ExplorerLookup.Find(database, "Table", "Orders", "dbo"), wanted), "navigation distinguishes schema and accepts native URN predicate order");
        check(security.Calls == 0, "navigation does not expand unrelated server security folders");
        check(ExplorerLookup.Find(database, "Table", "Missing", "dbo") == null, "missing object has no incorrect fallback selection");
        check(ExplorerLookup.Attribute("Table[@Schema='dbo' and @Name='Order''s']", "Name") == "Order's", "navigation decodes escaped object URN names");
        var service = new Service { Tree = new Tree { Hierarchies = new Dictionary<string, Hierarchy> { { "one", new Hierarchy { Root = new TreeNode { ContainedItem = root } } } } } };
        check(ReferenceEquals(ExplorerLookup.Roots(service).Single(), root), "navigation reads existing host hierarchy roots");
        check(ReferenceEquals(ExplorerLookup.Roots(new Proxy(service)).Single(), root), "navigation unwraps the SSMS automation service proxy");
        check(ExplorerLookup.ServerMatches(root, "EXAMPLE") && !ExplorerLookup.ServerMatches(root, "other"), "navigation selects matching connected server root only");
    }
}
