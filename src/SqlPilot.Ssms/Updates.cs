// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SqlPilot.Ssms
{
    internal static class Updates
    {
        static readonly UpdateClient client = new UpdateClient(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlPilot", "Updates"));
        static DispatcherTimer timer;
        static Task checking;
        static ReleaseInfo latest;
        static bool checkFailed;
        static Window dialog;
        internal static event Action Changed;
        internal static bool Available => latest != null && latest.IsNewerThan(ProductInfo.Version);
        internal static string MenuLabel => Available ? "Update to " + latest.Version.ToString(3) + "…" : "Check for updates…";
        internal static void Start(Dispatcher ui)
        {
            if (timer != null)
                return;
            timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += async (_, __) => { timer.Interval = TimeSpan.FromHours(6); await Check(); };
            timer.Start();
            Task.Run(() => { try { client.CleanCache(); } catch (Exception ex) { Diagnostics.Write("Update cache cleanup: " + ex.GetType().Name); } });
        }
        static Task Check()
        {
            // Calls originate from the UI dispatcher; coalesce concurrent menu/timer requests.
            if (checking != null && !checking.IsCompleted)
                return checking;
            return checking = CheckCore();
        }
        static async Task CheckCore()
        {
            try
            {
                latest = await client.CheckAsync();
                checkFailed = false;
                Changed?.Invoke();
            }
            catch (Exception ex) { checkFailed = true; Diagnostics.Write("Update check: " + ex.GetType().Name); }
        }
        internal static async void Show()
        {
            if (dialog != null)
            {
                dialog.Activate();
                return;
            }
            var panel = new StackPanel();
            var status = new TextBlock { Text = "Checking GitHub for updates…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            var detail = new TextBlock { Text = "Installed version: " + ProductInfo.Version, Foreground = SqlPilot.UI.Design.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
            var progress = new ProgressBar { Height = 5, IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 16) };
            var install = SqlPilot.UI.Design.Primary("Download and install");
            install.IsEnabled = false;
            panel.Children.Add(status);
            panel.Children.Add(detail);
            panel.Children.Add(progress);
            panel.Children.Add(install);
            var window = SqlPilot.UI.Design.Window("SqlPilot updates", panel, 570, 370);
            window.Icon = Brand.Icon();
            dialog = window;
            var cancellation = new CancellationTokenSource();
            bool downloading = false;
            window.Closed += (_, __) => { cancellation.Cancel(); if (!downloading) cancellation.Dispose(); dialog = null; };
            window.Show();
            await Check();
            if (dialog != window)
            {
                cancellation.Dispose();
                return;
            }
            progress.Visibility = Visibility.Collapsed;
            var release = latest;
            if (!Available)
            {
                status.Text = checkFailed || release == null ? "Could not check for updates. Check your internet connection and try again." : "SqlPilot is up to date.";
                install.Content = "Check again";
                install.IsEnabled = true;
                install.Click += (_, __) => { window.Close(); Show(); };
                return;
            }
            status.Text = "SqlPilot " + release.Version.ToString(3) + " is available.";
            detail.Text += "\nThe installer can prepare updates while SSMS is open. Save your queries and restart SSMS to finish installing.";
            install.IsEnabled = true;
            install.Click += async (_, __) =>
            {
                if (downloading)
                    return;
                downloading = true;
                install.IsEnabled = false;
                progress.Visibility = Visibility.Visible;
                progress.IsIndeterminate = false;
                try
                {
                    var reporter = new Progress<int>(value => { if (dialog != window) return; progress.Value = value; status.Text = "Downloading update… " + value + "%"; });
                    string path = await client.DownloadAsync(release, reporter, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    status.Text = "Verified. Opening SqlPilot Setup…";
                    using (var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }))
                        if (process == null)
                            throw new IOException("Could not start SqlPilot Setup.");
                    window.Close();
                }
                catch (OperationCanceledException) { if (dialog == window) status.Text = "Download cancelled or timed out. Try again."; }
                catch (Exception ex) { if (dialog == window) status.Text = "Update failed: " + ex.Message; Diagnostics.Write("Update download: " + ex.GetType().Name); }
                finally { downloading = false; if (dialog == window) { install.IsEnabled = true; progress.Visibility = Visibility.Collapsed; } else cancellation.Dispose(); }
            };
        }
    }
}
