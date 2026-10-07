// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using SqlPilot.UI;
namespace SqlPilot.Setup;
public sealed class HostChoice : INotifyPropertyChanged
{
    public Host Host
    {
        get;
    }
    public string Name => Host.Name; public string Path => Host.Ide; public string InstalledVersion => Host.InstalledVersion; public string Operation => Host.Operation;
    bool selected; string result; bool canOpen; bool canSelect = true;
    public bool CanOpen
    {
        get => canOpen; set
        {
            canOpen = value;
            Changed(nameof(CanOpen));
        }
    }
    public bool CanSelect
    {
        get => canSelect; set
        {
            canSelect = value;
            Changed(nameof(CanSelect));
        }
    }
    public bool Selected
    {
        get => selected; set
        {
            selected = value;
            Changed(nameof(Selected));
        }
    }
    public string Result
    {
        get => result; set
        {
            result = value;
            Changed(nameof(Result));
        }
    }
    public void RefreshVersion()
    {
        Changed(nameof(InstalledVersion));
        Changed(nameof(Operation));
    }
    public HostChoice(Host host)
    {
        Host = host;
        selected = host.Complete && File.Exists(host.Executable) && host.Operation != "Downgrade";
        result = host.Status;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public static class HostSelection
{
    public static string Encode(IEnumerable<string> paths) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(paths.ToArray())));
    public static HashSet<string>? Decode(string[] args)
    {
        int index = Array.IndexOf(args, "--selection");
        if (index < 0)
            return null;
        if (index + 1 >= args.Length)
            throw new ArgumentException("Missing selected SSMS versions.");
        return new HashSet<string>(JsonSerializer.Deserialize<string[]>(Encoding.UTF8.GetString(Convert.FromBase64String(args[index + 1]))) ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }
}
sealed class SetupWindow : Window
{
    readonly ObservableCollection<HostChoice> hosts = new();
    readonly DataGrid grid = new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserResizeRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, MinRowHeight = 74, ColumnHeaderHeight = 38, GridLinesVisibility = DataGridGridLinesVisibility.None, Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = DataGridSelectionMode.Single, MinHeight = 160 };
    readonly Button install = Design.Primary("Install selected components");
    readonly Button cancel = new() { Content = "Cancel pending", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 10, 0) };
    readonly List<Host> ready = new();
    readonly Dictionary<HostChoice, string> pending = new();
    readonly System.Windows.Threading.DispatcherTimer monitor = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Button refresh = new() { Content = "Rescan", Margin = new Thickness(0, 0, 10, 0) };
    readonly TextBlock summary = new() { Text = "Finding installed SSMS versions…", TextWrapping = TextWrapping.Wrap, Foreground = Design.Muted };
    readonly TextBlock guidance = new() { Text = "SSMS can stay open. Updates are staged and applied after you save your queries and close that version.", Foreground = Design.Muted, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock result = new() { Text = "Select where SqlPilot should be installed.", TextWrapping = TextWrapping.Wrap, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Design.Ink };
    readonly TextBox log = new() { IsReadOnly = true, AcceptsReturn = true, Height = 116, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, FontSize = 11, Background = Brushes.Transparent };
    readonly ProgressBar progress = new() { Height = 4, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 12) };
    readonly Installer engine; readonly string[] arguments; bool busy; bool allowDowngrade;
    public SetupWindow(string[] args)
    {
        arguments = args;
        allowDowngrade = args.Contains("--allow-downgrade");
        engine = new Installer();
        Title = "SqlPilot Setup";
        Width = 960;
        Height = 740;
        MinWidth = 800;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Foreground = Design.Ink;
        Icon = BitmapFrame.Create(new Uri(System.IO.Path.Combine(engine.Root, "assets", "sqlpilot.ico")), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition());
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var intro = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        intro.Children.Add(result);
        intro.Children.Add(guidance);
        panel.Children.Add(intro);
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        check.SetBinding(UIElement.IsEnabledProperty, new Binding("CanSelect"));
        check.SetBinding(CheckBox.IsCheckedProperty, new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "Install", CellTemplate = new DataTemplate { VisualTree = check }, Width = 65 });
        grid.Columns.Add(new DataGridTextColumn { Header = "SSMS version", Binding = new Binding("Name"), Width = 150, IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = "Installed SqlPilot", Binding = new Binding("InstalledVersion"), Width = 130, IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = "Installation folder", Binding = new Binding("Path"), MinWidth = 180, Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Result"), Width = 140, IsReadOnly = true });
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(7, 8, 7, 8)));
        textStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0));
        foreach (var c in grid.Columns.OfType<DataGridTextColumn>())
            c.ElementStyle = textStyle;
        var statusStyle = new Style(typeof(TextBlock), textStyle);
        var downgradeStyle = new DataTrigger { Binding = new Binding("Operation"), Value = "Downgrade" };
        downgradeStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(163, 88, 15))));
        downgradeStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        statusStyle.Triggers.Add(downgradeStyle);
        ((DataGridTextColumn)grid.Columns.Last()).ElementStyle = statusStyle;
        var launch = new FrameworkElementFactory(typeof(Button));
        launch.SetValue(ContentControl.ContentProperty, "Open SSMS");
        launch.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
        launch.SetValue(Control.PaddingProperty, new Thickness(10, 8, 10, 8));
        launch.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        launch.SetBinding(UIElement.IsEnabledProperty, new Binding("CanOpen"));
        launch.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, e) =>
        {
            if (((FrameworkElement)sender).DataContext is HostChoice choice && choice.CanOpen)
                Launch(choice.Host);
        }));
        grid.Columns.Add(new DataGridTemplateColumn { Header = "Launch", CellTemplate = new DataTemplate { VisualTree = launch }, Width = 118 });
        grid.ItemsSource = hosts;
        Grid.SetRow(grid, 1);
        panel.Children.Add(grid);
        Grid.SetRow(progress, 3);
        panel.Children.Add(progress);
        var details = new Expander { Header = "Installation details", Content = log, Margin = new Thickness(0, 8, 0, 12) };
        Grid.SetRow(details, 4);
        panel.Children.Add(details);
        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(summary);
        summary.VerticalAlignment = VerticalAlignment.Center;
        summary.Margin = new Thickness(0, 0, 16, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(cancel);
        actions.Children.Add(refresh);
        actions.Children.Add(install);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 5);
        panel.Children.Add(footer);
        Design.Apply(this, "A better SQL workspace", panel, "SqlPilot " + InstalledVersions.Current + " · " + ProductInfo.Author + " · Completion and SQL Library");
        install.Click += async (_, __) => { if ((string)install.Content == "Done") Close(); else await InstallSelected(); };
        cancel.Click += (_, __) => { foreach (var job in pending.Values) File.WriteAllText(job + ".cancel", "cancel"); cancel.IsEnabled = false; summary.Text = "Cancellation requested for updates still waiting for SSMS."; };
        monitor.Tick += (_, __) => CheckPending();
        refresh.Click += async (_, __) => await Scan();
        Closed += (_, __) => { monitor.Stop(); engine.Dispose(); };
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Loaded += async (_, __) => { await Scan(); if (args.Contains("--install")) await InstallSelected(); };
    }
    void Attention(string message)
    {
        summary.Text = message;
        summary.Foreground = new SolidColorBrush(Color.FromRgb(190, 38, 54));
        summary.FontWeight = FontWeights.SemiBold;
        var motion = summary.RenderTransform as TranslateTransform ?? new TranslateTransform();
        summary.RenderTransform = motion;
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(330), FillBehavior = FillBehavior.Stop };
        foreach (var frame in new[] { (0, 0.0), (55, -3.0), (110, 3.0), (165, -2.0), (220, 2.0), (330, 0.0) })
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(frame.Item2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(frame.Item1))));
        motion.BeginAnimation(TranslateTransform.XProperty, shake);
    }
    void ClearAttention()
    {
        summary.Foreground = Design.Muted;
        summary.FontWeight = FontWeights.Normal;
        (summary.RenderTransform as TranslateTransform)?.BeginAnimation(TranslateTransform.XProperty, null);
    }
    void Report(string message)
    {
        log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        log.ScrollToEnd();
    }
    async Task Scan()
    {
        ClearAttention();
        busy = true;
        install.IsEnabled = false;
        refresh.IsEnabled = false;
        grid.IsEnabled = true;
        guidance.Text = "SSMS can stay open. Updates are staged and applied after you save your queries and close that version.";
        progress.Visibility = Visibility.Visible;
        try
        {
            var selected = hosts.Count > 0 ? new HashSet<string>(hosts.Where(h => h.Selected).Select(h => h.Host.Ide), StringComparer.OrdinalIgnoreCase) : HostSelection.Decode(arguments);
            var found = await Detection.Find();
            hosts.Clear();
            install.Content = "Install selected components";
            result.Text = "Select where SqlPilot should be installed.";
            result.Foreground = Design.Ink;
            foreach (var host in found)
            {
                var choice = new HostChoice(host) { CanOpen = ready.Any(h => h.Ide.Equals(host.Ide, StringComparison.OrdinalIgnoreCase)) };
                if (selected != null)
                    choice.Selected = selected.Contains(host.Ide);
                hosts.Add(choice);
            }
            summary.Text = hosts.Count + " SSMS version(s) detected. Choose one or more.";
            Report(summary.Text);
        }
        catch (Exception ex) { summary.Text = "Detection failed. Open Installation details for more information."; Report(ex.Message); }
        finally { busy = false; install.IsEnabled = hosts.Count > 0; refresh.IsEnabled = true; progress.Visibility = Visibility.Collapsed; }
    }
    async Task InstallSelected()
    {
        ClearAttention();
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = hosts.Where(h => h.Selected && h.CanSelect).ToList();
        if (selected.Count == 0)
        {
            summary.Text = "Select an SSMS version to continue.";
            return;
        }
        foreach (var item in selected)
            InstalledVersions.Refresh(item.Host);
        var downgrades = selected.Where(h => h.Host.Operation == "Downgrade").ToList();
        if (downgrades.Count > 0 && !allowDowngrade)
        {
            string message = "A newer SqlPilot version is already installed:\n\n" + string.Join("\n", downgrades.Select(h => h.Name + ": " + h.Host.InstalledVersion + " → " + InstalledVersions.Current)) + "\n\nDowngrading may remove features or fixes. Do you want to install the older version?";
            if (MessageBox.Show(this, message, "Confirm SqlPilot downgrade", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                summary.Text = "Downgrade cancelled. No changes were made.";
                return;
            }
            allowDowngrade = true;
        }
        if (!Program.Elevated())
        {
            try
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                start.ArgumentList.Add("--install");
                if (allowDowngrade)
                    start.ArgumentList.Add("--allow-downgrade");
                start.ArgumentList.Add("--selection");
                start.ArgumentList.Add(HostSelection.Encode(selected.Select(h => h.Host.Ide)));
                Process.Start(start);
                Close();
            }
            catch (Win32Exception) { summary.Text = "SSMS administrator access was not granted." + " No changes were made."; }
            return;
        }
        busy = true;
        install.IsEnabled = false;
        refresh.IsEnabled = false;
        grid.IsEnabled = false;
        progress.Visibility = Visibility.Visible;
        int succeeded = 0;
        try
        {
            foreach (var item in selected)
            {
                item.CanOpen = false;
                item.Result = "Installing…";
                result.Text = "Installing SqlPilot on " + item.Name + "…";
                try
                {
                    if (Detection.Running(item.Host))
                    {
                        result.Text = "Preparing update for " + item.Name + "…";
                        await Task.Run(() => engine.Prepare(item.Host));
                        pending[item] = DeferredInstall.Queue(item.Host, allowDowngrade);
                        item.CanSelect = false;
                        item.CanOpen = false;
                        item.Result = "Prepared · restart required";
                        monitor.Start();
                        cancel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        item.Result = await engine.Install(item.Host, Report, allowDowngrade);
                        ready.Add(item.Host);
                        item.CanOpen = true;
                        item.CanSelect = false;
                        item.RefreshVersion();
                    }
                    succeeded++;
                    Report(item.Name + ": " + item.Result);
                }
                catch (Exception ex) { item.Result = "Failed"; Report(item.Name + ": " + ex.Message); }
            }
            result.Text = succeeded == selected.Count ? (pending.Count > 0 ? "Updates prepared · restart required" : "Installation complete") : succeeded == 0 ? "Installation failed" : "Installation completed with issues";
            guidance.Text = pending.Count > 0 ? "Save your queries and close SSMS normally. Setup will apply the update in the background; wait for completion before reopening. Keep Windows running until it finishes." : succeeded > 0 ? "Use Open SSMS in the corresponding row, then open a SQL query tab and the SqlPilot menu." : "Review the installation details below, then retry.";
            result.Foreground = succeeded == selected.Count ? new SolidColorBrush(Color.FromRgb(20, 107, 78)) : new SolidColorBrush(Color.FromRgb(153, 61, 26));
            summary.Text = $"Accepted {succeeded} of {selected.Count} selected version(s). " + (pending.Count > 0 ? "Background setup is waiting for SSMS to close." : succeeded > 0 ? "Installation complete. Open SSMS from its row." : "Open Installation details and try again.");
            Report(summary.Text);
            install.Content = pending.Count > 0 || succeeded == selected.Count ? "Done" : "Retry selected versions";
            if (succeeded == selected.Count)
                install.IsDefault = true;
        }
        finally { busy = false; install.IsEnabled = true; refresh.IsEnabled = pending.Count == 0; grid.IsEnabled = true; progress.Visibility = Visibility.Collapsed; }
    }
    void Launch(Host host)
    {
        try
        {
            if (Detection.Running(host))
            {
                summary.Text = host.Name + " is already open.";
                return;
            }
            // Existing Explorer handles this request instead of inheriting setup's elevated token.
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(host.Executable);
            using var process = Process.Start(start) ?? throw new Exception("Windows shell could not start SSMS.");
        }
        catch (Exception ex) { Attention("Could not open SSMS: " + ex.Message); }
    }
    void CheckPending()
    {
        foreach (var entry in pending.ToArray())
        {
            var status = DeferredInstall.Read(entry.Value);
            if (status == null)
                continue;
            if (!status.Finished)
            {
                if (status.Message.StartsWith("Applying"))
                    entry.Key.Result = "Applying · keep SSMS closed";
                continue;
            }
            entry.Key.Result = status.Success ? "Installed · ready to open" : status.Message;
            Report(entry.Key.Name + ": " + status.Message);
            if (status.Success)
            {
                ready.Add(entry.Key.Host);
                InstalledVersions.Refresh(entry.Key.Host);
                entry.Key.RefreshVersion();
                entry.Key.CanOpen = true;
            }
            entry.Key.CanSelect = !status.Success;
            pending.Remove(entry.Key);
        }
        if (pending.Count > 0)
            return;
        monitor.Stop();
        cancel.Visibility = Visibility.Collapsed;
        refresh.IsEnabled = true;
        result.Text = ready.Count > 0 ? "Background installation finished" : "Review installation details";
        guidance.Text = "Use Open SSMS for completed versions. Failed or cancelled updates can be retried with Rescan.";
        summary.Text = "Background updates finished. See each version's status.";
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
    internal static int Checks(string? preview)
    {
        var window = new SetupWindow(Array.Empty<string>());
        if (window.cancel.Visibility != Visibility.Collapsed || new HostChoice(new Host()).CanOpen)
            throw new Exception("Setup actions visible before installation.");
        int passed = 1;
        window.result.Text = "Installation complete";
        window.guidance.Text = "Use Open SSMS in the corresponding row, then open a SQL query tab and the SqlPilot menu.";
        window.summary.Text = "Installed on 2 selected versions.";
        window.install.Content = "Done";
        foreach (int major in new[] { 20, 22 })
            window.hosts.Add(new HostChoice(new Host { Name = "SSMS " + major, Ide = @"C:\Example\SSMS" + major, Major = major, InstalledCopies = new() { new InstalledCopy { Folder = @"C:\Example\Extension", Version = InstalledVersions.Current, AllUsers = true } } }) { Selected = true, CanOpen = major == 20, CanSelect = false, Result = major == 20 ? "Installed · ready to open" : "Prepared · restart required" });
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        root.Resources = window.Resources;
        System.Windows.Documents.TextElement.SetFontFamily(root, window.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(root, window.FontSize);
        System.Windows.Documents.TextElement.SetForeground(root, window.Foreground);
        var surface = new Border { Background = window.Background, Child = root, Width = 960, Height = 700 };
        surface.Measure(new Size(960, 700));
        surface.Arrange(new Rect(0, 0, 960, 700));
        surface.UpdateLayout();
        var launchButtons = Descendants(surface).OfType<Button>().Where(b => Equals(b.Content, "Open SSMS")).ToList();
        if (launchButtons.Count != 2 || !launchButtons[0].IsEnabled || launchButtons[1].IsEnabled || launchButtons.Any(b => b.ActualWidth < 70) || window.install.ActualWidth < 40)
            throw new Exception("Completion actions have no visible layout.");
        passed++;
        window.hosts[1].CanOpen = true;
        surface.UpdateLayout();
        if (!launchButtons[1].IsEnabled)
            throw new Exception("Completed background row did not enable Open SSMS.");
        passed++;
        window.hosts[1].Result = "Installed · ready to open";
        surface.UpdateLayout();
        foreach (var button in launchButtons)
        {
            var label = new FormattedText((string)button.Content, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize, button.Foreground, 1);
            if (button.ActualWidth < label.WidthIncludingTrailingWhitespace + button.Padding.Left + button.Padding.Right + 2)
                throw new Exception("Open SSMS label is clipped.");
        }
        passed++;
        if (preview != null)
        {
            var bitmap = new RenderTargetBitmap(960, 700, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(preview);
            encoder.Save(file);
        }
        window.engine.Dispose();
        return passed;
    }

}
