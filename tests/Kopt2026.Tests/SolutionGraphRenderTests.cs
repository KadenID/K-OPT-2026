using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using Kopt2026.App;
using Kopt2026.Domain;
using Kopt2026.Language;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Tests;

public sealed class SolutionGraphRenderTests
{
    [Fact]
    public void Result_Grids_Use_Clear_Headers_And_Tooltips()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                var variables = Assert.IsType<DataGrid>(window.FindName("VariablesGrid"));
                var constraints = Assert.IsType<DataGrid>(window.FindName("ConstraintsGrid"));

                Assert.Equal(4, variables.Columns.Count);
                Assert.DoesNotContain(variables.Columns, column => HeaderTitle(column) is "하한" or "상한");
                Assert.Equal("좌변 계산값", HeaderTitle(constraints.Columns[2]));
                Assert.All(variables.Columns, AssertHasTooltip);
                Assert.All(constraints.Columns, AssertHasTooltip);

                var dualHeader = Assert.IsType<TextBlock>(constraints.Columns[5].Header);
                var dualToolTip = Assert.IsType<ToolTip>(dualHeader.ToolTip);
                var dualText = Assert.IsType<TextBlock>(dualToolTip.Content);
                var dualRuns = dualText.Inlines.OfType<Run>().ToList();
                Assert.Contains("자원이 1 단위 증가할 때", string.Concat(dualRuns.Select(run => run.Text)));
                Assert.Equal(FontWeights.Bold, dualRuns[0].FontWeight);

                var applyHeaders = typeof(MainWindow).GetMethod("ApplyResultHeaders", BindingFlags.Instance | BindingFlags.NonPublic)!;
                applyHeaders.Invoke(window, [false]);
                Assert.Equal("Left-hand Side Value", HeaderTitle(constraints.Columns[2]));
                Assert.All(variables.Columns, AssertHasTooltip);
                Assert.All(constraints.Columns, AssertHasTooltip);
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    private static string HeaderTitle(DataGridColumn column) => Assert.IsType<TextBlock>(column.Header).Text;

    private static void AssertHasTooltip(DataGridColumn column)
    {
        var header = Assert.IsType<TextBlock>(column.Header);
        var toolTip = Assert.IsType<ToolTip>(header.ToolTip);
        var description = Assert.IsType<TextBlock>(toolTip.Content);
        Assert.NotEmpty(description.Inlines.OfType<Run>());
        Assert.All(description.Inlines.OfType<Run>(), run => Assert.False(string.IsNullOrWhiteSpace(run.Text)));
    }

    [Fact]
    public void Main_Window_Contains_Alternative_Optima_Tab_And_Grids()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                var tab = Assert.IsType<TabItem>(window.FindName("AlternativeOptimaTab"));
                Assert.Equal("복수 최적해", tab.Header);
                Assert.IsType<DataGrid>(window.FindName("OptimalRangesGrid"));
                Assert.IsType<DataGrid>(window.FindName("AlternativeSolutionsGrid"));
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    [Fact]
    public void Main_Window_Contains_Sensitivity_Tab_And_Grids()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                var tab = Assert.IsType<TabItem>(window.FindName("SensitivityTab"));
                Assert.Equal("민감도 분석", tab.Header);
                Assert.IsType<DataGrid>(window.FindName("ObjectiveSensitivityGrid"));
                Assert.IsType<DataGrid>(window.FindName("RightHandSideSensitivityGrid"));
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    [Fact]
    public void Main_Window_Contains_Resizable_Editor_And_Legacy_Output_Panes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                var editor = Assert.IsType<TextBox>(window.FindName("ModelEditorTextBox"));
                var output = Assert.IsType<TextBox>(window.FindName("LegacyKoptOutputTextBox"));
                var editorColumn = Assert.IsType<ColumnDefinition>(window.FindName("EditorColumn"));
                var outputColumn = Assert.IsType<ColumnDefinition>(window.FindName("LegacyOutputColumn"));
                var workspaceRow = Assert.IsType<RowDefinition>(window.FindName("WorkspaceRow"));
                var resultsRow = Assert.IsType<RowDefinition>(window.FindName("ResultsRow"));

