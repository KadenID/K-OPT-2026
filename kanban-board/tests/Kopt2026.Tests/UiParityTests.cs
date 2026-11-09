using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kopt2026.App;
using Kopt2026.Domain;
using Kopt2026.Persistence;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Tests;
public sealed class UiParityTests
{
    private static DesktopWorkspace Workspace(string? settings = null) => new(new TestDialogs(), new UserSettingsStore(settings ?? Path.Combine(Path.GetTempPath(), "kopt-common-settings", Guid.NewGuid()+".json")));
    private static void Close(MainWindow window) { window.Close(); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public async Task Toolbar_Run_Button_Executes_The_Current_Model_With_Rhs_Variables()
    {
        var workspace = Workspace();
        var window = new MainWindow(workspace);
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            workspace.ViewModel.EditorText = "MAX\n3 X + 2 Y\nST\n2 X <= 10 - Y\n3 Y <= 12 - X\nEND\n";
            using var initial = window.CaptureRenderedFrame();
            var point = window.SolveButton.TranslatePoint(new Point(window.SolveButton.Bounds.Width / 2, window.SolveButton.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (workspace.ViewModel.IsRunning) await Task.Delay(20, timeout.Token);
            Assert.Equal(SolveStatus.Optimal, workspace.ViewModel.LatestResult!.Status);
            Assert.Equal(16.4, workspace.ViewModel.LatestResult.ObjectiveValue!.Value, 7);
        }
        finally { Close(window); }
    }
    [AvaloniaTheory]
    [InlineData(920, 640, 1)]
    [InlineData(1240, 800, 2)]
    public async Task Editor_And_Output_Reach_Long_Text_And_Release_Unused_Scrollbar_Space(int width, int height, double scaling)
    {
        var workspace = Workspace();
        var window = new MainWindow(workspace) { Width = width, Height = height };
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            window.SetRenderScaling(scaling);
            foreach (var editor in new[] { window.ModelEditor, window.ResultOutput })
            {
                editor.Text = "# 한글 모델\nEND";
                using var initial = window.CaptureRenderedFrame();
                var scroll = Assert.Single(editor.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "PART_ScrollViewer");
                Assert.DoesNotContain(scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>(), b => b.IsVisible);
                Assert.InRange(scroll.Bounds.Width - scroll.Viewport.Width, 11.5, 12.5);
                Assert.InRange(scroll.Bounds.Height - scroll.Viewport.Height, 11.5, 12.5);

                var longText = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"행 {i}: " + new string('X', 160))) + "\n마지막 행 LAST_COLUMN";
                editor.Text = longText;
                using var expanded = window.CaptureRenderedFrame();
                Assert.True(scroll.Extent.Width > scroll.Viewport.Width + 100);
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 100);
                Assert.True(scroll.Bounds.Width > scroll.Viewport.Width);
                Assert.True(scroll.Bounds.Height > scroll.Viewport.Height);
                scroll.Offset = new Vector(scroll.Extent.Width, scroll.Extent.Height);
                using var end = window.CaptureRenderedFrame();
                Assert.Equal(scroll.Extent.Width - scroll.Viewport.Width, scroll.Offset.X, 1);
                Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1);
                editor.SelectionStart = longText.LastIndexOf("마지막", StringComparison.Ordinal);
                editor.SelectionEnd = longText.Length;
                Assert.Equal("마지막 행 LAST_COLUMN", editor.SelectedText);
                if (editor.IsReadOnly)
                {
                    editor.Focus();
                    window.KeyTextInput("덮어쓰기 금지");
                    Assert.Equal(longText, editor.Text);
                }
                editor.Text = "END";
                using var cleared = window.CaptureRenderedFrame();
                Assert.Equal(new Vector(0, 0), scroll.Offset);
                Assert.DoesNotContain(scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>(), b => b.IsVisible);
            }
        }
        finally { Close(window); }
    }
    [AvaloniaFact]
    public async Task Analysis_Tabs_And_Header_Explanations_Survive_Consolidation()
    {
        var workspace = Workspace();
        var window = new MainWindow(workspace);
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            Assert.Equal(new[] { "진단", "변수 해", "제약식", "민감도 분석", "그래프", "복수 최적해", "로그" }, window.ResultsTabs.Items.Cast<TabItem>().Select(t => t.Header));
            window.ResultsTabs.SelectedIndex = 2;
            using var frame = window.CaptureRenderedFrame();
            var grid = Assert.IsType<DataGrid>(((TabItem)window.ResultsTabs.SelectedItem!).Content);
            var header = Assert.IsType<TextBlock>(grid.Columns[5].Header);
            var tip = Assert.IsType<TextBlock>(ToolTip.GetTip(header));
            var description = Assert.IsType<Run>(tip.Inlines![0]);
            Assert.Contains("자원이 1 단위 증가할 때", description.Text);
            Assert.Equal(FontWeight.Bold, description.FontWeight);
            workspace.ViewModel.SetKoreanResultTerms(false);
            Assert.Equal("Left-hand Side Value", Assert.IsType<TextBlock>(grid.Columns[2].Header).Text);
        }
        finally { Close(window); }
    }
    [AvaloniaFact]
    public void Editor_Output_Splitter_Keeps_A_Custom_Ratio()
    {
        var window = new MainWindow(Workspace()) { Width = 1600, Height = 900 };
        window.Show();
        try
        {
            window.EditorOutputGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            window.EditorOutputGrid.ColumnDefinitions[2].Width = new GridLength(4, GridUnitType.Star);
            using var frame = window.CaptureRenderedFrame();
            Assert.InRange(window.ModelEditor.Bounds.Width / window.ResultOutput.Bounds.Width, 0.22, 0.28);
            Assert.Equal(3, window.GetVisualDescendants().OfType<GridSplitter>().Count());
        }
        finally { Close(window); }
    }
    [AvaloniaTheory]
    [InlineData(920, 640)]
    [InlineData(1240, 800)]
    public async Task Long_Graphs_Can_Scroll_To_The_Last_Variable_And_Reset(int width, int height)
    {
        var workspace = Workspace();
        var window = new MainWindow(workspace) { Width = width, Height = height };
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            var variables = Enumerable.Range(1, 50).Select(i => new Variable("v"+i, "X"+i+"_"+new string('A',180), VariableType.Continuous)).ToArray();
            var model = new OptimizationModel(ObjectiveSense.Maximize, new(new Dictionary<string,double>()), variables, [], "UTF-8", "test");
            window.Graph.Model = model;
            window.Graph.Result = new(SolveStatus.Optimal, 1, variables.Select(v => new VariableResult(v.Name,v.Type,123456789012345,0,0,double.PositiveInfinity)).ToArray(), [], null, TimeSpan.Zero,"test",new("test","1"),new(),[]);
            window.ResultsTabs.SelectedIndex = 4;
            using var initial = window.CaptureRenderedFrame();
            var scroll = window.GraphScrollViewer;
            Assert.True(scroll.Extent.Height - scroll.Viewport.Height > 1000);
            Assert.True(scroll.Extent.Width - scroll.Viewport.Width > 100);
            scroll.Offset = new Vector(scroll.Extent.Width, scroll.Extent.Height);
            using var bottom = window.CaptureRenderedFrame();
            Assert.Equal(scroll.Extent.Height-scroll.Viewport.Height, scroll.Offset.Y, 1);
            Assert.Equal(scroll.Extent.Width-scroll.Viewport.Width, scroll.Offset.X, 1);
            var last = window.Graph.TranslatePoint(new Point(24,58+49*32),scroll)!.Value;
            Assert.InRange(last.Y,0,scroll.Viewport.Height-10);
            window.Graph.Model = null; window.Graph.Result = null;
            using var reset = window.CaptureRenderedFrame();
            Assert.True(scroll.Extent.Height < 1000);
            Assert.True(scroll.Extent.Width < 1400);
        }
        finally { Close(window); }
    }
    [AvaloniaFact]
    public async Task Long_Representative_Solutions_Wrap_And_Remain_Reachable()
    {
        var workspace = Workspace();
        var window = new MainWindow(workspace);
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            var values=string.Join(", ",Enumerable.Range(1,60).Select(i=>$"X{i}_LONG_VARIABLE_NAME=123456789.12"))+", LAST=42";
            workspace.ViewModel.AlternativeSolutions.Add(new(1,values));
            window.ResultsTabs.SelectedIndex=5;
            using var initial=window.CaptureRenderedFrame();
            var text=Assert.Single(window.AlternativeSolutionsGrid.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text==values);
            Assert.Equal(TextWrapping.Wrap,text.TextWrapping);
            var scroll=Assert.IsType<ScrollViewer>(text.Parent);
            Assert.True(scroll.Extent.Height>scroll.Viewport.Height);
            Assert.True(window.AlternativeSolutionsGrid.Columns[1].ActualWidth>window.AlternativeSolutionsGrid.Bounds.Width*0.6);
            scroll.Offset=new Vector(0,scroll.Extent.Height);
            using var bottom=window.CaptureRenderedFrame();
            Assert.Equal(scroll.Extent.Height-scroll.Viewport.Height,scroll.Offset.Y,1);
        }
        finally { Close(window); }
    }
    [AvaloniaFact]
    public async Task Closing_After_Menu_Changes_Preserves_The_Latest_Settings()
    {
        var directory=Path.Combine(Path.GetTempPath(),"kopt-common-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"settings.json");
        var workspace=Workspace(path);
        var window=new MainWindow(workspace);
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            var items=window.PrecisionMenu.Items.Cast<MenuItem>().ToArray();
            Assert.Equal(16,items.Length);
            foreach(var digits in new[]{2,9,3,3}) items.Single(i=>(string?)i.Header==$"{digits}자리").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.KoreanTermsMenu.IsChecked=false;
            window.KoreanTermsMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.InterceptsMenu.IsChecked=false;
            window.InterceptsMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed+=(_,_)=>closed.TrySetResult();
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var restored=await new UserSettingsStore(path).LoadAsync();
            Assert.Equal(3,restored.DecimalPlaces);
            Assert.False(restored.UseKoreanResultTerms);
            Assert.False(restored.ShowGraphIntercepts);
            Assert.Empty(Directory.GetFiles(directory,"*.tmp"));
        }
        finally { Close(window); Directory.Delete(directory,true); }
    }
}
