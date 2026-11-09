using System.Text;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kopt2026.Application;
using Kopt2026.App;
using Kopt2026.Language;
using Kopt2026.Persistence;

[assembly: AvaloniaTestApplication(typeof(Kopt2026.Tests.TestAppBuilder))]
namespace Kopt2026.Tests;
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DesktopApp>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class WorkspaceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kopt-desktop-" + Guid.NewGuid().ToString("N"));
    public WorkspaceTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private DesktopWorkspace Create(TestDialogs dialogs) => new(dialogs, new UserSettingsStore(Path.Combine(directory, "settings.json")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Korean_Path_And_Utf8_Or_Cp949_Load_And_Save_Without_Data_Loss(bool cp949)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(directory, "한글 모델.opt");
        var content = "# 한글 설명\r\n" + SampleModels.Lp.Replace("\n", "\r\n");
        await File.WriteAllBytesAsync(path, (cp949 ? Encoding.GetEncoding(949) : new UTF8Encoding(true)).GetBytes(content));
        var workspace = Create(new TestDialogs());
        await workspace.OpenPathAsync(path);
        Assert.Equal(cp949 ? "CP949" : "UTF-8", workspace.ViewModel.EncodingText);
        Assert.Contains("한글 설명", workspace.ViewModel.EditorText);
        workspace.ViewModel.EditorText += "# 수정한 한글\n";
        Assert.True(workspace.IsDirty);
        await workspace.SaveAsync();
        Assert.False(workspace.IsDirty);
        Assert.Equal("UTF-8", workspace.ViewModel.EncodingText);
        Assert.Equal(workspace.ViewModel.EditorText, await File.ReadAllTextAsync(path, new UTF8Encoding(false, true)));
    }
    [Fact]
    public async Task Legacy_Conversion_Does_Not_Overwrite_The_Source_When_Save_Is_Cancelled()
    {
        var path = Path.Combine(directory, "legacy.opt");
        const string original = "MAX 20X1 + 10X2\nST X1 > 1000\nX2 > 1000\n-X1 + X2 < 500\nEND\n";
        await File.WriteAllTextAsync(path, original);
        var workspace = Create(new TestDialogs { SavePath = null });
        await workspace.OpenPathAsync(path);
        Assert.Null(workspace.CurrentPath);
        Assert.True(workspace.IsDirty);
        await workspace.SaveAsync();
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.True(workspace.IsDirty);
    }
    [Theory]
    [InlineData(UnsavedDecision.Cancel, false)]
    [InlineData(UnsavedDecision.Save, false)]
    [InlineData(UnsavedDecision.Discard, true)]
    public async Task New_Document_Respects_Cancel_And_Cancelled_Save(UnsavedDecision decision, bool replaced)
    {
        var workspace = Create(new TestDialogs { Decision = decision });
        workspace.ViewModel.EditorText = "# 변경\n" + SampleModels.Lp;
        var original = workspace.ViewModel.EditorText;
        await workspace.NewAsync();
        Assert.Equal(replaced, workspace.ViewModel.EditorText != original);
    }
    [Fact]
    public async Task Failed_Save_As_Keeps_Previous_Path_And_Dirty_State()
    {
        var original = Path.Combine(directory, "original.opt");
        await File.WriteAllTextAsync(original, SampleModels.Lp);
        var dialogs = new TestDialogs { SavePath = Path.Combine(directory, "missing", "new.opt") };
        var workspace = Create(dialogs);
        await workspace.OpenPathAsync(original);
        workspace.ViewModel.EditorText += "# 편집\n";
        await workspace.SaveAsync(true);
        Assert.Equal(original, workspace.CurrentPath);
        Assert.True(workspace.IsDirty);
        Assert.Single(dialogs.Messages);
        Assert.Equal(SampleModels.Lp, await File.ReadAllTextAsync(original));
    }
    [Fact]
    public async Task Missing_Open_Does_Not_Replace_Current_Document()
    {
        var dialogs = new TestDialogs();
        var workspace = Create(dialogs);
        var original = workspace.ViewModel.EditorText;
        await workspace.OpenPathAsync(Path.Combine(directory, "missing.opt"));
        Assert.Equal(original, workspace.ViewModel.EditorText);
        Assert.Null(workspace.CurrentPath);
        Assert.Single(dialogs.Messages);
    }
    [Fact]
    public async Task Save_Before_New_Preserves_The_Current_Model()
    {
        var path = Path.Combine(directory, "saved.opt");
        var workspace = Create(new TestDialogs { Decision = UnsavedDecision.Save, SavePath = path });
        workspace.ViewModel.EditorText = SampleModels.Mip;
        await workspace.NewAsync();
        Assert.Equal(SampleModels.Mip, await File.ReadAllTextAsync(path));
        Assert.Null(workspace.CurrentPath);
        Assert.False(workspace.IsDirty);
    }
    [Theory]
    [InlineData("txt")]
    [InlineData("csv")]
    [InlineData("json")]
    public async Task Results_Export_With_Native_Calculation(string extension)
    {
        var path = Path.Combine(directory, "결과." + extension);
        var workspace = Create(new TestDialogs { SavePath = path });
        await workspace.RunAsync();
        await workspace.ExportAsync();
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains(extension == "csv" ? "3.6" : "16.4", text);
        Assert.Contains("X1", text);
    }
    [Fact]
    public async Task Close_Cancel_Keeps_Unsaved_Work_And_Settings_RoundTrip()
    {
        var workspace = Create(new TestDialogs { Decision = UnsavedDecision.Cancel });
        await workspace.InitializeAsync();
        await workspace.SetSettingsAsync(new UserSettings(false, false, 2));
        workspace.ViewModel.EditorText += "# 수정\n";
        Assert.False(await workspace.CanCloseAsync());
        var reloaded = Create(new TestDialogs());
        await reloaded.InitializeAsync();
        Assert.False(reloaded.ViewModel.UseKoreanResultTerms);
        Assert.False(reloaded.ViewModel.ShowGraphIntercepts);
        Assert.Equal(2, reloaded.ViewModel.DecimalPlaces);
    }
}

