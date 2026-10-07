// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SqlPilot
{
    internal static class BoundedLog
    {
        internal static void Append(string path, string text, int limit = 2 * 1024 * 1024)
        {
            if (limit < 16)
                throw new ArgumentOutOfRangeException(nameof(limit));
            text = text.Substring(0, Math.Min(text.Length, Math.Min(8192, limit / 4)));
            string key;
            using (var sha = SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "");
            using (var gate = new Mutex(false, "Local\\SqlPilot.Log." + key))
            {
                bool acquired = false;
                try
                {
                    try
                    {
                        acquired = gate.WaitOne(0);
                    }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired)
                        return;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    long size = File.Exists(path) ? new FileInfo(path).Length : 0;
                    if (size + Encoding.UTF8.GetByteCount(text) > limit)
                    {
                        File.Delete(path + ".1");
                        if (size <= limit)
                            File.Move(path, path + ".1");
                        else
                            File.Delete(path);
                    }
                    File.AppendAllText(path, text, new UTF8Encoding(false));
                }
                finally { if (acquired) gate.ReleaseMutex(); }
            }
        }
    }
}
