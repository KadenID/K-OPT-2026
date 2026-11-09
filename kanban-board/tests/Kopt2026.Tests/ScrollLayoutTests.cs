using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kopt2026.App;
using Kopt2026.Persistence;

namespace Kopt2026.Tests;

public sealed class ScrollLayoutTests
{
    [AvaloniaTheory]
    [InlineData(920, 640, 1)]
    [InlineData(1240, 800, 1)]
    [InlineData(920, 640, 2)]
    [InlineData(1240, 800, 2)]
    public async Task Editor_Scrollbars_Stay_At_The_Edges_Without_Covering_Text(int width, int height, double scaling)
    {
        var workspace = new DesktopWorkspace(new TestDialogs(), new UserSettingsStore(Path.Combine(Path.GetTempPath(), "kopt-scroll-" + Guid.NewGuid() + ".json")));
        var window = new MainWindow(workspace) { Width = width, Height = height };
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            window.SetRenderScaling(scaling);
            foreach (var editor in new[] { window.ModelEditor, window.ResultOutput })
            {
                var longText = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"행 {i:D3}: " + new string('X', 160))) + "\n" + new string('X', 160) + " 마지막 행 끝";
                editor.Text = longText;
                using var expanded = window.CaptureRenderedFrame();
                var scroll = Assert.Single(editor.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "PART_ScrollViewer");
                var viewport = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollContentPresenter>());
                var horizontal = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollBar>(), b => b.Orientation == Avalonia.Layout.Orientation.Horizontal);
                var vertical = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollBar>(), b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
                Assert.True(horizontal.IsVisible && vertical.IsVisible);
                var v = vertical.TranslatePoint(default, editor)!.Value;
                var h = horizontal.TranslatePoint(default, editor)!.Value;
                Assert.InRange(editor.Bounds.Width - (v.X + vertical.Bounds.Width), 0, 1);
                Assert.InRange(editor.Bounds.Height - (h.Y + horizontal.Bounds.Height), 0, 1);
                var content = viewport.TranslatePoint(default, editor)!.Value;
                Assert.InRange(v.X - (content.X + viewport.Bounds.Width), 5.5, 6.5);
                Assert.InRange(h.Y - (content.Y + viewport.Bounds.Height), 5.5, 6.5);
                scroll.Offset = new Vector(scroll.Extent.Width, scroll.Extent.Height);
                using var bottom = window.CaptureRenderedFrame();
                var presenter = Assert.Single(editor.GetVisualDescendants().OfType<TextPresenter>());
                var last = Assert.Single(presenter.TextLayout.HitTestTextRange(longText.Length - 1, 1));
                var bounds = last.TransformToAABB(presenter.TransformToVisual(viewport)!.Value);
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= viewport.Bounds.Height + 0.1, "The last line has insufficient bottom padding.");
                Assert.True(bounds.Left >= 0 && bounds.Right <= viewport.Bounds.Width + 0.1, "The last character has insufficient right padding.");
                editor.Text = "한글 END";
                using var shortText = window.CaptureRenderedFrame();
                Assert.False(horizontal.IsVisible || vertical.IsVisible);
                Assert.InRange(editor.Bounds.Width - scroll.Bounds.Width, 0, 1);
                Assert.InRange(editor.Bounds.Height - scroll.Bounds.Height, 0, 1);
            }
            if (width == 1240 && scaling == 1)
            {
                workspace.ViewModel.EditorText = SampleModels.Lp;
                await workspace.RunAsync();
                var scroll = Assert.Single(window.ResultOutput.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "PART_ScrollViewer");
                using var initial = window.CaptureRenderedFrame();
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                using var final = window.CaptureRenderedFrame();
                var screenshot = Environment.GetEnvironmentVariable("KOPT_UI_SCREENSHOT");
                if (!string.IsNullOrWhiteSpace(screenshot))
                {
                    var directory = Path.GetDirectoryName(Path.GetFullPath(screenshot))!;
                    Directory.CreateDirectory(directory);
                    final!.Save(Path.Combine(directory, "output-scrolled-to-end.png"), PngBitmapEncoderOptions.Default);
                    var panel = (Border)window.ResultOutput.Parent!.Parent!;
                    using var detail = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(panel.Bounds.Width), (int)Math.Ceiling(panel.Bounds.Height)));
                    detail.Render(panel);
                    detail.Save(Path.Combine(directory, "output-detail.png"), PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
}
