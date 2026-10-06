// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
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
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    internal sealed partial class Controller
    {
        internal static Controller Active;
        internal bool NativeKeys;
        internal IWpfTextView View => view;
        internal string Status => status;
        internal void CompletionCommand() => ShowCompletion(true);
        readonly CompletionGuard guard;
        readonly CompletionState completionState = new CompletionState();
        readonly InlineDiagnostics inlineDiagnostics;
        readonly StarHint starHint;
        readonly IWpfTextView view; readonly ITextDocument document; readonly ICompletionBroker broker;
        readonly string tab = Guid.NewGuid().ToString("N");
        readonly DispatcherTimer completionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        readonly DispatcherTimer historyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        readonly DispatcherTimer connectionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        readonly Popup scanPopup = new Popup { StaysOpen = true, AllowsTransparency = true, Placement = PlacementMode.Relative, Focusable = false, IsHitTestVisible = false };
        readonly TextBlock scanLabel = new TextBlock { Text = "Scanning metadata…", TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, Foreground = Brushes.White };
        bool scanning; DateTime retryAfter;
        readonly List<ITrackingSpan> parameterStops = new List<ITrackingSpan>(); int parameterIndex;
        readonly ListBox list = new ListBox { MinWidth = 330, MaxWidth = 650, MaxHeight = 300, FontFamily = new FontFamily("Consolas"), FontSize = 13 };
        readonly Popup popup = new Popup { StaysOpen = true, AllowsTransparency = true, Placement = PlacementMode.Relative };
        List<DbObject> catalog = new List<DbObject>(); string connection, manualConnection, observedHostConnection; CancellationTokenSource scan; Task<List<DbObject>> metadataTask;
        bool replacing, dirty; int replacementStart, replacementLength, generation;
        string status = "Schema is not loaded. Ctrl+Shift+C connects; Ctrl+Shift+R refreshes.";
        public Controller(IWpfTextView view, ITextDocument document, ICompletionBroker broker, IIntellisenseSessionStackMapService stacks)
        {
            this.view = view;
            this.document = document;
            this.broker = broker;
            guard = new CompletionGuard(view, broker, stacks);
            try
            {
                inlineDiagnostics = new InlineDiagnostics(view, (sql, issue) => Engine.DiagnosticFix(sql, issue, catalog, UserSettings.Current.UseBrackets), ApplyDiagnosticFix, ReviewDiagnostic, ConfigureDiagnostic);
            }
            catch (Exception ex) { Diagnostics.Write("Inline diagnostics initialization failed: " + ex); }
            starHint = new StarHint(view, () => catalog, () => CompletionOwnership.Enabled && !completionState.Dismissed && parameterStops.Count == 0, () => { popup.IsOpen = false; completionTimer.Stop(); });
            CompletionOwnership.Changed += CompletionModeChanged;
            UserSettings.Changed += SettingsChanged;
            if (view.HasAggregateFocus)
                Active = this;
            Diagnostics.Write("SqlPilot " + ProductInfo.Version + " attached to SQL editor.");
            popup.Child = new Border { Child = list, BorderThickness = new Thickness(1), BorderBrush = Brushes.SlateGray, Background = Brushes.White };
            popup.PlacementTarget = view.VisualElement;
            var scanPanel = new StackPanel();
            scanPanel.Children.Add(scanLabel);
            scanPanel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 3, Margin = new Thickness(0, 5, 0, 0) });
            scanPopup.Child = new Border { Child = scanPanel, Width = 220, Padding = new Thickness(10, 7, 10, 7), Background = new SolidColorBrush(Color.FromRgb(72, 39, 106)), CornerRadius = new CornerRadius(4) };
            scanPopup.PlacementTarget = view.VisualElement;
            ApplyAppearance();
            view.VisualElement.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown), true);
            view.VisualElement.LostKeyboardFocus += LostFocus;
            view.GotAggregateFocus += Focused;
            view.Caret.PositionChanged += CaretChanged;
            view.TextBuffer.Changed += Changed;
            view.Closed += Closed;
            completionTimer.Tick += (_, __) => { completionTimer.Stop(); if (view.HasAggregateFocus) ShowCompletion(false); };
            historyTimer.Tick += (_, __) => SaveHistory();
            historyTimer.Start();
            connectionTimer.Tick += async (_, __) => { if (view.HasAggregateFocus && !view.IsClosed) await Refresh(false); UpdateScanIndicator(); };
            connectionTimer.Start();
            list.PreviewMouseLeftButtonUp += (_, __) => Commit();
            if (view.HasAggregateFocus)
                view.VisualElement.Dispatcher.BeginInvoke(new Action(Activate));
        }
        void CaretChanged(object sender, CaretPositionChangedEventArgs e)
        {
            if (!replacing)
            {
                popup.IsOpen = false;
                if (parameterStops.Count > 0)
                {
                    var span = parameterStops[parameterIndex].GetSpan(view.TextBuffer.CurrentSnapshot);
                    int caret = view.Caret.Position.BufferPosition.Position;
                    if (caret < span.Start.Position || caret > span.End.Position)
                        parameterStops.Clear();
                }
            }
        }
        void LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!list.IsKeyboardFocusWithin)
                popup.IsOpen = false;
            scanPopup.IsOpen = false;
        }
        async void Focused(object sender, EventArgs args)
        {
            Active = this;
            Sessions.RememberActive();
            CompletionOwnership.TakeOver();
            guard.Dismiss();
            await Refresh(false);
        }
        internal void Activate() => Focused(this, EventArgs.Empty);
        void SettingsChanged(object sender, EventArgs e)
        {
            popup.IsOpen = false;
            ApplyAppearance();
            starHint.Invalidate();
        }
        void ApplyAppearance()
        {
            var settings = UserSettings.Current;
            bool dark = settings.Theme == "Dark" || (settings.Theme == "Auto" || settings.Theme == "Purple") && view.Background is SolidColorBrush b && (b.Color.R + b.Color.G + b.Color.B) < 384;
            list.FontFamily = new FontFamily("Segoe UI");
            list.FontSize = settings.FontSize;
            list.MinWidth = 260;
            list.MaxWidth = 460;
            list.MaxHeight = 210;
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.Background = dark ? new SolidColorBrush(Color.FromRgb(37, 28, 54)) : new SolidColorBrush(Color.FromRgb(252, 248, 255));
            list.Foreground = dark ? Brushes.WhiteSmoke : new SolidColorBrush(Color.FromRgb(30, 41, 59));
            ((Border)popup.Child).Background = list.Background;
            var row = new FrameworkElementFactory(typeof(DockPanel));
            var category = new FrameworkElementFactory(typeof(TextBlock));
            category.SetValue(FrameworkElement.WidthProperty, 52.0);
            category.SetValue(TextBlock.FontSizeProperty, 10.0);
            category.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            category.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Category"));
            var categoryColor = new System.Windows.Data.MultiBinding { Converter = new CategoryColor(dark) };
            categoryColor.Bindings.Add(new System.Windows.Data.Binding("Category"));
            categoryColor.Bindings.Add(new System.Windows.Data.Binding("IsSelected") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) });
            category.SetBinding(TextBlock.ForegroundProperty, categoryColor);
            row.AppendChild(category);
            var detail = new FrameworkElementFactory(typeof(TextBlock));
            detail.SetValue(DockPanel.DockProperty, Dock.Right);
            detail.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("Tag") { Source = list });
            detail.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Detail"));
            detail.SetValue(TextBlock.FontSizeProperty, (double)Math.Max(10, settings.FontSize - 1));
            detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            detail.SetValue(UIElement.OpacityProperty, 0.85);
            detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Detail"));
            row.AppendChild(detail);
            var label = new FrameworkElementFactory(typeof(MatchLabel));
            label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 8, 0));
            label.SetBinding(MatchLabel.CandidateProperty, new System.Windows.Data.Binding());
            row.AppendChild(label);
            list.ItemTemplate = new DataTemplate { VisualTree = row };
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Insert")));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            // Keep the selection readable while keyboard focus remains in the SQL editor.
            var container = new FrameworkElementFactory(typeof(Border));
            container.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            container.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            container.AppendChild(content);
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = container }));
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(111, 58, 175))));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(selected);
            list.ItemContainerStyle = style;
        }
        sealed class CategoryColor : System.Windows.Data.IMultiValueConverter
        {
            readonly bool dark;
            internal CategoryColor(bool dark)
            {
                this.dark = dark;
            }
            public object Convert(object[] values, Type target, object parameter, System.Globalization.CultureInfo culture)
            {
                if (values.Length > 1 && values[1] is bool selected && selected)
                    return Brushes.White;
                object value = values[0];
                string color = ((string)value).StartsWith("FK", StringComparison.Ordinal) ? (dark ? "#86EFAC" : "#166534") : (string)value == "Keyword" ? (dark ? "#C4B5FD" : "#6D28D9") : (string)value == "Column" ? (dark ? "#FCD34D" : "#854D0E") :
                    (string)value == "Snippet" ? (dark ? "#FDBA74" : "#9A3412") : (dark ? "#67E8F9" : "#0E7490");
                return new BrushConverter().ConvertFromString(color);
            }
            public object[] ConvertBack(object value, Type[] target, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
        }
        void CompletionModeChanged(object sender, EventArgs e)
        {
            popup.IsOpen = false;
            completionTimer.Stop();
            starHint.Invalidate();
            if (CompletionOwnership.Enabled)
                guard.Dismiss();
        }
        internal void ToggleCompletion() => CompletionOwnership.Toggle();
        internal void BeforeEditorInput()
        {
            guard.Dismiss();
            if (view.HasAggregateFocus)
                ObserveConnection();
        }
        void ObserveConnection()
        {
            _ = Refresh(false);
        }
        string ResolveConnection()
        {
            string host = ActiveConnection.TryGet();
            if (host != null && observedHostConnection != null && host != observedHostConnection)
                manualConnection = null;
            if (host != null)
                observedHostConnection = host;
            return manualConnection ?? host;
        }
        void UpdateScanIndicator()
        {
            scanPopup.HorizontalOffset = Math.Max(0, view.VisualElement.RenderSize.Width - 232);
            scanPopup.VerticalOffset = Math.Max(0, view.VisualElement.RenderSize.Height - 48);
            scanPopup.IsOpen = scanning && view.HasAggregateFocus && !view.IsClosed;
        }
        async Task Refresh(bool force)
        {
            if (view.IsClosed)
                return;
            string next = ResolveConnection();
            if (!force && next == connection && (scanning || retryAfter == DateTime.MinValue || DateTime.UtcNow < retryAfter))
                return;
            connection = next;
            catalog = new List<DbObject>();
            starHint.Invalidate();
            popup.IsOpen = false;
            parameterStops.Clear();
            scan?.Cancel();
            scan?.Dispose();
            scan = new CancellationTokenSource();
            int current = ++generation;
            retryAfter = DateTime.MinValue;
            if (next == null)
            {
                scanning = false;
                UpdateScanIndicator();
                status = "Active connection unavailable. Ctrl+Shift+C sets this tab's metadata connection.";
                return;
            }
            scanning = true;
            scanLabel.Text = "Scanning " + new System.Data.SqlClient.SqlConnectionStringBuilder(next).InitialCatalog + "…";
            UpdateScanIndicator();
            status = "Scanning database metadata…";
            try
            {
                metadataTask = SchemaReader.Scan(next, scan.Token);
                var result = await metadataTask;
                if (current == generation && !view.IsClosed)
                {
                    catalog = result;
                    starHint.Invalidate();
                    status = $"{catalog.Count} database objects loaded.";
                    if (view.HasAggregateFocus)
                        ShowCompletion(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                string error = ex.GetType().Name + (ex is System.Data.SqlClient.SqlException se ? ", SQL error " + se.Number : "");
                Diagnostics.Write("Metadata scan failed: " + error);
                if (current == generation)
                    status = "Metadata scan failed (" + error + "). Check connection, certificate and metadata permissions. Ctrl+Shift+C.";
                if (current == generation)
                    retryAfter = DateTime.UtcNow.AddSeconds(10);
            }
            finally { if (current == generation) { scanning = false; UpdateScanIndicator(); } }
        }
        internal async void RefreshCommand()
        {
            await Refresh(true);
            Notifications.Show(status);
        }
        void Changed(object sender, TextContentChangedEventArgs args)
        {
            dirty = true;
            if (!replacing)
                foreach (var change in args.Changes)
                    completionState.Edit(change.NewText);
            if (!replacing && UserSettings.Current.AutoConvert && args.Changes.Count == 1)
            {
                var change = args.Changes[0];
                if (change.NewLength == 1 && change.OldLength == 0 && char.IsWhiteSpace(change.NewText[0]))
                {
                    int expected = change.NewPosition + 1;
                    var version = args.After.Version;
                    view.VisualElement.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        if (view.IsClosed || view.TextBuffer.CurrentSnapshot.Version != version || view.Caret.Position.BufferPosition.Position != expected)
                            return;
                        var fix = Engine.AutoConversion(view.TextBuffer.CurrentSnapshot.GetText(), expected);
                        if (fix == null)
                            return;
                        replacing = true;
                        try
                        {
                            view.TextBuffer.Replace(new Span(fix.Offset, fix.Length), fix.Replacement);
                            view.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, expected + fix.Replacement.Length - fix.Length));
                        }
                        finally { replacing = false; }
                    }));
                }
            }
            guard.Dismiss();
            if (parameterStops.Count > 0)
                return;
            if (replacing || !CompletionOwnership.Enabled)
                return;
            popup.IsOpen = false;
            completionTimer.Stop();
            if (args.Changes.Any(c => (c.NewLength > 0 && c.NewLength <= 2) || (c.NewLength == 0 && c.OldLength <= 2)))
                completionTimer.Start();
        }
        void ShowCompletion(bool explicitRequest)
        {
            if (explicitRequest)
            {
                starHint.Dismiss();
            }
            if (view.HasAggregateFocus)
                ObserveConnection();
            if (!CompletionOwnership.Enabled || !view.Selection.IsEmpty)
            {
                popup.IsOpen = false;
                return;
            }
            var snapshot = view.TextBuffer.CurrentSnapshot;
            int caret = view.Caret.Position.BufferPosition.Position;
            string text = snapshot.GetText();
            if (!completionState.Allow(text, caret, explicitRequest))
            {
                popup.IsOpen = false;
                return;
            }
            guard.Dismiss();
            var request = Engine.RequestCompletion(text, caret, catalog, explicitRequest, UserSettings.Current.Options);
            replacementStart = request.Start;
            replacementLength = request.Length;
            var candidates = request.Candidates;
            list.ItemsSource = candidates;
            popup.IsOpen = candidates.Count > 0;
            if (!popup.IsOpen)
                return;
            var typeface = new Typeface(list.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Func<string, double> measure = value => new FormattedText(value ?? "", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, list.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(view.VisualElement).PixelsPerDip).WidthIncludingTrailingWhitespace;
            double detailWidth = Math.Max(40, Math.Min(124, candidates.Max(c => measure(c.Detail)) * 0.9 + 8));
            list.Tag = detailWidth;
            list.Width = Math.Max(260, Math.Min(460, 52 + detailWidth + 34 + candidates.Max(c => measure(c.Label))));
            list.MaxHeight = Math.Min(8, candidates.Count) * (list.FontSize * 1.4 + 4) + 6;
            if (UserSettings.Current.ExclusiveCompletion)
                broker.DismissAllSessions(view);
            list.SelectedIndex = 0;
            var line = view.Caret.ContainingTextViewLine;
            popup.HorizontalOffset = Math.Max(0, view.Caret.Left - view.ViewportLeft);
            popup.VerticalOffset = line.Bottom - view.ViewportTop;
        }
        void Commit()
        {
            ObserveConnection();
            if (!popup.IsOpen)
                return;
            if (!(list.SelectedItem is Candidate item))
                return;
            int caret = view.Caret.Position.BufferPosition.Position;
            if (caret < replacementStart)
            {
                popup.IsOpen = false;
                return;
            }
            int insertedAt = replacementStart;
            string following = view.TextBuffer.CurrentSnapshot.GetText(insertedAt + replacementLength, view.TextBuffer.CurrentSnapshot.Length - insertedAt - replacementLength);
            string accepted = Engine.AcceptedText(item.Insert, following);
            Replace(insertedAt, replacementLength, accepted);
            if (accepted == item.Insert && (following.StartsWith(" ") || following.StartsWith("\t")) && !char.IsWhiteSpace(accepted[accepted.Length - 1]))
                view.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, insertedAt + accepted.Length + 1));
            popup.IsOpen = false;
            view.VisualElement.Focus();
            parameterStops.Clear();
            var snapshot = view.TextBuffer.CurrentSnapshot;
            parameterStops.AddRange(item.Placeholders.Select(p => snapshot.CreateTrackingSpan(new Span(insertedAt + p.Offset, p.Length), SpanTrackingMode.EdgeInclusive)));
            if (parameterStops.Count > 0)
            {
                parameterIndex = 0;
                SelectParameter();
            }
            else if (item.Category == "Table" || item.Category == "FK Table" || item.Insert.Trim().Equals("ON", StringComparison.OrdinalIgnoreCase))
                ShowCompletion(false);
        }
        void SelectParameter()
        {
            var span = parameterStops[parameterIndex].GetSpan(view.TextBuffer.CurrentSnapshot);
            replacing = true;
            try
            {
                view.Caret.MoveTo(span.End);
                view.Selection.Select(span, false);
                view.Caret.EnsureVisible();
            }
            finally { replacing = false; }
        }
        bool NextParameter(bool backwards)
        {
            if (parameterStops.Count == 0)
                return false;
            int next = parameterIndex + (backwards ? -1 : 1);
            if (next < 0)
            {
                SelectParameter();
                return true;
            }
            if (next >= parameterStops.Count)
            {
                parameterStops.Clear();
                view.Selection.Clear();
                return true;
            }
            parameterIndex = next;
            SelectParameter();
            return true;
        }
        void Replace(int start, int length, string text)
        {
            replacing = true;
            try
            {
                view.TextBuffer.Replace(new Span(start, length), text);
                view.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, start + text.Length));
            }
            finally { replacing = false; }
        }
        internal void DismissCompletion()
        {
            completionState.Escape();
            popup.IsOpen = false;
            completionTimer.Stop();
            starHint.Dismiss();
        }
        internal void QuickFixCommand()
        {
            popup.IsOpen = false;
            completionTimer.Stop();
            starHint.Dismiss();
            inlineDiagnostics?.ShowAtCaret();
        }
        void ApplyDiagnosticFix(TextExpansion fix)
        {
            completionTimer.Stop();
            popup.IsOpen = false;
            guard.Dismiss();
            parameterStops.Clear();
            Replace(fix.Start, fix.Length, fix.Text);
            if (fix.CaretOffset >= 0)
            {
                view.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, fix.Start + fix.CaretOffset));
                completionState.Explicit();
            }
            view.VisualElement.Focus();
            view.Caret.EnsureVisible();
            if (fix.CaretOffset >= 0)
                ShowCompletion(true);
        }
        void ConfigureDiagnostic(Issue issue)
        {
            var fix = Engine.DiagnosticFix(view.TextBuffer.CurrentSnapshot.GetText(), issue, catalog, UserSettings.Current.UseBrackets);
            if (fix != null)
                ApplyDiagnosticFix(fix);
        }
        void ReviewDiagnostic(Issue issue)
        {
            var snapshot = view.TextBuffer.CurrentSnapshot;
            int start = Math.Max(0, Math.Min(snapshot.Length, issue.Offset));
            int length = Math.Max(0, Math.Min(issue.Length, snapshot.Length - start));
            view.Selection.Select(new SnapshotSpan(snapshot, start, length), false);
            view.Caret.MoveTo(new SnapshotPoint(snapshot, start));
            view.VisualElement.Focus();
            view.Caret.EnsureVisible();
        }
        bool ExpandStar()
        {
            if (!view.Selection.IsEmpty)
                return false;
            var expansion = Engine.ExpandStar(view.TextBuffer.CurrentSnapshot.GetText(), view.Caret.Position.BufferPosition.Position, catalog, UserSettings.Current.UseBrackets);
            if (expansion == null)
                return false;
            completionTimer.Stop();
            popup.IsOpen = false;
            guard.Dismiss();
            Replace(expansion.Start, expansion.Length, expansion.Text);
            return true;
        }
        void KeyDown(object sender, KeyEventArgs e)
        {
            if (view.HasAggregateFocus)
                ObserveConnection();
            string command = KeyboardShortcuts.Match(e);
            if (command != null)
            {
                e.Handled = true;
                MainMenu.Execute(command);
                return;
            }
            if (e.Key == Key.Escape)
                DismissCompletion();
            if (e.Handled)
                return;
            if (e.Key == Key.F12 && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Control) || e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift) && NextParameter(Keyboard.Modifiers == ModifierKeys.Shift))
            {
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && parameterStops.Count > 0)
            {
                parameterStops.Clear();
                view.Selection.Clear();
                e.Handled = true;
                return;
            }
            if (!CompletionOwnership.Enabled)
                return;
            if (NativeKeys || Keyboard.Modifiers != ModifierKeys.None)
                return;
            // Exact snippets always win over ordinary completion candidates.
            if (e.Key == Key.Tab && view.Selection.IsEmpty)
            {
                if (ExpandStar())
                {
                    e.Handled = true;
                    return;
                }
                string text = view.TextBuffer.CurrentSnapshot.GetText();
                int caret = view.Caret.Position.BufferPosition.Position;
                int start = Engine.WordStart(text, caret);
                string word = text.Substring(start, caret - start);
                if (Engine.IsCode(text, caret) && PersonalDialogs.Snippets.TryGetValue(word, out var snippet))
                {
                    e.Handled = true;
                    popup.IsOpen = false;
                    Replace(start, caret - start, snippet);
                    ShowCompletion(false);
                    return;
                }
            }
            if (!popup.IsOpen)
                return;
            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                e.Handled = true;
                list.SelectedIndex = (list.SelectedIndex + (e.Key == Key.Down ? 1 : list.Items.Count - 1)) % list.Items.Count;
                list.ScrollIntoView(list.SelectedItem);
            }
            else if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                e.Handled = true;
                Commit();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                popup.IsOpen = false;
            }
        }
        internal bool HandleEditorCommand(uint id)
        {
            if ((id == 4 || id == (uint)Microsoft.VisualStudio.VSConstants.VSStd2KCmdID.BACKTAB) && NextParameter(id != 4))
                return true;
            if (id == (uint)Microsoft.VisualStudio.VSConstants.VSStd2KCmdID.CANCEL && parameterStops.Count > 0)
            {
                DismissCompletion();
                parameterStops.Clear();
                view.Selection.Clear();
                return true;
            }
            if (id == (uint)Microsoft.VisualStudio.VSConstants.VSStd2KCmdID.CANCEL)
            {
                bool wasOpen = popup.IsOpen;
                completionState.Escape();
                popup.IsOpen = false;
                completionTimer.Stop();
                starHint.Dismiss();
                return wasOpen;
            }
            if (!CompletionOwnership.Enabled)
                return false;
            guard.Dismiss();
            if (NativeFilter.IsCompletionCommand(id))
            {
                ShowCompletion(true);
                return true;
            }
            if (id == 4 && view.Selection.IsEmpty)
            {
                if (ExpandStar())
                    return true;
                string text = view.TextBuffer.CurrentSnapshot.GetText();
                int caret = view.Caret.Position.BufferPosition.Position;
                int start = Engine.WordStart(text, caret);
                if (Engine.IsCode(text, caret) && PersonalDialogs.Snippets.TryGetValue(text.Substring(start, caret - start), out var snippet))
                {
                    completionTimer.Stop();
                    popup.IsOpen = false;
                    broker.DismissAllSessions(view);
                    Replace(start, caret - start, snippet);
                    ShowCompletion(false);
                    return true;
                }
            }
            // Resolve an immediately typed prefix before the 60 ms popup timer fires.
            if ((id == 3 || id == 4) && completionTimer.IsEnabled)
            {
                completionTimer.Stop();
                ShowCompletion(false);
            }
            if (!popup.IsOpen)
                return false;
            if (id == 11 || id == 13)
            {
                list.SelectedIndex = (list.SelectedIndex + (id == 13 ? 1 : list.Items.Count - 1)) % list.Items.Count;
                list.ScrollIntoView(list.SelectedItem);
                return true;
            }
            if (id == 3 || id == 4)
            {
                Commit();
                return true;
            }
            if (id == (uint)Microsoft.VisualStudio.VSConstants.VSStd2KCmdID.CANCEL)
            {
                popup.IsOpen = false;
                completionTimer.Stop();
                return true;
            }
            return false;
        }
        void SaveHistory()
        {
            if (!dirty)
                return;
            try
            {
                History.Save(tab, document?.FilePath ?? "Unsaved SQL tab", view.TextBuffer.CurrentSnapshot.GetText());
                dirty = false;
            }
            catch { status = "History could not be saved. Check LocalAppData permissions and disk space."; }
        }
        internal void SaveToLibrary(Action<string> open)
        {
            string sql = view.Selection.IsEmpty ? view.TextBuffer.CurrentSnapshot.GetText() : view.Selection.SelectedSpans[0].GetText();
            PersonalDialogs.ShowLibrary(sql, document == null ? "Untitled query" : System.IO.Path.GetFileNameWithoutExtension(document.FilePath), InsertLibrarySql, open);
        }
        bool navigating;
        internal async void NavigateReference(bool explorer)
        {
            if (navigating)
                return;
            navigating = true;
            popup.IsOpen = false;
            try
            {
                string current = ResolveConnection();
                if (current == null)
                    throw new InvalidOperationException("Connect this SQL tab to a database first.");
                var options = new System.Data.SqlClient.SqlConnectionStringBuilder(current);
                string referenceSql = view.TextBuffer.CurrentSnapshot.GetText();
                int referenceCaret = view.Caret.Position.BufferPosition.Position;
                var objects = current == connection && catalog.Count > 0 ? catalog : null;
                if (objects == null)
                {
                    Notifications.Show("Loading metadata for reference navigation…");
                    objects = current == connection && scanning && metadataTask != null ? await metadataTask : await SchemaReader.Scan(current, CancellationToken.None);
                    if (current == ResolveConnection())
                    {
                        connection = current;
                        catalog = objects;
                        starHint.Invalidate();
                    }
                }
                if (view.IsClosed || view.TextBuffer.CurrentSnapshot.GetText() != referenceSql || ResolveConnection() != current)
                    return;
                var targets = Engine.ReferenceAt(referenceSql, referenceCaret, objects, options.InitialCatalog);
                if (targets.Count == 0)
                    throw new InvalidOperationException("Place the cursor on a database object name or table alias.");
                DbObject target = targets[0];
                if (targets.Count > 1)
                {
                    var choices = new ListBox { ItemsSource = targets.Select(t => t.Qualified + " · " + t.Kind).ToList(), SelectedIndex = 0, MinHeight = 130 };
                    var panel = new StackPanel { Margin = new Thickness(12) };
                    panel.Children.Add(new TextBlock { Text = "Choose the referenced object:" });
                    panel.Children.Add(choices);
                    var window = Dialog("SqlPilot · Reference", panel);
                    var select = new Button { Content = "Open", IsDefault = true, Margin = new Thickness(0, 10, 0, 0) };
                    panel.Children.Add(select);
                    select.Click += (_, __) => window.DialogResult = true;
                    if (window.ShowDialog() != true)
                        return;
                    target = targets[choices.SelectedIndex];
                }
                bool table = target.Kind == "USER_TABLE" || string.Equals(target.Kind, "table", StringComparison.OrdinalIgnoreCase);
                if (explorer || table)
                    ReferenceNavigation.Explorer(current, target);
                else
                    ReferenceNavigation.OpenModify(await SchemaReader.ModifyScript(current, target, CancellationToken.None), target);
            }
            catch (Exception ex) { MessageBox.Show(ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException.Message : ex.Message, "SqlPilot · Reference"); }
            finally { navigating = false; }
        }
        internal void InsertLibrarySql(string sql)
        {
            if (view.IsClosed)
                throw new InvalidOperationException("The target SQL tab was closed.");
            int start = view.Selection.IsEmpty ? view.Caret.Position.BufferPosition.Position : view.Selection.Start.Position.Position;
            int length = view.Selection.IsEmpty ? 0 : view.Selection.End.Position.Position - start;
            Replace(start, length, sql);
            view.VisualElement.Focus();
        }
        void Closed(object sender, EventArgs e)
        {
            CompletionOwnership.Changed -= CompletionModeChanged;
            UserSettings.Changed -= SettingsChanged;
            starHint.Dispose();
            inlineDiagnostics?.Dispose();
            guard.Dispose();
            if (Active == this)
                Active = null;
            SaveHistory();
            historyTimer.Stop();
            completionTimer.Stop();
            connectionTimer.Stop();
            scan?.Cancel();
            scan?.Dispose();
            popup.IsOpen = false;
            scanPopup.IsOpen = false;
            parameterStops.Clear();
            view.VisualElement.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown));
            view.VisualElement.LostKeyboardFocus -= LostFocus;
            view.GotAggregateFocus -= Focused;
            view.TextBuffer.Changed -= Changed;
            view.Caret.PositionChanged -= CaretChanged;
            view.Closed -= Closed;
        }
        internal async void Connect()
        {
            var input = new TextBox { Margin = new Thickness(10), MinWidth = 550 };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "Metadata connection for this tab only (kept in memory).\nUse the same server and database as the SSMS tab.\nExample: Server=localhost;Database=MyDb;Integrated Security=true;Encrypt=true;", Margin = new Thickness(10) });
            panel.Children.Add(input);
            var button = new Button { Content = "Connect and scan", Margin = new Thickness(10), IsDefault = true };
            panel.Children.Add(button);
            var window = Dialog("SqlPilot · Connection", panel);
            button.Click += (_, __) => { try { var b = new System.Data.SqlClient.SqlConnectionStringBuilder(input.Text); if (string.IsNullOrWhiteSpace(b.InitialCatalog)) throw new Exception("Specify Database."); b.ConnectTimeout = 8; manualConnection = b.ConnectionString; window.DialogResult = true; } catch (Exception ex) { MessageBox.Show(ex.Message, "Connection settings"); } };
            if (window.ShowDialog() == true)
            {
                await Refresh(true);
                Notifications.Show(status);
            }
            input.Clear();
        }
        internal void Format()
        {
            try
            {
                var selection = view.Selection;
                int start = selection.IsEmpty ? 0 : selection.Start.Position.Position;
                int length = selection.IsEmpty ? view.TextBuffer.CurrentSnapshot.Length : selection.End.Position.Position - start;
                Replace(start, length, Engine.Format(view.TextBuffer.CurrentSnapshot.GetText(start, length)));
            }
            catch (Exception ex) { Notifications.Show("SQL formatting could not be completed: " + ex.Message); }
        }
        internal void Help()
        {
            MainMenu.Help(this);
        }
        internal void AnalyzeDialog()
        {
            string original = view.TextBuffer.CurrentSnapshot.GetText();
            var issues = Engine.Analyze(original);
            var box = new ListBox { ItemsSource = issues, Margin = new Thickness(10) };
            var panel = new DockPanel();
            var footer = new StackPanel { Margin = new Thickness(10) };
            var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 680 };
            var button = new Button { Content = "Apply selected fix", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            var review = new Button { Content = "Review in editor", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            footer.Children.Add(hint);
            footer.Children.Add(button);
            footer.Children.Add(review);
            DockPanel.SetDock(footer, Dock.Bottom);
            panel.Children.Add(footer);
            panel.Children.Add(box);
            var window = Dialog($"SqlPilot · {issues.Count} findings", panel);
            box.SelectionChanged += (_, __) =>
            {
                var issue = box.SelectedItem as Issue;
                review.IsEnabled = issue != null;
                var fix = issue == null ? null : Engine.DiagnosticFix(original, issue, catalog, UserSettings.Current.UseBrackets);
                button.IsEnabled = fix != null || Engine.CanConfigureDiagnosticFix(issue);
                button.Content = fix != null ? "Apply selected fix" : "Configure selected fix…";
                hint.Text = issue == null ? "Select a finding to review its available correction." : fix == null ? Engine.DiagnosticFixHint(issue) : "Replacement preview:\n" + fix.Text;
            };
            button.Click += (_, __) =>
            {
                if (!(box.SelectedItem is Issue issue))
                    return;
                if (view.TextBuffer.CurrentSnapshot.GetText() != original)
                {
                    MessageBox.Show("The query changed. Run analysis again.", "SqlPilot");
                    return;
                }
                try
                {
                    var fix = Engine.DiagnosticFix(original, issue, catalog, UserSettings.Current.UseBrackets);
                    if (fix == null)
                        return;
                    ApplyDiagnosticFix(fix);
                    original = view.TextBuffer.CurrentSnapshot.GetText();
                    box.ItemsSource = Engine.Analyze(original);
                    button.IsEnabled = false;
                    hint.Text = "Fix applied. Select another finding to continue.";
                }
                catch (Exception ex) { MessageBox.Show("The fix could not be applied: " + ex.Message, "SqlPilot"); }
            };
            review.Click += (_, __) => { if (box.SelectedItem is Issue issue && view.TextBuffer.CurrentSnapshot.GetText() == original) { window.Close(); ReviewDiagnostic(issue); } };
            if (issues.Count > 0)
                box.SelectedIndex = 0;
            else
                hint.Text = "No findings in this SQL query.";
            window.ShowDialog();
        }
        internal void HistoryDialog()
        {
            SaveHistory();
            var entries = History.Load();
            var search = new TextBox { Margin = new Thickness(8) };
            var box = new ListBox { ItemsSource = entries, Margin = new Thickness(8), Height = 150 };
            var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(8) };
            var restore = new Button { Content = "Insert selected snapshot at cursor", Margin = new Thickness(8) };
            var panel = new DockPanel();
            foreach (var element in new UIElement[] { search, box, restore })
            {
                DockPanel.SetDock(element, Dock.Top);
                panel.Children.Add(element);
            }
            panel.Children.Add(preview);
            search.TextChanged += (_, __) => box.ItemsSource = entries.Where(x => (x.File + "\n" + x.Sql).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            box.SelectionChanged += (_, __) => preview.Text = (box.SelectedItem as HistoryEntry)?.Sql ?? "";
            var window = Dialog("SqlPilot · Query history (last 30 days)", panel);
            restore.Click += (_, __) => { if (box.SelectedItem is HistoryEntry entry) { Replace(view.Caret.Position.BufferPosition.Position, 0, entry.Sql); window.Close(); } };
            window.ShowDialog();
        }
        Window Dialog(string title, UIElement content)
        {
            var w = SqlPilot.UI.Design.Window(title, content, 850, 620);
            w.Icon = Brand.Icon();
            w.Owner = Window.GetWindow(view.VisualElement);
            return w;
        }
    }
}
