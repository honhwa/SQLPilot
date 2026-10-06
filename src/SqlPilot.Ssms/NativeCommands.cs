// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace SqlPilot.Ssms
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class NativeListener : IVsTextViewCreationListener
    {
        [Import] internal IVsEditorAdaptersFactoryService Adapters = null;
        [Import] internal Microsoft.VisualStudio.Text.ITextDocumentFactoryService Documents = null;
        [Import] internal Microsoft.VisualStudio.Language.Intellisense.ICompletionBroker Broker = null;
        [Import] internal Microsoft.VisualStudio.Language.Intellisense.IIntellisenseSessionStackMapService Stacks = null;
        public void VsTextViewCreated(IVsTextView adapter)
        {
            var view = Adapters.GetWpfTextView(adapter);
            if (view == null)
                return;
            Documents.TryGetTextDocument(view.TextBuffer, out var document);
            if (!Listener.IsSql(view, document))
                return;
            var controller = view.Properties.GetOrCreateSingletonProperty(() => new Controller(view, document, Broker, Stacks));
            try
            {
                view.Properties.GetOrCreateSingletonProperty(() => new NativeFilter(adapter, controller));
            }
            catch (Exception ex) { Diagnostics.Write("Native command filter failed: " + ex); }
        }
    }
    internal sealed class NativeFilter : IOleCommandTarget
    {
        internal static bool IsCompletionCommand(uint id) => id == (uint)Microsoft.VisualStudio.VSConstants.VSStd2KCmdID.COMPLETEWORD ||
            id == (uint)Microsoft.VisualStudio.VSConstants.VSStd2KCmdID.SHOWMEMBERLIST || id == (uint)Microsoft.VisualStudio.VSConstants.VSStd2KCmdID.AUTOCOMPLETE;
        static readonly Guid EditorCommands = new Guid("1496a755-94de-11d0-8c3f-00c04fc2aae2");
        static readonly Guid NavigationCommands = Microsoft.VisualStudio.VSConstants.GUID_VSStandardCommandSet97;
        static bool Navigation(uint id) => id == (uint)Microsoft.VisualStudio.VSConstants.VSStd97CmdID.GotoDefn || id == (uint)Microsoft.VisualStudio.VSConstants.VSStd97CmdID.GotoDecl;
        readonly IVsTextView adapter;
        readonly Controller controller;
        IOleCommandTarget next;
        internal NativeFilter(IVsTextView adapter, Controller controller)
        {
            this.adapter = adapter;
            this.controller = controller;
            int result = adapter.AddCommandFilter(this, out next);
            System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(result);
            controller.NativeKeys = true;
            controller.View.Closed += Closed;
            Diagnostics.Write("Native editor command filter attached.");
        }
        void Closed(object sender, EventArgs e)
        {
            adapter.RemoveCommandFilter(this);
            controller.View.Closed -= Closed;
        }
        public int QueryStatus(ref Guid group, uint count, OLECMD[] commands, IntPtr text)
        {
            int result = next.QueryStatus(ref group, count, commands, text);
            bool handled = false;
            if (group == NavigationCommands)
                for (int i = 0; i < Math.Min(count, commands.Length); i++)
                    if (Navigation(commands[i].cmdID))
                    {
                        commands[i].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_ENABLED);
                        handled = true;
                    }
            if (group == EditorCommands && CompletionOwnership.Enabled)
                for (int i = 0; i < Math.Min(count, commands.Length); i++)
                    if (IsCompletionCommand(commands[i].cmdID))
                    {
                        // The shell checks availability before dispatching Ctrl+Space. Native
                        // IntelliSense is disabled during takeover, but our commands remain enabled.
                        commands[i].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_ENABLED);
                        handled = true;
                    }
            return handled ? 0 : result;
        }
        public int Exec(ref Guid group, uint id, uint options, IntPtr input, IntPtr output)
        {
            if (group == EditorCommands)
                controller.BeforeEditorInput();
            if (group == NavigationCommands && Navigation(id))
            {
                controller.NavigateReference(id == (uint)Microsoft.VisualStudio.VSConstants.VSStd97CmdID.GotoDecl);
                return 0;
            }
            // VSStd2K: RETURN=3, TAB=4, CANCEL=103, UP=11, DOWN=13,
            // Use host SDK completion constants; snippet picker commands pass through.
            if (group == EditorCommands && controller.HandleEditorCommand(id))
                return 0;
            controller.BeforeEditorInput();
            int result = next.Exec(ref group, id, options, input, output);
            controller.BeforeEditorInput();
            return result;
        }
    }
}