public sealed class WindowTests
{
    private static DesktopWorkspace Create(TestDialogs dialogs) => new(dialogs, new UserSettingsStore(Path.Combine(Path.GetTempPath(), "missing-kopt-settings", Guid.NewGuid()+".json")));
    [AvaloniaFact]
    public async Task Native_Solve_Updates_Tables_Legacy_Output_And_Graph()
    {
        var workspace = Create(new TestDialogs());
        var window = new MainWindow(workspace);
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            window.ModelEditor.Text = SampleModels.Lp;
            window.SolveButton.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (workspace.ViewModel.IsRunning) await Task.Delay(20, timeout.Token);
            Assert.Contains("16.4", window.ResultOutput.Text);
            Assert.Equal(2, workspace.ViewModel.Variables.Count);
            Assert.Equal(2, workspace.ViewModel.ObjectiveSensitivity.Count);
            window.ResultsTabs.SelectedIndex = 4;
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Same(workspace.ViewModel.LatestResult, window.Graph.Result);
            Assert.Same(workspace.ViewModel.LatestModel, window.Graph.Model);
            var screenshot = Environment.GetEnvironmentVariable("KOPT_UI_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot))
            { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!); frame.Save(screenshot, PngBitmapEncoderOptions.Default); }
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
    [AvaloniaFact]
    public async Task Precision_And_Language_Refresh_Existing_Table_Cells()
    {
        var workspace = Create(new TestDialogs());
        var window = new MainWindow(workspace);
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            workspace.ViewModel.EditorText = "MAX\nX1\nST\n3 X1 <= 1\nEND\n";
            await workspace.RunAsync();
            window.ResultsTabs.SelectedIndex = 1;
            using var initial = window.CaptureRenderedFrame();
            workspace.ViewModel.SetDecimalPlaces(2);
            workspace.ViewModel.SetKoreanResultTerms(false);
            using var updated = window.CaptureRenderedFrame();
            Assert.Equal("Name", Assert.IsType<TextBlock>(window.VariablesGrid.Columns[0].Header).Text);
            Assert.Equal("0.33", workspace.ViewModel.ObjectiveText);
            var cells = window.VariablesGrid.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains("0.33", cells);
            Assert.Equal(1d/3, workspace.ViewModel.LatestResult!.Variables[0].Value, 8);
            Assert.DoesNotContain("0.333333", cells);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
    [AvaloniaFact]
    public async Task Korean_Committed_Text_And_Run_Shortcut_Use_The_Current_Editor()
    {
        var workspace = Create(new TestDialogs());
        var window = new MainWindow(workspace);
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            window.ModelEditor.Text = string.Empty;
            window.ModelEditor.Focus();
            window.KeyTextInput("# 한글 메모\n" + SampleModels.Lp);
            Assert.Contains("한글 메모", workspace.ViewModel.EditorText);
            var runBinding = window.KeyBindings.Single(k => k.Gesture?.Key == Key.F5 && k.Gesture.KeyModifiers == KeyModifiers.None);
            Assert.Same(workspace.ViewModel.RunCommand, runBinding.Command);
            window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
            window.KeyRelease(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (workspace.ViewModel.IsRunning) await Task.Delay(20, timeout.Token);
            Assert.Equal(16.4, workspace.ViewModel.LatestResult!.ObjectiveValue!.Value, 7);
            Assert.Equal(KeyModifiers.Meta, MainWindow.PrimaryModifier(true));
            Assert.Equal(KeyModifiers.Control, MainWindow.PrimaryModifier(false));
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
    [AvaloniaFact]
    public async Task Intercept_And_Precision_Changes_Rerender_The_Graph()
    {
        var workspace = Create(new TestDialogs());
        await workspace.RunAsync();
        var graph = new SolutionGraph { Model = workspace.ViewModel.LatestModel, Result = workspace.ViewModel.LatestResult, Width = 1000, Height = 360 };
        var window = new Window { Content = graph, Width = 1000, Height = 360 };
        window.Show();
        try
        {
            var before = Capture(window);
            graph.ShowIntercepts = false;
            graph.DecimalPlaces = 2;
            var after = Capture(window);
            Assert.False(before.SequenceEqual(after));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void Many_Variables_Expand_The_Graph_For_Scrolling()
    {
        var model = new OptParser().Parse(SourceDocument.FromText("MAX\nX1 + X2 + X3\nST\nX1 + X2 + X3 <= 30\nEND\n")).Model!;
        var result = new Kopt2026.Solver.Abstractions.SolveResult(Kopt2026.Solver.Abstractions.SolveStatus.Optimal, 30,
            Enumerable.Range(1, 40).Select(i => new Kopt2026.Solver.Abstractions.VariableResult("VARIABLE_WITH_A_LONG_NAME_"+i, Kopt2026.Domain.VariableType.Continuous, i, 0, 0, 100)).ToArray(),
            [], null, TimeSpan.Zero, model.SourceHash, new("test", "1"), new(), []);
        var graph = new SolutionGraph { Model = model, Result = result };
        graph.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(graph.DesiredSize.Height >= 40*32);
        Assert.True(graph.DesiredSize.Width >= 640);
    }
    [AvaloniaFact]
    public async Task Korean_Clipboard_Paste_And_Save_Shortcut_RoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kopt-paste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "붙여넣기.opt");
        var workspace = Create(new TestDialogs { SavePath = path });
        var window = new MainWindow(workspace);
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            window.ModelEditor.Text = string.Empty;
            window.ModelEditor.Focus();
            var data = new DataTransfer();
            var content = "# 붙여넣은 한글\n" + SampleModels.Lp;
            data.Add(DataTransferItem.CreateText(content));
            await window.Clipboard!.SetDataAsync(data);
            var modifiers = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            window.KeyPress(Key.V, modifiers, PhysicalKey.V, "v");
            window.KeyRelease(Key.V, modifiers, PhysicalKey.V, "v");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!workspace.ViewModel.EditorText.Contains("붙여넣은 한글")) await Task.Delay(10, timeout.Token);
            window.KeyPress(Key.S, modifiers, PhysicalKey.S, "s");
            window.KeyRelease(Key.S, modifiers, PhysicalKey.S, "s");
            while (workspace.IsFileBusy) await Task.Delay(10, timeout.Token);
            Assert.True(File.Exists(path));
            Assert.Equal(content, await File.ReadAllTextAsync(path));
            Assert.False(workspace.IsDirty);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(directory, true); }
    }
    [AvaloniaTheory]
    [InlineData(920, 640)]
    [InlineData(1600, 1000)]
    public async Task Window_Resizes_Without_Collapsing_Editor_Or_Graph(int width, int height)
    {
        var workspace = Create(new TestDialogs());
        var window = new MainWindow(workspace) { Width = width, Height = height };
        window.Show();
        try
        {
            await workspace.InitializeAsync();
            await workspace.RunAsync();
            window.ResultsTabs.SelectedIndex = 4;
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(window.ModelEditor.Bounds.Width > 200);
            Assert.True(window.ModelEditor.Bounds.Height > 120);
            Assert.True(window.Graph.Bounds.Width >= 640);
            Assert.True(window.Graph.Bounds.Height >= 210);
            if (width == 920)
            {
                var screenshot = Environment.GetEnvironmentVariable("KOPT_UI_SCREENSHOT");
                if (!string.IsNullOrWhiteSpace(screenshot))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
                    frame.Save(Path.Combine(Path.GetDirectoryName(screenshot)!, "minimum-window.png"), PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
    private static byte[] Capture(Window window)
    {
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }
}

internal sealed class TestDialogs : IWorkspaceDialogs
{
    public string? SavePath { get; set; }
    public string? OpenPath { get; set; }
    public UnsavedDecision Decision { get; set; } = UnsavedDecision.Discard;
    public List<string> Messages { get; } = [];
    public Task<string?> PickOpenAsync() => Task.FromResult(OpenPath);
    public Task<string?> PickSaveAsync(string? name, bool export) => Task.FromResult(SavePath);
    public Task<UnsavedDecision> ConfirmUnsavedAsync() => Task.FromResult(Decision);
    public Task MessageAsync(string title, string message) { Messages.Add(title+": "+message); return Task.CompletedTask; }
}