                Assert.Equal(1, editorColumn.Width.Value, 10);
                Assert.Equal(1, outputColumn.Width.Value, 10);
                Assert.Equal(220, editorColumn.MinWidth);
                Assert.Equal(220, outputColumn.MinWidth);
                Assert.Equal(285, workspaceRow.MinHeight);
                Assert.Equal(180, resultsRow.MinHeight);
                Assert.Equal(ScrollBarVisibility.Auto, editor.HorizontalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
                Assert.Equal(TextWrapping.NoWrap, editor.TextWrapping);
                Assert.True(output.IsReadOnly);
                Assert.Equal(ScrollBarVisibility.Auto, output.HorizontalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Auto, output.VerticalScrollBarVisibility);
                Assert.Equal(TextWrapping.NoWrap, output.TextWrapping);
                Assert.IsType<GridSplitter>(window.FindName("EditorOutputSplitter"));
                Assert.IsType<GridSplitter>(window.FindName("MainHorizontalSplitter"));
                Assert.IsType<GridSplitter>(window.FindName("OverviewSplitter"));
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    [Fact]
    public void Keeps_One_To_Four_Editor_Output_Ratio_After_Layout()
    {
        Exception? failure = null;
        var editorRatio = 0d;
        var editorWidth = 0d;
        var outputWidth = 0d;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                var editorColumn = Assert.IsType<ColumnDefinition>(window.FindName("EditorColumn"));
                var outputColumn = Assert.IsType<ColumnDefinition>(window.FindName("LegacyOutputColumn"));
                editorColumn.Width = new GridLength(1, GridUnitType.Star);
                outputColumn.Width = new GridLength(4, GridUnitType.Star);

                content.Measure(new Size(1900, 780));
                content.Arrange(new Rect(0, 0, 1900, 780));
                content.UpdateLayout();
                content.InvalidateArrange();
                content.UpdateLayout();

                editorWidth = editorColumn.ActualWidth;
                outputWidth = outputColumn.ActualWidth;
                editorRatio = editorWidth / (editorWidth + outputWidth);
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.InRange(editorRatio, 0.19, 0.21);
        Assert.True(editorWidth >= 220);
        Assert.True(outputWidth >= 220);
    }

    [Theory]
    [InlineData(1240, 780)]
    [InlineData(920, 620)]
    public void Renders_Resizable_Main_Window_At_Normal_And_Minimum_Size(int width, int height)
    {
        Exception? failure = null;
        var coloredPixels = 0;
        var editorRatio = 0d;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                content.Measure(new Size(width, height));
                content.Arrange(new Rect(0, 0, width, height));
                content.UpdateLayout();

                var editorColumn = Assert.IsType<ColumnDefinition>(window.FindName("EditorColumn"));
                var outputColumn = Assert.IsType<ColumnDefinition>(window.FindName("LegacyOutputColumn"));
                editorRatio = editorColumn.ActualWidth / (editorColumn.ActualWidth + outputColumn.ActualWidth);

                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var pixels = new byte[width * height * 4];
                bitmap.CopyPixels(pixels, width * 4, 0);
                for (var index = 0; index < pixels.Length; index += 4)
                    if (pixels[index] < 245 || pixels[index + 1] < 245 || pixels[index + 2] < 245) coloredPixels++;
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.InRange(editorRatio, 0.49, 0.51);
        Assert.True(coloredPixels > 10_000, $"Expected rendered main-window content, observed {coloredPixels} pixels.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Renders_With_Intercept_Display_On_Or_Off_Without_Exception(bool showIntercepts)
    {
        Exception? failure = null;
        var coloredPixels = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var model = new OptParser().Parse(SourceDocument.FromText(
                    "MAX\n3 X1 + 2 X2\nST\nCAPACITY: 2 X1 + X2 <= 10\nDEMAND: X1 + 3 X2 <= 12\nEND\n")).Model!;
                var result = new SolveResult(
                    SolveStatus.Optimal,
                    16.4,
                    [
                        new("X1", VariableType.Continuous, 3.6, 0, 0, double.PositiveInfinity),
                        new("X2", VariableType.Continuous, 2.8, 0, 0, double.PositiveInfinity)
                    ],
                    [
                        new("CAPACITY", Relation.LessOrEqual, 10, 10, 0, 1.4),
                        new("DEMAND", Relation.LessOrEqual, 12, 12, 0, 0.2)
                    ],
                    null,
                    TimeSpan.Zero,
                    model.SourceHash,
                    new("Test", "1"),
                    new(),
                    []);
                var graph = new SolutionGraph
                {
                    Model = model,
                    Result = result,
                    UseKoreanTerms = true,
                    ShowIntercepts = showIntercepts,
                    Width = 1200,
                    Height = 420
                };
                graph.Measure(new Size(1200, 420));
                graph.Arrange(new Rect(0, 0, 1200, 420));
                graph.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1200, 420, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(graph);
                var pixels = new byte[1200 * 420 * 4];
                bitmap.CopyPixels(pixels, 1200 * 4, 0);
                for (var i = 0; i < pixels.Length; i += 4)
                    if (pixels[i] < 245 || pixels[i + 1] < 245 || pixels[i + 2] < 245) coloredPixels++;
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.True(coloredPixels > 5_000, $"Expected rendered graph pixels, observed {coloredPixels}.");
    }

    [Fact]
    public void Renders_Densely_Clustered_Intercept_Labels_Without_Exception()
    {
        Exception? failure = null;
        var coloredPixels = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var model = new OptParser().Parse(SourceDocument.FromText(
                    "MAX\nX1 + X2\nST\n" +
                    "C1: X1 + X2 <= 100\n" +
                    "C2: 1.01 X1 + X2 <= 100.5\n" +
                    "C3: 1.02 X1 + X2 <= 101\n" +
                    "C4: 1.03 X1 + X2 <= 101.5\n" +
                    "C5: X1 + 1.01 X2 <= 100.5\n" +
                    "C6: X1 + 1.02 X2 <= 101\n" +
                    "C7: X1 + 1.03 X2 <= 101.5\n" +
                    "END\n")).Model!;
                var result = new SolveResult(
                    SolveStatus.Optimal,
                    100,
                    [
                        new("X1", VariableType.Continuous, 50, 0, 0, double.PositiveInfinity),
                        new("X2", VariableType.Continuous, 50, 0, 0, double.PositiveInfinity)
                    ],
                    [],
                    null,
                    TimeSpan.Zero,
                    model.SourceHash,
                    new("Test", "1"),
                    new(),
                    []);
                var graph = new SolutionGraph
                {
                    Model = model,
                    Result = result,
                    UseKoreanTerms = true,
                    ShowIntercepts = true,
                    Width = 1200,
                    Height = 500
                };
                graph.Measure(new Size(1200, 500));
                graph.Arrange(new Rect(0, 0, 1200, 500));
                graph.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1200, 500, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(graph);
                var pixels = new byte[1200 * 500 * 4];
                bitmap.CopyPixels(pixels, 1200 * 4, 0);
                for (var i = 0; i < pixels.Length; i += 4)
                    if (pixels[i] < 245 || pixels[i + 1] < 245 || pixels[i + 2] < 245) coloredPixels++;
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.True(coloredPixels > 8_000, $"Expected dense rendered graph pixels, observed {coloredPixels}.");
    }
}
