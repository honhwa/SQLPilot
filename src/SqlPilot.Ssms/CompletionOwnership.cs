// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text.Editor;

namespace SqlPilot.Ssms
{
    internal static class CompletionOwnership
    {
        internal static bool Enabled { get; private set; } = UserSettings.Current.Enabled;
        internal static event EventHandler Changed;
        static DTE host;
        static int editors;
        static readonly List<CommandBarButton> disabled = new List<CommandBarButton>();
        static readonly string[] Toggles = { "IntelliSense Enabled", "Enable IntelliSense", "Enable code suggestions", "Enable suggestions", "Show code suggestions", "Enable autocomplete", "Enable auto-completion" };
        internal static void SetHost(DTE dte)
        {
            host = dte;
            if (Enabled && editors > 0)
                TakeOver();
        }
        internal static void Toggle()
        {
            Enabled = !Enabled;
            UserSettings.Current.Enabled = Enabled;
            UserSettings.Save();
            if (Enabled && editors > 0)
                TakeOver();
            else
                Restore();
            Changed?.Invoke(null, EventArgs.Empty);
        }
        internal static void ApplySettings()
        {
            Enabled = UserSettings.Current.Enabled;
            if (Enabled && UserSettings.Current.ExclusiveCompletion && editors > 0)
                TakeOver();
            else
                Restore();
            Changed?.Invoke(null, EventArgs.Empty);
        }
        internal static void Acquire()
        {
            editors++;
            if (Enabled)
                TakeOver();
        }
        internal static void Release()
        {
            editors = Math.Max(0, editors - 1);
            if (editors == 0)
                Restore();
        }
        static string Caption(string text) => (text ?? "").Replace("&", "").Replace("_", "").Trim().TrimEnd('.');
        static IEnumerable<CommandBarButton> Buttons(CommandBarControls controls, int depth = 0)
        {
            if (depth > 8)
                yield break;
            foreach (CommandBarControl control in controls)
            {
                if (control is CommandBarButton button)
                    yield return button;
                if (control is CommandBarPopup popup)
                    foreach (var child in Buttons(popup.Controls, depth + 1))
                        yield return child;
            }
        }
        internal static void TakeOver()
        {
            if (!Enabled || !UserSettings.Current.ExclusiveCompletion || editors == 0 || host == null)
                return;
            try
            {
                foreach (CommandBar bar in (CommandBars)host.CommandBars)
                {
                    // Touch only recognized completion toggles, never whole extensions or unrelated menus.
                    if (!new[] { "MenuBar", "SQL Editor", "SQL Prompt", "SQL Assistant" }.Contains(bar.Name, StringComparer.OrdinalIgnoreCase))
                        continue;
                    foreach (var button in Buttons(bar.Controls).ToList())
                    {
                        if (!Toggles.Contains(Caption(button.Caption), StringComparer.OrdinalIgnoreCase) || !button.Enabled || button.State != MsoButtonState.msoButtonDown)
                            continue;
                        button.Execute();
                        if (button.State == MsoButtonState.msoButtonUp)
                        {
                            disabled.Add(button);
                            Diagnostics.Write("Completion takeover disabled: " + Caption(button.Caption));
                        }
                    }
                }
            }
            catch (Exception ex) { Diagnostics.Write("External completion toggle unavailable: " + ex.GetType().Name); }
        }
        static void Restore()
        {
            foreach (var button in disabled)
            {
                try
                {
                    if (button.Enabled && button.State == MsoButtonState.msoButtonUp)
                        button.Execute();
                }
                catch (Exception ex) { Diagnostics.Write("Completion toggle restore unavailable: " + ex.GetType().Name); }
            }
            disabled.Clear();
        }
    }
    internal sealed class CompletionGuard : IDisposable
    {
        readonly IWpfTextView view;
        readonly ICompletionBroker broker;
        readonly INotifyCollectionChanged sessions;
        readonly DispatcherTimer retry;
        object modernBroker;
        EventInfo modernEvent;
        Delegate modernHandler;
        bool dismissing;
        bool failureReported;
        bool modernFailed;
        int ticks;
        internal CompletionGuard(IWpfTextView view, ICompletionBroker broker, IIntellisenseSessionStackMapService stacks)
        {
            this.view = view;
            this.broker = broker;
            sessions = stacks?.GetStackForTextView(view).Sessions as INotifyCollectionChanged;
            if (sessions != null)
                sessions.CollectionChanged += Added;
            CompletionOwnership.Acquire();
            TryModernBroker();
            retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            retry.Tick += Tick;
            retry.Start();
            Dismiss();
        }
        void Added(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (CompletionOwnership.Enabled && !view.IsClosed)
                view.VisualElement.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Dismiss));
        }
        void Tick(object sender, EventArgs e)
        {
            if (!view.HasAggregateFocus || !CompletionOwnership.Enabled)
                return;
            Dismiss();
            if (++ticks % 20 == 0)
                CompletionOwnership.TakeOver();
        }
        internal void Dismiss()
        {
            if (!CompletionOwnership.Enabled || !UserSettings.Current.ExclusiveCompletion || view.IsClosed || dismissing)
                return;
            dismissing = true;
            try
            {
                broker.DismissAllSessions(view);
                if (modernBroker != null && !modernFailed)
                {
                    var session = modernBroker.GetType().GetInterfaces().First(t => t.Name == "IAsyncCompletionBroker").GetMethod("GetSession")?.Invoke(modernBroker, new object[] { view });
                    var contract = session?.GetType().GetInterfaces().FirstOrDefault(t => t.Name == "IAsyncCompletionSession");
                    contract?.GetMethod("Dismiss")?.Invoke(session, null);
                }
            }
            catch (Exception ex)
            {
                modernFailed = true;
                if (!failureReported)
                {
                    failureReported = true;
                    Diagnostics.Write("Completion session suppression: " + ex.GetType().Name);
                }
            }
            finally { dismissing = false; }
        }
        void TryModernBroker()
        {
            // Optional adapter: SSMS 20 has no modern completion broker. Avoid a hard assembly dependency.
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var brokerType = assemblies.Select(a => a.GetType("Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.IAsyncCompletionBroker")).FirstOrDefault(t => t != null);
                var serviceType = assemblies.Select(a => a.GetType("Microsoft.VisualStudio.ComponentModelHost.SComponentModel")).FirstOrDefault(t => t != null);
                var modelType = assemblies.Select(a => a.GetType("Microsoft.VisualStudio.ComponentModelHost.IComponentModel")).FirstOrDefault(t => t != null);
                if (brokerType == null || serviceType == null || modelType == null)
                    return;
                var model = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider.GetService(serviceType);
                modernBroker = modelType.GetMethod("GetService").MakeGenericMethod(brokerType).Invoke(model, null);
                modernEvent = brokerType.GetEvent("CompletionTriggered");
                if (modernEvent == null)
                    return;
                modernHandler = Delegate.CreateDelegate(modernEvent.EventHandlerType, this, GetType().GetMethod("ModernTriggered", BindingFlags.Instance | BindingFlags.NonPublic));
                modernEvent.AddEventHandler(modernBroker, modernHandler);
            }
            catch (Exception ex) { Diagnostics.Write("Modern completion adapter unavailable: " + ex.GetType().Name); }
        }
        void ModernTriggered(object sender, EventArgs e)
        {
            // Each guard dismisses only its own SQL view's session.
            if (CompletionOwnership.Enabled)
                view.VisualElement.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Dismiss));
        }
        public void Dispose()
        {
            retry.Stop();
            retry.Tick -= Tick;
            if (sessions != null)
                sessions.CollectionChanged -= Added;
            if (modernHandler != null)
            {
                try
                {
                    modernEvent.RemoveEventHandler(modernBroker, modernHandler);
                }
                catch { }
            }
            CompletionOwnership.Release();
        }
    }
}
