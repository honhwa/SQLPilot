using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SqlPilot;
using SqlPilot.Ssms;

internal static class AboutChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var content = AboutDialog.CreateContent("Test metadata loaded.");
        var blocks = content.Children.OfType<TextBlock>().ToArray();
        var links = blocks.SelectMany(b => b.Inlines.OfType<Hyperlink>()).ToArray();
        check(blocks.Any(b => b.Text == ProductInfo.Author), "About displays the author");
        check(links.Any(l => l.NavigateUri.AbsoluteUri == ProductInfo.TelegramUrl), "About links to the author's Telegram");
        check(links.Any(l => l.NavigateUri.AbsoluteUri == ProductInfo.RepositoryUrl), "About links to the official repository");
        check(blocks.Any(b => b.Text.Contains(ProductInfo.LicenseName)), "About displays the usage license");
        check(ProductInfo.Version == typeof(AboutChecks).Assembly.GetName().Version.ToString(3), "product version follows build metadata");

        // Optional offscreen artifact rendering; no user application is controlled.
        string folder = Environment.GetEnvironmentVariable("SQLPILOT_TEST_ARTIFACTS");
        if (string.IsNullOrEmpty(folder))
            return;
        Directory.CreateDirectory(folder);
        var window = SqlPilot.UI.Design.Window("About SqlPilot", new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 700, 610, "Version " + ProductInfo.Version);
        var visual = (FrameworkElement)window.Content;
        visual.Measure(new Size(700, 610));
        visual.Arrange(new Rect(0, 0, 700, 610));
        visual.UpdateLayout();
        var backdrop = new DrawingVisual();
        using (var drawing = backdrop.RenderOpen())
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 700, 610));
        var bitmap = new RenderTargetBitmap(700, 610, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(backdrop);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(folder, "about.png")))
            png.Save(file);
    }
}
