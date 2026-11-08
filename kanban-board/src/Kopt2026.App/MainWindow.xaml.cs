using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Win32;
using Kopt2026.Application;
using Kopt2026.Domain;
using Kopt2026.Export;
using Kopt2026.Language;
using Kopt2026.Persistence;
using Kopt2026.Solver.Highs;

namespace Kopt2026.App;

public partial class MainWindow : Window
{
    private readonly DocumentStore documents = new();
    private readonly UserSettingsStore settings;
    private readonly ResultExporter exporter = new();
    private readonly LegacyKoptImporter legacyImporter;
    private readonly MainViewModel viewModel;
    private string? currentPath;
    private Task pendingSettingsSave = Task.CompletedTask;
    private bool waitingForSettings;

    public MainWindow() : this(new UserSettingsStore()) { }

    public MainWindow(UserSettingsStore settings)
    {
        this.settings = settings;
        InitializeComponent();
        var parser = new OptParser();
        legacyImporter = new LegacyKoptImporter(parser);
        viewModel = new MainViewModel(new KoptEngine(parser, new HighsSolverAdapter()));
        DataContext = viewModel;
        ApplyResultHeaders(true);
        for (var digits = ResultDisplayPrecision.Minimum; digits <= ResultDisplayPrecision.Maximum; digits++)
        {
            var item = new MenuItem
            {
                Header = digits == ResultDisplayPrecision.Default ? $"{digits}자리 (기본값)" : $"{digits}자리",
                Tag = digits,
                IsCheckable = true
            };
            item.Click += DecimalPlaces_Click;
            DecimalPlacesMenuItem.Items.Add(item);
        }
        UpdateDecimalPlacesMenu();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) => await LoadSettingsAsync();

    private async Task LoadSettingsAsync()
    {
        var loaded = await settings.LoadAsync();
        KoreanTermsMenuItem.IsChecked = loaded.UseKoreanResultTerms;
        GraphInterceptsMenuItem.IsChecked = loaded.ShowGraphIntercepts;
        viewModel.SetKoreanResultTerms(loaded.UseKoreanResultTerms);
        viewModel.SetShowGraphIntercepts(loaded.ShowGraphIntercepts);
        viewModel.SetDecimalPlaces(loaded.DecimalPlaces);
        UpdateDecimalPlacesMenu();
        ApplyResultHeaders(loaded.UseKoreanResultTerms);
        SettingsMenuItem.IsEnabled = true;
    }

    private void New_Click(object sender, RoutedEventArgs e) { currentPath = null; viewModel.NewDocument(); Title = "새 문서 — K-OPT 2026"; }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "K-OPT 모델 (*.opt)|*.opt|모든 파일 (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var document = await documents.OpenAsync(dialog.FileName);
            var imported = legacyImporter.Import(document);
            viewModel.Load(imported.Document);

            if (!imported.WasConverted)
            {
                currentPath = dialog.FileName;
                Title = $"{Path.GetFileName(currentPath)} — K-OPT 2026";
                return;
            }

