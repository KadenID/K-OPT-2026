using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kopt2026.App;
using Kopt2026.Application;
using Kopt2026.Language;
using Kopt2026.Persistence;
using Kopt2026.Solver.Highs;

namespace Kopt2026.Tests;
public sealed class MacUiRegressionTests
{
    private static MainWindow Create() => new(new DesktopWorkspace(new TestDialogs(),
        new UserSettingsStore(Path.Combine(Path.GetTempPath(), "kopt-ui-regression", Guid.NewGuid()+".json"))));
    private static void Close(MainWindow window) { window.Close(); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void Editor_Title_And_Toolbar_Match_The_Original_Interface()
    {
        var window=Create();window.Show();
        try
        {
            using var frame=window.CaptureRenderedFrame();
            var labels=window.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text??"").ToArray();
            Assert.Contains("모델 편집기",labels);
            Assert.DoesNotContain(labels,t=>Regex.IsMatch(t,"[\\u3040-\\u30ff]"));
            var toolbar=Assert.IsType<StackPanel>(window.SolveButton.Parent);
            Assert.Equal(new[]{"새 문서","열기","저장","검증","실행","중지"},toolbar.Children.OfType<Button>().Select(AutomationProperties.GetName));
            var screenshot = Environment.GetEnvironmentVariable("KOPT_UI_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot))
            {
                var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshot))!, "toolbar.png");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var panel = Assert.IsType<Border>(toolbar.Parent);
                using var preview = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Math.Ceiling(toolbar.Children.Last().Bounds.Right + toolbar.Margin.Left + 4), (int)Math.Ceiling(panel.Bounds.Height)));
                preview.Render(panel);
                preview.Save(output, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally {Close(window);}
    }

    [AvaloniaTheory]
    [InlineData(920,640,1)]
    [InlineData(1240,800,1)]
    [InlineData(1707,1092,2)]
    [InlineData(920,640,2)]
    public void Splitters_Stop_At_Both_Bounds_And_Remain_Usable_After_Resizing(int width,int height,double scaling)
    {
        var window=Create();window.Width=width;window.Height=height;window.Show();
        try
        {
            window.SetRenderScaling(scaling);
            using var frame=window.CaptureRenderedFrame();
            var root=Assert.IsType<Grid>(window.Content);
            var workspace=Assert.IsType<Grid>(window.EditorOutputGrid.Parent);
            var inner=Assert.Single(window.EditorOutputGrid.Children.OfType<GridSplitter>());
            var outer=Assert.Single(workspace.Children.OfType<GridSplitter>());
            var vertical=Assert.Single(root.Children.OfType<GridSplitter>());
            var before=window.ModelEditor.Bounds.Width;
            Drag(window,inner,new Vector(40,0));
            Assert.True(window.ModelEditor.Bounds.Width>before+10,"The splitter must still allow normal resizing.");
            foreach(var splitter in new[]{inner,outer,vertical})
            {
                var horizontal=splitter.ResizeDirection==GridResizeDirection.Columns;
                foreach(var sign in new[]{1,-1})
                {
                    Drag(window,splitter,horizontal?new Vector(sign*10000,0):new Vector(0,sign*10000));
                    AssertLimits(window,root,workspace);
                }
            }
            window.Width=920;window.Height=640;
            using var resized=window.CaptureRenderedFrame();
            AssertLimits(window,root,workspace);
            var screenshot=Environment.GetEnvironmentVariable("KOPT_UI_SCREENSHOT");
            if(!string.IsNullOrWhiteSpace(screenshot) && width==1707)
            {
                var output=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshot))!,"splitter-limits-retina.png");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                resized!.Save(output,Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally {Close(window);}
    }
    private static void Drag(Window window,GridSplitter splitter,Vector delta)
    {
        var point=splitter.TranslatePoint(new Point(splitter.Bounds.Width/2,splitter.Bounds.Height/2),window)!.Value;
        window.MouseDown(point,MouseButton.Left);
        window.MouseMove(point+delta,RawInputModifiers.LeftMouseButton);
        window.MouseUp(point+delta,MouseButton.Left);
    }
    private static void AssertLimits(MainWindow window,Grid root,Grid workspace)
    {
        using var frame=window.CaptureRenderedFrame();
        Assert.True(window.EditorOutputGrid.Bounds.Width>=599,"Editor/output region collapsed.");
        Assert.True(window.ModelEditor.Bounds.Width>=217,"Model editor collapsed.");
        Assert.True(window.ResultOutput.Bounds.Width>=217,"Compatible output collapsed.");
        Assert.True(workspace.ColumnDefinitions[2].ActualWidth>=259,"Overview collapsed.");
        Assert.True(root.RowDefinitions[2].ActualHeight>=284,"Upper workspace collapsed.");
        Assert.True(root.RowDefinitions[4].ActualHeight>=179,"Results region collapsed.");
        Assert.True(workspace.ColumnDefinitions.Sum(c=>c.ActualWidth)<=workspace.Bounds.Width+1,"Columns overflow after resizing.");
        Assert.True(root.RowDefinitions.Sum(r=>r.ActualHeight)<=root.Bounds.Height+1,"Rows overflow after resizing.");
    }

    [Fact]
    public async Task Clearing_The_Editor_To_Null_Does_Not_Crash_Validation_Or_Run()
    {
        var vm=new MainViewModel(new KoptEngine(new OptParser(),new HighsSolverAdapter()));
        vm.EditorText=null!;
        vm.ValidateCommand.Execute(null);
        await vm.RunAsync();
        Assert.Equal(string.Empty,vm.EditorText);
        Assert.Null(vm.LatestResult);
        Assert.NotEmpty(vm.Diagnostics);
        Assert.False(vm.IsRunning);
    }
}
