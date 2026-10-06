// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SqlPilot.Ssms
{
    internal static class AboutDialog
    {
        internal static StackPanel CreateContent(string status)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = ProductInfo.Author, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var contact = new TextBlock { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var address in new[] { ProductInfo.TelegramUrl, ProductInfo.RepositoryUrl })
            {
                if (contact.Inlines.Count > 0)
                    contact.Inlines.Add(new LineBreak());
                var link = new Hyperlink(new Run(address)) { NavigateUri = new Uri(address), Foreground = SqlPilot.UI.Design.Accent };
                link.RequestNavigate += (_, e) => { try { System.Diagnostics.Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { Notifications.Show("Could not open the link. Copy it from the project README."); } e.Handled = true; };
                contact.Inlines.Add(link);
            }
            panel.Children.Add(contact);
            panel.Children.Add(new TextBlock { Text = ProductInfo.Copyright + "\n" + ProductInfo.LicenseName + "\nFree installation and use of the original version. Modification, redistribution and resale require written permission.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
            panel.Children.Add(new TextBlock { Text = status ?? "Open a SQL query tab to load database metadata.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
            panel.Children.Add(new TextBlock { Text = "Type in a SQL tab to see contextual suggestions.\nTab / Enter: accept · Up / Down: select · Esc: dismiss\n\nEdit Tab shortcuts to rename snippets such as ssf → sf.\nKeyboard shortcuts can be reassigned from the SqlPilot menu.\nSessions are encrypted for your Windows account and restore without executing SQL.", TextWrapping = TextWrapping.Wrap });
            return panel;
        }
        internal static void Show(string status)
        {
            var window = SqlPilot.UI.Design.Window("About SqlPilot", new ScrollViewer { Content = CreateContent(status), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 700, 610, "Version " + ProductInfo.Version + " · Your SQL workspace assistant");
            window.Icon = Brand.Icon();
            window.ShowDialog();
        }
    }
}
