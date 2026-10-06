// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    internal static class Brand
    {
        internal static System.Windows.Media.Imaging.BitmapImage Icon()
        {
            using (var stream = typeof(Brand).Assembly.GetManifestResourceStream("SqlPilot.Icon.png"))
            {
                if (stream == null)
                    return null;
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
    }
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class Listener : IWpfTextViewCreationListener
    {
        [Import] internal ITextDocumentFactoryService Documents = null;
        [Import] internal ICompletionBroker Broker = null;
        [Import] internal IIntellisenseSessionStackMapService Stacks = null;
        [Import(typeof(Microsoft.VisualStudio.Shell.SVsServiceProvider))] internal IServiceProvider Services = null;
        internal static bool IsSql(IWpfTextView view, ITextDocument document) => view.TextBuffer.ContentType.IsOfType("SQL") || view.TextBuffer.ContentType.IsOfType("TSQL") || string.Equals(System.IO.Path.GetExtension(document?.FilePath), ".sql", StringComparison.OrdinalIgnoreCase);
        public void TextViewCreated(IWpfTextView view)
        {
            view.VisualElement.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { try { ToolbarPackage.EnsureToolbar((EnvDTE.DTE)Services.GetService(typeof(EnvDTE.DTE))); } catch (Exception ex) { Diagnostics.Write("Toolbar fallback: " + ex); } }));
            Diagnostics.Write("Editor discovered: " + view.TextBuffer.ContentType.TypeName);
            Documents.TryGetTextDocument(view.TextBuffer, out var document);
            if (!IsSql(view, document))
                return;
            view.Properties.GetOrCreateSingletonProperty(() => new Controller(view, document, Broker, Stacks));
        }
    }
}
