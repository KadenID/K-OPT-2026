using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kopt2026.App;
using Kopt2026.Persistence;

namespace Kopt2026.Tests;

public sealed class DecimalDisplayUiTests
{
    [Fact]
    public void Precision_Menu_Saves_Latest_Selection_With_Other_Options_And_Restores_In_New_Window() => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "kopt-decimal-ui-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var window = new MainWindow(new UserSettingsStore(path));
        MainWindow? reopened = null;
        try
        {
            await InvokeTask(window, "LoadSettingsAsync");
            var menu = Assert.IsType<MenuItem>(window.FindName("DecimalPlacesMenuItem"));
            var items = menu.Items.Cast<MenuItem>().ToList();
            Assert.Equal(16, items.Count);
            Assert.Equal(6, Assert.Single(items, i => i.IsChecked).Tag);
            foreach (var digits in new[] { 2, 9, 3, 3 })
                items.Single(i => (int)i.Tag == digits).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var korean = Assert.IsType<MenuItem>(window.FindName("KoreanTermsMenuItem"));
            korean.IsChecked = false;
            korean.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var intercepts = Assert.IsType<MenuItem>(window.FindName("GraphInterceptsMenuItem"));
            intercepts.IsChecked = false;
            intercepts.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await PendingSave(window);
            Assert.Equal(3, Assert.Single(items, i => i.IsChecked).Tag);
            Assert.Equal("소수 자릿수: 3자리", menu.Header);
            window.Close();

            reopened = new MainWindow(new UserSettingsStore(path));
            await InvokeTask(reopened, "LoadSettingsAsync");
            var vm = Assert.IsType<MainViewModel>(reopened.DataContext);
            Assert.Equal(3, vm.DecimalPlaces);
            Assert.False(vm.UseKoreanResultTerms);
            Assert.False(vm.ShowGraphIntercepts);
            var restoredMenu = Assert.IsType<MenuItem>(reopened.FindName("DecimalPlacesMenuItem"));
            Assert.Equal(3, Assert.Single(restoredMenu.Items.Cast<MenuItem>(), i => i.IsChecked).Tag);
        }
        finally
        {
            reopened?.Close();
            window.Close();
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    });

    [Fact]
    public void Numeric_Cells_And_Graph_Bindings_Update_Immediately() => OnSta(async () =>
    {
        var window = new MainWindow();
        try
        {
            var vm = Assert.IsType<MainViewModel>(window.DataContext);
            var grid = Assert.IsType<DataGrid>(window.FindName("VariablesGrid"));
            Assert.IsType<TabItem>(grid.Parent).IsSelected = true;
            vm.Variables.Add(new("X1", "연속", 1.23456789, -1.23456789, 0, double.PositiveInfinity));
            Layout(window);
            var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal("1.234568", Assert.IsType<TextBlock>(grid.Columns[2].GetCellContent(row)).Text);
            vm.SetDecimalPlaces(2);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Layout(window);
            Assert.Equal("1.23", Assert.IsType<TextBlock>(grid.Columns[2].GetCellContent(row)).Text);
            Assert.Equal("-1.23", Assert.IsType<TextBlock>(grid.Columns[3].GetCellContent(row)).Text);
            SaveImage(grid, "two-decimal-cells.png");
            vm.SetDecimalPlaces(6);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("1.234568", Assert.IsType<TextBlock>(grid.Columns[2].GetCellContent(row)).Text);

            Assert.IsType<TabItem>(window.FindName("GraphTab")).IsSelected = true;
            vm.SetDecimalPlaces(3);
            Layout(window);
            var graph = Assert.IsType<SolutionGraph>(Assert.IsType<ScrollViewer>(window.FindName("GraphScrollViewer")).Content);
            Assert.Equal(3, graph.DecimalPlaces);
            Assert.True(((FrameworkPropertyMetadata)SolutionGraph.DecimalPlacesProperty.GetMetadata(typeof(SolutionGraph))).AffectsMeasure);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Closing_Waits_For_Pending_Settings_Write() => OnSta(async () =>
    {
        var window = new MainWindow();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        typeof(MainWindow).GetField("pendingSettingsSave", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, gate.Task);
        var args = new CancelEventArgs();
        typeof(MainWindow).GetMethod("MainWindow_Closing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, args]);
        Assert.True(args.Cancel);
        Assert.False(Assert.IsType<MenuItem>(window.FindName("SettingsMenuItem")).IsEnabled);
        Assert.False(closed.Task.IsCompleted);
        gate.SetResult();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    });

    private static Task InvokeTask(MainWindow window, string method) =>
        (Task)typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;

    private static Task PendingSave(MainWindow window) =>
        (Task)typeof(MainWindow).GetField("pendingSettingsSave", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static void Layout(MainWindow window)
    {
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        content.Measure(new Size(920, 620));
        content.Arrange(new Rect(0, 0, 920, 620));
        content.UpdateLayout();
    }

    private static void SaveImage(FrameworkElement element, string name)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "visual-checks");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }

    private static void OnSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await action(); }
                catch (Exception ex) { failure = ex; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF precision test did not finish.");
        Assert.Null(failure);
    }
}
