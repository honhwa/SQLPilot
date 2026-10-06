// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.


namespace SqlPilot
{
    internal static class ProductInfo
    {
        internal static string Version => typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        internal const string Author = "Arash Ghasemi Rad";
        internal const string TelegramUrl = "https://t.me/ArashGhasemiRad";
        internal const string RepositoryUrl = "https://github.com/tyeety/SQLPilot";
        internal const string LicenseName = "SqlPilot Source-Available Use License 1.0";
        internal const string Copyright = "Copyright © 2026 Arash Ghasemi Rad. All rights reserved.";
    }
}
