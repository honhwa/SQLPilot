// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    internal sealed class MetadataHover : IDisposable
    {
        readonly IWpfTextView view;
        readonly Func<IEnumerable<DbObject>> catalog;
        readonly ToolTip tip = new ToolTip { Placement = PlacementMode.Mouse, MaxWidth = 560, StaysOpen = false };
        internal MetadataHover(IWpfTextView view, Func<IEnumerable<DbObject>> catalog)
        {
            this.view = view;
            this.catalog = catalog;
            view.MouseHover += Hover;
            view.VisualElement.MouseLeave += Leave;
            view.VisualElement.PreviewKeyDown += Key;
            view.TextBuffer.Changed += Changed;
            view.LostAggregateFocus += LostFocus;
            view.LayoutChanged += Layout;
        }
        void Hover(object sender, MouseHoverEventArgs args)
        {
            try
            {
                if (view.IsClosed || !view.HasAggregateFocus || Mouse.LeftButton != MouseButtonState.Released)
                    return;
                var description = Engine.DescribeAt(view.TextBuffer.CurrentSnapshot.GetText(), args.Position, catalog());
                tip.IsOpen = false;
                if (description == null)
                    return;
                var panel = new StackPanel { MaxWidth = 510, Resources = SqlPilot.UI.Design.Resources() };
                panel.Children.Add(new TextBlock { Text = description.Title, FontWeight = FontWeights.SemiBold, Foreground = SqlPilot.UI.Design.Accent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7) });
                panel.Children.Add(new TextBlock { Text = description.Body, Foreground = SqlPilot.UI.Design.Ink, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12 });
                tip.Content = new ScrollViewer { Content = panel, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                tip.PlacementTarget = view.VisualElement;
                tip.IsOpen = true;
            }
            catch (Exception ex) { tip.IsOpen = false; Diagnostics.Write("Metadata hover: " + ex.GetType().Name); }
        }
        void Leave(object sender, MouseEventArgs e) => tip.IsOpen = false;
        void Key(object sender, KeyEventArgs e) => tip.IsOpen = false;
        void Changed(object sender, TextContentChangedEventArgs e) => tip.IsOpen = false;
        void LostFocus(object sender, EventArgs e) => tip.IsOpen = false;
        void Layout(object sender, TextViewLayoutChangedEventArgs e) => tip.IsOpen = false;
        internal void Dismiss() => tip.IsOpen = false;
        public void Dispose()
        {
            tip.IsOpen = false;
            view.MouseHover -= Hover;
            view.VisualElement.MouseLeave -= Leave;
            view.VisualElement.PreviewKeyDown -= Key;
            view.TextBuffer.Changed -= Changed;
            view.LostAggregateFocus -= LostFocus;
            view.LayoutChanged -= Layout;
        }
    }
}
