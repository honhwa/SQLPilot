// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    internal sealed class InlineDiagnostics : IDisposable
    {
        readonly IWpfTextView view;
        readonly IAdornmentLayer layer;
        readonly object tag = new object();
        readonly DispatcherTimer debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        readonly DispatcherTimer hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        readonly Popup actions = new Popup { Placement = PlacementMode.Relative, StaysOpen = true, AllowsTransparency = true };
        readonly DispatcherTimer leave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly Func<string, Issue, TextExpansion> resolveFix;
        readonly Action<TextExpansion> applyFix;
        readonly Action<Issue> review;
        readonly Action<Issue> configure;
        readonly List<Tuple<Rect, InlineIssue>> hits = new List<Tuple<Rect, InlineIssue>>();
        List<InlineIssue> marks = new List<InlineIssue>();
        ITextSnapshot analyzed;
        InlineIssue hovered;
        bool disposed, running, pending;
        double renderedLeft, renderedTop, renderedWidth;
        internal InlineDiagnostics(IWpfTextView view, Func<string, Issue, TextExpansion> resolveFix, Action<TextExpansion> applyFix, Action<Issue> review, Action<Issue> configure)
        {
            this.view = view;
            this.resolveFix = resolveFix;
            this.applyFix = applyFix;
            this.review = review;
            this.configure = configure;
            actions.PlacementTarget = view.VisualElement;
            // Use the host's existing layer so the package fallback also works without MEF discovery.
            layer = view.GetAdornmentLayer(PredefinedAdornmentLayers.Squiggle);
            view.TextBuffer.Changed += Changed;
            view.LayoutChanged += Layout;
            view.VisualElement.MouseMove += MouseMove;
            view.VisualElement.MouseLeave += MouseLeave;
            view.VisualElement.PreviewKeyDown += KeyDown;
            UserSettings.Changed += Settings;
            debounce.Tick += Analyze;
            hover.Tick += ShowActions;
            leave.Tick += (_, __) => { leave.Stop(); if (actions.Child?.IsMouseOver != true && actions.Child?.IsKeyboardFocusWithin != true) Dismiss(); };
            debounce.Start();
        }
        void Dismiss()
        {
            actions.IsOpen = false;
            hovered = null;
            hover.Stop();
            leave.Stop();
        }
        void Clear()
        {
            layer.RemoveAdornmentsByTag(tag);
            hits.Clear();
            Dismiss();
        }
        void Changed(object sender, TextContentChangedEventArgs e)
        {
            Clear();
            analyzed = null;
            pending = true;
            debounce.Stop();
            debounce.Start();
        }
        void Settings(object sender, EventArgs e)
        {
            Clear();
            debounce.Stop();
            debounce.Start();
        }
        async void Analyze(object sender, EventArgs e)
        {
            debounce.Stop();
            if (disposed || view.IsClosed || !UserSettings.Current.InlineDiagnostics)
                return;
            if (running)
            {
                pending = true;
                return;
            }
            running = true;
            pending = false;
            var snapshot = view.TextBuffer.CurrentSnapshot;
            try
            {
                var result = await Task.Run(() => Engine.InlineIssues(snapshot.GetText()));
                if (!disposed && !view.IsClosed && UserSettings.Current.InlineDiagnostics && snapshot == view.TextBuffer.CurrentSnapshot)
                {
                    marks = result;
                    analyzed = snapshot;
                    Render();
                }
            }
            catch (Exception ex) { Diagnostics.Write("Inline analysis failed: " + ex.GetType().Name); }
            finally { running = false; if (!disposed && pending) { debounce.Stop(); debounce.Start(); } }
        }
        void Layout(object sender, TextViewLayoutChangedEventArgs e)
        {
            Render(actions.IsOpen && analyzed == view.TextBuffer.CurrentSnapshot && renderedLeft == view.ViewportLeft && renderedTop == view.ViewportTop && renderedWidth == view.ViewportWidth);
        }
        void Render(bool keepActions = false)
        {
            layer.RemoveAdornmentsByTag(tag);
            hits.Clear();
            if (!keepActions)
                Dismiss();
            renderedLeft = view.ViewportLeft;
            renderedTop = view.ViewportTop;
            renderedWidth = view.ViewportWidth;
            if (disposed || !UserSettings.Current.InlineDiagnostics || analyzed != view.TextBuffer.CurrentSnapshot || view.TextViewLines == null)
                return;
            foreach (var mark in marks)
            {
                var span = new SnapshotSpan(analyzed, mark.Offset, mark.Length);
                if (!view.TextViewLines.FormattedSpan.IntersectsWith(span))
                    continue;
                foreach (var bound in view.TextViewLines.GetNormalizedTextBounds(span))
                {
                    var line = new Line
                    {
                        X1 = 0,
                        X2 = Math.Max(2, bound.Width),
                        Y1 = 1,
                        Y2 = 1,
                        Stroke = ColorFor(mark.Severity),
                        StrokeThickness = 1.5,
                        StrokeDashArray = mark.Severity == IssueSeverity.Error ? new DoubleCollection { 2, 1.5 } : new DoubleCollection { 1, 2 },
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(line, bound.Left);
                    Canvas.SetTop(line, bound.TextBottom - 1);
                    layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, tag, line, null);
                    hits.Add(Tuple.Create(new Rect(bound.Left - view.ViewportLeft, bound.Top - view.ViewportTop, Math.Max(2, bound.Width), bound.Height + 3), mark));
                }
            }
        }
        internal static Brush ColorFor(IssueSeverity severity) => new SolidColorBrush(severity == IssueSeverity.Error ? Color.FromRgb(230, 74, 74) :
            severity == IssueSeverity.Warning ? Color.FromRgb(207, 145, 20) : Color.FromRgb(143, 88, 211));
        void MouseMove(object sender, MouseEventArgs e)
        {
            var point = e.GetPosition(view.VisualElement);
            var next = hits.FirstOrDefault(h => h.Item1.Contains(point))?.Item2;
            if (next == hovered)
            {
                if (next != null)
                    leave.Stop();
                return;
            }
            if (next == null)
            {
                hover.Stop();
                leave.Stop();
                leave.Start();
                return;
            }
            Dismiss();
            hovered = next;
            hover.Start();
        }
        void ShowActions(object sender, EventArgs e)
        {
            hover.Stop();
            if (hovered == null || disposed)
                return;
            var snapshot = analyzed;
            if (snapshot == null || snapshot != view.TextBuffer.CurrentSnapshot)
                return;
            var panel = new StackPanel { Width = 390, Resources = SqlPilot.UI.Design.Resources(), FlowDirection = FlowDirection.LeftToRight };
            System.Windows.Documents.TextElement.SetForeground(panel, SqlPilot.UI.Design.Ink);
            foreach (var issue in hovered.Issues)
            {
                panel.Children.Add(new TextBlock { Text = Engine.Severity(issue) + " · " + issue.Code, Foreground = ColorFor(Engine.Severity(issue)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 3) });
                panel.Children.Add(new TextBlock { Text = "Reason: " + issue.Message, Foreground = SqlPilot.UI.Design.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                var fix = resolveFix(snapshot.GetText(), issue);
                if (fix != null)
                {
                    var preview = new TextBlock { Text = fix.Text, Foreground = SqlPilot.UI.Design.Ink, FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
                    panel.Children.Add(new ScrollViewer { Content = preview, MaxHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 7, 0, 0) });
                    var apply = SqlPilot.UI.Design.Primary("Apply fix");
                    apply.IsDefault = false;
                    apply.HorizontalAlignment = HorizontalAlignment.Left;
                    apply.Margin = new Thickness(0, 8, 0, 8);
                    apply.Click += (_, __) =>
                    {
                        if (disposed || snapshot != view.TextBuffer.CurrentSnapshot)
                        {
                            Dismiss();
                            return;
                        }
                        try
                        {
                            var fresh = resolveFix(snapshot.GetText(), issue);
                            Dismiss();
                            if (fresh != null)
                                applyFix(fresh);
                        }
                        catch (Exception ex) { MessageBox.Show("The fix could not be applied: " + ex.Message, "SqlPilot"); }
                    };
                    panel.Children.Add(apply);
                }
                else
                {
                    panel.Children.Add(new TextBlock { Text = Engine.DiagnosticFixHint(issue), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = SqlPilot.UI.Design.Muted, Margin = new Thickness(0, 6, 0, 0) });
                }
                if (fix == null && Engine.CanConfigureDiagnosticFix(issue))
                {
                    var setup = new Button { Content = issue.Code == "TOP-ORDER" ? "Choose ORDER BY…" : "Add WHERE…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                    setup.Click += (_, __) => { if (snapshot != view.TextBuffer.CurrentSnapshot) { Dismiss(); return; } Dismiss(); configure(issue); };
                    panel.Children.Add(setup);
                }
                var jump = new Button { Content = "Review in editor", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 10) };
                jump.Click += (_, __) => { if (snapshot != view.TextBuffer.CurrentSnapshot) { Dismiss(); return; } Dismiss(); review(issue); };
                panel.Children.Add(jump);
            }
            var card = SqlPilot.UI.Design.Card(new ScrollViewer { Content = panel, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            card.Padding = new Thickness(14);
            card.CornerRadius = new CornerRadius(9);
            card.MouseEnter += (_, __) => leave.Stop();
            card.MouseLeave += (_, __) => { leave.Stop(); leave.Start(); };
            KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
            card.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    Dismiss();
                    Controller.Active?.DismissCompletion();
                    view.VisualElement.Focus();
                }
                else if (args.Key == Key.Enter && Keyboard.FocusedElement is Button button && button.IsEnabled)
                {
                    args.Handled = true;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };
            var bounds = hits.FirstOrDefault(h => h.Item2 == hovered)?.Item1;
            actions.HorizontalOffset = bounds?.Left ?? Math.Max(0, view.Caret.Left - view.ViewportLeft);
            actions.VerticalOffset = bounds?.Bottom ?? view.Caret.ContainingTextViewLine.Bottom - view.ViewportTop;
            actions.Child = card;
            actions.IsOpen = true;
        }
        internal async void ShowAtCaret()
        {
            if (!UserSettings.Current.InlineDiagnostics || disposed || view.IsClosed)
                return;
            if (analyzed != view.TextBuffer.CurrentSnapshot)
            {
                var snapshot = view.TextBuffer.CurrentSnapshot;
                try
                {
                    var result = await Task.Run(() => Engine.InlineIssues(snapshot.GetText()));
                    if (disposed || view.IsClosed || snapshot != view.TextBuffer.CurrentSnapshot)
                        return;
                    marks = result;
                    analyzed = snapshot;
                    Render();
                }
                catch (Exception ex) { Diagnostics.Write("Requested diagnostics failed: " + ex.GetType().Name); return; }
            }
            int caret = view.Caret.Position.BufferPosition.Position;
            var mark = marks.FirstOrDefault(m => caret >= m.Offset && caret <= m.Offset + m.Length);
            if (mark == null)
                mark = marks.Where(m => m.Issues.Any(i => caret >= i.Offset && caret < i.Offset + i.Length))
                .OrderBy(m => m.Issues.Where(i => caret >= i.Offset && caret < i.Offset + i.Length).Min(i => i.Length))
                .ThenBy(m => Math.Abs(m.Offset - caret)).FirstOrDefault();
            if (mark == null)
                mark = marks.Where(m => analyzed.GetLineFromPosition(m.Offset).LineNumber == analyzed.GetLineFromPosition(caret).LineNumber).OrderBy(m => Math.Abs(m.Offset - caret)).FirstOrDefault();
            if (mark == null)
                return;
            Dismiss();
            hovered = mark;
            ShowActions(this, EventArgs.Empty);
            (actions.Child as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
        void MouseLeave(object sender, MouseEventArgs e)
        {
            hover.Stop();
            leave.Stop();
            leave.Start();
        }
        void KeyDown(object sender, KeyEventArgs e)
        {
            Dismiss();
        }
        public void Dispose()
        {
            disposed = true;
            debounce.Stop();
            hover.Stop();
            leave.Stop();
            Clear();
            view.TextBuffer.Changed -= Changed;
            view.LayoutChanged -= Layout;
            view.VisualElement.MouseMove -= MouseMove;
            view.VisualElement.MouseLeave -= MouseLeave;
            view.VisualElement.PreviewKeyDown -= KeyDown;
            UserSettings.Changed -= Settings;
        }
    }
}
