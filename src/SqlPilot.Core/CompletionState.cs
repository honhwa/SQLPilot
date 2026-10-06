// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
namespace SqlPilot.Core
{
    public sealed class CompletionState
    {
        public bool Dismissed
        {
            get; private set;
        }
        public void Escape() => Dismissed = true;
        public void Edit(string inserted)
        {
            if (!string.IsNullOrWhiteSpace(inserted))
                Dismissed = false;
        }
        public void Explicit() => Dismissed = false;
        public bool Allow(string sql, int caret, bool explicitRequest)
        {
            if (explicitRequest)
            {
                Explicit();
                return true;
            }
            if (Dismissed)
                return false;
            // Empty lines are for editing/spacing; explicit completion still works there.
            caret = Math.Max(0, Math.Min(sql.Length, caret));
            int start = caret;
            while (start > 0 && sql[start - 1] != '\n')
                start--;
            return !string.IsNullOrWhiteSpace(sql.Substring(start, caret - start));
        }
    }
}
