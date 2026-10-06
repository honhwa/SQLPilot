// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    // Advertise Tab only when the same expansion used by the Tab command can succeed.
    internal sealed class StarHint : IDisposable
    {
        readonly IWpfTextView view;
        readonly Func<List<DbObject>> catalog;
        readonly Func<bool> enabled;
        readonly Action shown;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        readonly Popup popup = new Popup { Placement = PlacementMode.Relative, StaysOpen = true, AllowsTransparency = true, Focusable = false, IsHitTestVisible = false };
        readonly TextBlock label = new TextBlock { Text = "Tab — Expand all columns", FontFamily = new FontFamily("Segoe UI"), FontSize = 11 };
        readonly Border card;
        bool disposed, running, pending;
        int revision;
        internal StarHint(IWpfTextView view, Func<List<DbObject>> catalog, Func<bool> enabled, Action shown)
        {
            this.view = view;
            this.catalog = catalog;
            this.enabled = enabled;
            this.shown = shown;
            card = new Border { Child = label, Padding = new Thickness(7, 3, 7, 3), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Opacity = .95 };
            popup.Child = card;
            popup.PlacementTarget = view.VisualElement;
            view.Caret.PositionChanged += Caret;
            view.Selection.SelectionChanged += Selection;
            view.TextBuffer.Changed += Changed;
            view.LayoutChanged += Layout;
            view.GotAggregateFocus += Focus;
            view.LostAggregateFocus += Blur;
            timer.Tick += Evaluate;
            Invalidate();
        }
        internal void Dismiss()
        {
            revision++;
            pending = false;
            timer.Stop();
            popup.IsOpen = false;
        }
        internal void Invalidate()
        {
            Dismiss();
            pending = true;
            if (!disposed)
                timer.Start();
        }
        void Caret(object sender, CaretPositionChangedEventArgs e) => Invalidate();
        void Selection(object sender, EventArgs e) => Invalidate();
        void Changed(object sender, TextContentChangedEventArgs e) => Invalidate();
        void Focus(object sender, EventArgs e) => Invalidate();
        void Blur(object sender, EventArgs e) => Dismiss();
        void Layout(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (popup.IsOpen)
                popup.IsOpen = Position();
        }
        async void Evaluate(object sender, EventArgs e)
        {
            timer.Stop();
            if (disposed || view.IsClosed || !view.HasAggregateFocus || !view.Selection.IsEmpty || !enabled())
                return;
            if (running)
            {
                pending = true;
                return;
            }
            var snapshot = view.TextBuffer.CurrentSnapshot;
            int caret = view.Caret.Position.BufferPosition.Position;
            if (!(caret > 0 && snapshot[caret - 1] == '*') && !(caret < snapshot.Length && snapshot[caret] == '*'))
                return;
            var objects = catalog();
            if (objects.Count == 0)
                return;
            int request = revision;
            running = true;
            pending = false;
            try
            {
                var expansion = await Task.Run(() => Engine.ExpandStar(snapshot.GetText(), caret, objects));
                if (expansion == null || disposed || view.IsClosed || revision != request || snapshot != view.TextBuffer.CurrentSnapshot ||
                    catalog() != objects || !view.HasAggregateFocus || !view.Selection.IsEmpty || !enabled())
                    return;
                bool dark = view.Background is SolidColorBrush brush && brush.Color.R + brush.Color.G + brush.Color.B < 384;
                label.Foreground = new SolidColorBrush(dark ? Color.FromRgb(184, 198, 217) : Color.FromRgb(82, 105, 133));
                card.Background = new SolidColorBrush(dark ? Color.FromRgb(35, 43, 54) : Color.FromRgb(240, 246, 253));
                card.BorderBrush = new SolidColorBrush(dark ? Color.FromRgb(67, 82, 102) : Color.FromRgb(211, 223, 237));
                if (Position())
                {
                    shown();
                    popup.IsOpen = true;
                }
            }
            catch (Exception ex) { Diagnostics.Write("Star hint failed: " + ex.GetType().Name); }
            finally { running = false; if (!disposed && pending) timer.Start(); }
        }
        bool Position()
        {
            var line = view.Caret.ContainingTextViewLine;
            if (line == null || line.Bottom < view.ViewportTop || line.Top > view.ViewportBottom)
                return false;
            popup.HorizontalOffset = Math.Max(0, Math.Min(view.Caret.Left - view.ViewportLeft, view.ViewportWidth - 190));
            popup.VerticalOffset = line.Bottom - view.ViewportTop + 3;
            return true;
        }
        public void Dispose()
        {
            disposed = true;
            Dismiss();
            view.Caret.PositionChanged -= Caret;
            view.Selection.SelectionChanged -= Selection;
            view.TextBuffer.Changed -= Changed;
            view.LayoutChanged -= Layout;
            view.GotAggregateFocus -= Focus;
            view.LostAggregateFocus -= Blur;
        }
    }
}
