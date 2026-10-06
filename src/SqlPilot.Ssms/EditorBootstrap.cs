// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;

namespace SqlPilot.Ssms
{
    // SSMS isolated shells can load the package without cataloging its MEF listeners.
    // Discover the active native view through host services as a second attachment path.
    internal static class EditorBootstrap
    {
        static DispatcherTimer timer;
        static string lastError;
        internal static void Start()
        {
            if (timer != null)
                return;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, __) => Attach();
            timer.Start();
            Attach();
        }
        static T Service<T>(object model) where T : class
        {
            var contract = model.GetType().GetInterfaces().First(t => t.FullName == "Microsoft.VisualStudio.ComponentModelHost.IComponentModel");
            return (T)contract.GetMethods().First(m => m.Name == "GetService" && m.IsGenericMethod).MakeGenericMethod(typeof(T)).Invoke(model, null);
        }
        internal static Controller Attach()
        {
            try
            {
                var manager = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager)) as IVsTextManager;
                if (manager == null || manager.GetActiveView(0, null, out var adapter) != 0 || adapter == null)
                    return null;
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Microsoft.VisualStudio.ComponentModelHost")
                    ?? Assembly.Load("Microsoft.VisualStudio.ComponentModelHost");
                var model = ServiceProvider.GlobalProvider.GetService(assembly.GetType("Microsoft.VisualStudio.ComponentModelHost.SComponentModel", true));
                if (model == null)
                    return null;
                var view = Service<IVsEditorAdaptersFactoryService>(model).GetWpfTextView(adapter);
                if (view == null || view.IsClosed)
                    return null;
                Service<ITextDocumentFactoryService>(model).TryGetTextDocument(view.TextBuffer, out var document);
                if (!Listener.IsSql(view, document))
                    return null;
                var controller = view.Properties.GetOrCreateSingletonProperty(() =>
                {
                    Diagnostics.Write("Package fallback discovered SQL editor.");
                    IIntellisenseSessionStackMapService stacks = null;
                    try
                    {
                        stacks = Service<IIntellisenseSessionStackMapService>(model);
                    }
                    catch { }
                    return new Controller(view, document, Service<ICompletionBroker>(model), stacks);
                });
                view.Properties.GetOrCreateSingletonProperty(() => new NativeFilter(adapter, controller));
                if (Controller.Active != controller && view.HasAggregateFocus)
                    controller.Activate();
                Sessions.Register(controller);
                return controller;
            }
            catch (Exception ex)
            {
                string error = ex.ToString();
                if (error != lastError)
                {
                    lastError = error;
                    Diagnostics.Write("Editor fallback failed: " + error);
                }
                return null;
            }
        }
    }
}
