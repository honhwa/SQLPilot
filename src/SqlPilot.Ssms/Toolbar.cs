// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EnvDTE;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;

namespace SqlPilot.Ssms
{
    [Guid("c429a14e-8788-41a8-9e6f-95a2323c0fe3")]
    public sealed class ToolbarPackage : Package
    {
        static ToolbarPackage instance;
        internal static void EnsureToolbar(DTE dte)
        {
            CompletionOwnership.SetHost(dte);
            ShortcutRouter.Start(dte);
            Sessions.Start(dte);
            if (instance != null)
                return;
            var candidate = new ToolbarPackage();
            candidate.CreateToolbar(dte);
            if (candidate.bar != null && candidate.buttons.Count > 0)
                instance = candidate;
        }
        readonly List<CommandBarButton> buttons = new List<CommandBarButton>();
        CommandBar bar;
        protected override void Initialize()
        {
            base.Initialize();
            EnsureToolbar((DTE)GetService(typeof(DTE)));
            EditorBootstrap.Start();
        }
        void CreateToolbar(DTE dte)
        {
            try
            {
                var bars = (CommandBars)dte.CommandBars;
                try
                {
                    bar = bars["SqlPilot"];
                }
                catch (ArgumentException) { }
                catch (COMException) { }
                if (bar == null)
                    bar = bars.Add("SqlPilot", MsoBarPosition.msoBarTop, false, true);
                while (bar.Controls.Count > 0)
                    bar.Controls[1].Delete(false);
                Add("SqlPilot", null, true, () => MainMenu.Show(dte));
                bar.Visible = true;
                Diagnostics.Write("Main SqlPilot toolbar initialized.");
            }
            catch (Exception ex) { Diagnostics.Write("Toolbar initialization failed: " + ex); }
        }
        CommandBarButton Add(string caption, Action<Controller> action, bool icon, Action globalAction = null)
        {
            var button = (CommandBarButton)bar.Controls.Add(MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
            button.Caption = caption;
            button.TooltipText = "SqlPilot: " + caption;
            button.Style = icon ? MsoButtonStyle.msoButtonIconAndCaption : MsoButtonStyle.msoButtonCaption;
            if (icon)
            {
                using (var stream = typeof(Brand).Assembly.GetManifestResourceStream("SqlPilot.Icon.png"))
                using (var image = Image.FromStream(stream))
                using (var bitmap = new Bitmap(image, 20, 20))
                    button.Picture = (stdole.StdPicture)PictureHost.Convert(bitmap);
            }
            button.Click += delegate (CommandBarButton sender, ref bool cancelDefault)
            {
                cancelDefault = true;
                if (globalAction != null)
                {
                    globalAction();
                    return;
                }
                var controller = EditorBootstrap.Attach() ?? Controller.Active;
                if (controller == null || controller.View.IsClosed)
                {
                    MessageBox.Show("Open a SQL query tab to use SqlPilot.", "SqlPilot");
                    return;
                }
                action(controller);
            };
            buttons.Add(button);
            return button;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && bar != null)
            {
                try
                {
                    bar.Delete();
                }
                catch { }
                buttons.Clear();
            }
            base.Dispose(disposing);
        }
        sealed class PictureHost : AxHost
        {
            PictureHost() : base("") { }
            internal static object Convert(Image image) => GetIPictureDispFromPicture(image);
        }
    }
}
