using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kopt2026.App;
using Kopt2026.Domain;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Tests;

public sealed class ResultLayoutTests
{
    [Theory]
    [InlineData(920, 620)]
    [InlineData(1240, 780)]
    public void All_Variable_Bars_And_Long_Labels_Are_Reachable_By_Scrolling(int width, int height) => OnSta(() =>
    {
        var window = new MainWindow();
        try
        {
            Assert.IsType<TabItem>(window.FindName("GraphTab")).IsSelected = true;
            var scroll = Assert.IsType<ScrollViewer>(window.FindName("GraphScrollViewer"));
            var graph = Assert.IsType<SolutionGraph>(scroll.Content);
            var variables = Enumerable.Range(1, 50)
                .Select(i => new Variable($"v{i}", $"X{i}_" + new string('A', 180), VariableType.Continuous)).ToList();
            var model = new OptimizationModel(ObjectiveSense.Maximize, new(new Dictionary<string, double>()), variables, [], "UTF-8", "test");
            var result = new SolveResult(SolveStatus.Optimal, 1,
                variables.Select(v => new VariableResult(v.Name, v.Type, 123456789012345, 0, 0, double.PositiveInfinity)).ToList(),
                [], null, TimeSpan.Zero, "test", new("Test", "1"), new(), []);
            graph.Model = model;
            graph.Result = result;
            Layout(window, width, height);

            Assert.True(scroll.ScrollableHeight > 1000);
            Assert.True(scroll.ScrollableWidth > 100);
            Assert.True(graph.ActualHeight >= 70 + 50 * 32);
            scroll.ScrollToBottom();
            scroll.ScrollToRightEnd();
            Assert.IsAssignableFrom<FrameworkElement>(window.Content).UpdateLayout();
            Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset, 1);
            Assert.Equal(scroll.ScrollableWidth, scroll.HorizontalOffset, 1);
            var lastRow = graph.TransformToAncestor(scroll).Transform(new Point(24, 58 + 49 * 32));
            Assert.InRange(lastRow.Y, 0, scroll.ViewportHeight - 15);
            SaveImage(scroll, $"graph-bottom-{width}.png");

            graph.Result = null;
            graph.Model = null;
            Layout(window, width, height);
            Assert.True(scroll.ExtentHeight < 1670);
            Assert.True(scroll.ExtentWidth < 1000);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(920, 620)]
    [InlineData(1240, 780)]
    public void Long_Representative_Solutions_Wrap_And_Can_Scroll_Inside_A_Row(int width, int height) => OnSta(() =>
    {
        var window = new MainWindow();
        try
        {
            Assert.IsType<TabItem>(window.FindName("AlternativeOptimaTab")).IsSelected = true;
            var grid = Assert.IsType<DataGrid>(window.FindName("AlternativeSolutionsGrid"));
            var values = string.Join(", ", Enumerable.Range(1, 60).Select(i => $"X{i}_LONG_VARIABLE_NAME=123,456,789.12"));
            var rows = Enumerable.Range(1, 10).Select(i => new RepresentativeSolutionDisplayRow(i, values + $", LAST_{i}=42")).ToList();
            grid.ItemsSource = rows;
            Layout(window, width, height);
            Assert.True(grid.Columns[1].ActualWidth > grid.ActualWidth * 0.6);
            var firstRow = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
            var text = Descendants<TextBlock>(firstRow).Single(t => t.Text == rows[0].Values);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.True(text.ActualHeight > 100);
            Assert.True(text.ActualWidth <= grid.Columns[1].ActualWidth);
            var scroll = Descendants<ScrollViewer>(grid).First();
            Assert.True(scroll.ScrollableHeight > scroll.ViewportHeight);

            // Pixel scrolling also exposes the end of a row taller than the viewport.
            scroll.ScrollToBottom();
            Assert.IsAssignableFrom<FrameworkElement>(window.Content).UpdateLayout();
            Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset, 1);
            var lastRow = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(9));
            var lastText = Descendants<TextBlock>(lastRow).Single(t => t.Text == rows[9].Values);
            var viewport = Descendants<ScrollContentPresenter>(scroll).First();
            var bottom = lastText.TransformToAncestor(viewport).Transform(new Point(0, lastText.ActualHeight));
            Assert.InRange(bottom.Y, 0, viewport.ActualHeight + 2);
            SaveImage(grid, $"representative-bottom-{width}.png");
        }
        finally { window.Close(); }
    });

    private static void Layout(MainWindow window, int width, int height)
    {
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        content.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void SaveImage(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            drawing.DrawRectangle(Brushes.White, null, bounds);
            drawing.DrawRectangle(new VisualBrush(element), null, bounds);
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "visual-checks");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF layout did not finish.");
        Assert.Null(failure);
    }
}