            currentPath = null;
            Title = $"{Path.GetFileName(dialog.FileName)} (구형 형식에서 변환) — K-OPT 2026";
            var details = string.Join(
                Environment.NewLine,
                imported.Changes
                    .GroupBy(change => change.Description)
                    .Select(group => $"- {group.Key} ({group.Count()}건)"));
            MessageBox.Show(
                this,
                $"{imported.Message}{Environment.NewLine}{Environment.NewLine}" +
                $"원본 파일은 변경하지 않았습니다.{Environment.NewLine}" +
                $"편집기에는 변환된 K-OPT 2026 문법이 표시됩니다.{Environment.NewLine}" +
                $"저장할 때 새 파일명과 위치를 선택하십시오.{Environment.NewLine}{Environment.NewLine}" +
                $"변환 내용{Environment.NewLine}{details}",
                "구형 K-OPT 파일 가져오기",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "파일 열기 오류", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (currentPath is null) { await SaveAsAsync(); return; }
        await SaveToAsync(currentPath);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await SaveAsAsync();

    private async Task SaveAsAsync()
    {
        var dialog = new SaveFileDialog { Filter = "K-OPT 모델 (*.opt)|*.opt", DefaultExt = ".opt", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        currentPath = dialog.FileName;
        await SaveToAsync(currentPath);
        Title = $"{Path.GetFileName(currentPath)} — K-OPT 2026";
    }

    private async Task SaveToAsync(string path)
    {
        try { await documents.SaveUtf8Async(path, viewModel.EditorText); viewModel.MarkSaved(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.LatestResult is null) { MessageBox.Show(this, "먼저 모델을 실행하십시오.", "결과 없음"); return; }
        var dialog = new SaveFileDialog { Filter = "텍스트 결과 (*.txt)|*.txt|CSV (*.csv)|*.csv|JSON (*.json)|*.json", DefaultExt = ".txt", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { await exporter.ExportAsync(viewModel.LatestResult, dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "내보내기 오류", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void KoreanTerms_Click(object sender, RoutedEventArgs e)
    {
        var useKorean = KoreanTermsMenuItem.IsChecked;
        viewModel.SetKoreanResultTerms(useKorean);
        ApplyResultHeaders(useKorean);
        await SaveSettingsAsync();
    }

    private async void GraphIntercepts_Click(object sender, RoutedEventArgs e)
    {
        var showIntercepts = GraphInterceptsMenuItem.IsChecked;
        viewModel.SetShowGraphIntercepts(showIntercepts);
        await SaveSettingsAsync();
    }

    private async void DecimalPlaces_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int digits }) return;
        viewModel.SetDecimalPlaces(digits);
        UpdateDecimalPlacesMenu();
        await SaveSettingsAsync();
    }

    private void UpdateDecimalPlacesMenu()
    {
        DecimalPlacesMenuItem.Header = $"소수 자릿수: {viewModel.DecimalPlaces}자리";
        foreach (MenuItem item in DecimalPlacesMenuItem.Items)
            item.IsChecked = (int)item.Tag == viewModel.DecimalPlaces;
    }

    private Task SaveSettingsAsync()
    {
        var snapshot = new UserSettings(viewModel.UseKoreanResultTerms, viewModel.ShowGraphIntercepts, viewModel.DecimalPlaces);
        pendingSettingsSave = SaveSettingsAfterAsync(pendingSettingsSave, snapshot);
        return pendingSettingsSave;
    }

    private async Task SaveSettingsAfterAsync(Task previous, UserSettings snapshot)
    {
        await previous;
        try { await settings.SaveAsync(snapshot); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "설정 저장 오류", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (pendingSettingsSave.IsCompleted) return;
        e.Cancel = true;
        if (waitingForSettings) return;
        waitingForSettings = true;
        SettingsMenuItem.IsEnabled = false;
        await pendingSettingsSave;
        Close();
    }

    private void ApplyResultHeaders(bool korean)
    {
        var variableHeaders = korean
            ? new[]
            {
                new ColumnHeaderInfo("이름", "모델에서 변수를 구분하는 이름입니다."),
                new ColumnHeaderInfo("변수 유형", "변수가 연속값을 갖는지 정수값만 갖는지 나타냅니다."),
                new ColumnHeaderInfo("최적값", "최적해에서 이 변수에 배정된 값입니다."),
                new ColumnHeaderInfo(
                    "수정비용",
                    "연속 선형계획에서 현재 경계에 있는 변수가 해에 들어오기 위해 목적함수 계수가 얼마나 개선되어야 하는지를 나타냅니다.",
                    "정수계획에서는 직접적인 경제적 의미로 해석하지 않습니다.")
            }
            : new[]
            {
                new ColumnHeaderInfo("Name", "The name that identifies the variable in the model."),
                new ColumnHeaderInfo("Type", "Whether the variable can be continuous or must be an integer."),
                new ColumnHeaderInfo("Value", "The value assigned to this variable in the optimal solution."),
                new ColumnHeaderInfo(
                    "Reduced Cost",
                    "For a continuous LP, the objective coefficient improvement generally required for a variable at a bound to enter the solution.",
                    "Do not give it the same direct economic interpretation for an integer program.")
            };
        var constraintHeaders = korean
            ? new[]
            {
                new ColumnHeaderInfo("이름", "모델에서 제약식을 구분하는 이름입니다."),
                new ColumnHeaderInfo("관계식", "제약식의 좌변 계산값과 우변을 비교하는 <=, >= 또는 = 관계입니다."),
                new ColumnHeaderInfo("좌변 계산값", "최적 변수값을 제약식 좌변에 대입해 계산한 값입니다."),
                new ColumnHeaderInfo("우변", "제약식 오른쪽에 지정한 기준값입니다."),
                new ColumnHeaderInfo(
                    "여유·잉여",
                    "현재 해가 제약 경계에서 얼마나 떨어져 있는지 나타냅니다.",
                    "<=는 우변-좌변, >=는 좌변-우변, =는 두 값의 차이로 계산하며 0이면 경계에서 정확히 만족됩니다."),
                new ColumnHeaderInfo(
                    "쌍대값(잠재가격)",
                    "자원이 1 단위 증가할 때, 최적 목적 함수 값(이익 또는 비용)이 얼마나 변하는지를 나타내는 값",
                    "연속 선형계획에서 다른 조건이 유지되는 국소 범위의 해석이며, 부호는 최대화·최소화와 제약 방향에 따라 달라집니다.",
                    true)
            }
            : new[]
            {
                new ColumnHeaderInfo("Name", "The name that identifies the constraint in the model."),
                new ColumnHeaderInfo("Relation", "The <=, >=, or = relation comparing the left-hand side value with the right-hand side."),
                new ColumnHeaderInfo("Left-hand Side Value", "The value calculated by substituting the optimal variable values into the left-hand side of the constraint."),
                new ColumnHeaderInfo("Right-hand Side", "The reference value specified on the right-hand side of the constraint."),
                new ColumnHeaderInfo(
                    "Slack / Surplus",
                    "How far the current solution is from the constraint boundary.",
                    "It is RHS-LHS for <=, LHS-RHS for >=, and the difference for =; zero means the constraint is binding."),
                new ColumnHeaderInfo(
                    "Dual (Shadow Price)",
                    "Indicates how much the optimal objective value (profit or cost) changes when the available resource increases by one unit.",
                    "Interpret it locally for a continuous LP; the sign depends on the objective sense and constraint direction.",
                    true)
            };
        var objectiveSensitivityHeaders = korean
            ? new[] { "변수", "현재계수", "하한", "상한" }
            : new[] { "Variable", "Current Coefficient", "Lower", "Upper" };
        var rightHandSideSensitivityHeaders = korean
            ? new[] { "제약식", "현재우변", "하한", "상한" }
            : new[] { "Constraint", "Current RHS", "Lower", "Upper" };
        ApplyColumnHeaders(VariablesGrid, variableHeaders);
        ApplyColumnHeaders(ConstraintsGrid, constraintHeaders);
        for (var i = 0; i < ObjectiveSensitivityGrid.Columns.Count; i++) ObjectiveSensitivityGrid.Columns[i].Header = objectiveSensitivityHeaders[i];
        for (var i = 0; i < RightHandSideSensitivityGrid.Columns.Count; i++) RightHandSideSensitivityGrid.Columns[i].Header = rightHandSideSensitivityHeaders[i];
    }

    private static void ApplyColumnHeaders(DataGrid grid, IReadOnlyList<ColumnHeaderInfo> headers)
    {
        if (grid.Columns.Count != headers.Count)
            throw new InvalidOperationException($"열 수({grid.Columns.Count})와 머리글 정의 수({headers.Count})가 일치하지 않습니다.");

        for (var index = 0; index < headers.Count; index++)
            grid.Columns[index].Header = CreateColumnHeader(headers[index]);
    }

    private static TextBlock CreateColumnHeader(ColumnHeaderInfo info)
    {
        var description = new TextBlock
        {
            MaxWidth = 420,
            TextWrapping = TextWrapping.Wrap
        };
        description.Inlines.Add(new Run(info.Description)
        {
            FontWeight = info.EmphasizeDescription ? FontWeights.Bold : FontWeights.Normal
        });
        if (!string.IsNullOrWhiteSpace(info.Detail))
        {
            description.Inlines.Add(new LineBreak());
            description.Inlines.Add(new Run(info.Detail));
        }

        var header = new TextBlock { Text = info.Title };
        header.ToolTip = new ToolTip
        {
            Content = description,
            Padding = new Thickness(10)
        };
        ToolTipService.SetShowDuration(header, 20_000);
        AutomationProperties.SetHelpText(header, $"{info.Description} {info.Detail}".Trim());
        return header;
    }

    private sealed record ColumnHeaderInfo(
        string Title,
        string Description,
        string? Detail = null,
        bool EmphasizeDescription = false);

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, "K-OPT 2026\n교육용 선형·정수 최적화 프로그램\n.NET 10 · HiGHS 1.15.1", "K-OPT 2026 정보");
}
