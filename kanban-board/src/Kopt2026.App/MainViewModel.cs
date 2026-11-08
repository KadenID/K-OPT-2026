using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Kopt2026.Application;
using Kopt2026.Domain;
using Kopt2026.Language;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly KoptEngine engine;
    private string editorText = SampleText;
    private string statusText = "준비";
    private string encodingText = "UTF-8";
    private string overviewText = "검증 전";
    private string objectiveText = "-";
    private string alternativeSummaryText = "최적화 실행 후 복수 최적해를 분석합니다.";
    private string sensitivitySummaryText = "최적화 실행 후 민감도 범위를 분석합니다.";
    private string legacyKoptOutputText = LegacyKoptReportFormatter.EmptyReport;
    private bool isRunning;
    private bool useKoreanResultTerms = true;
    private bool showGraphIntercepts = true;
    private CancellationTokenSource? solveCancellation;
    private SolveResult? latestResult;
    private OptimizationModel? latestModel;
    private long documentRevision;
    private int decimalPlaces = ResultDisplayPrecision.Default;

    public MainViewModel(KoptEngine engine)
    {
        this.engine = engine;
        ValidateCommand = new RelayCommand(Validate, () => !IsRunning);
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        Validate();
    }

    public string EditorText { get => editorText; set { if (Set(ref editorText, value)) { documentRevision++; ResetResults(); StatusText = "편집됨 · 검증 필요"; } } }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public string EncodingText { get => encodingText; private set => Set(ref encodingText, value); }
    public string OverviewText { get => overviewText; private set => Set(ref overviewText, value); }
    public string ObjectiveText { get => objectiveText; private set => Set(ref objectiveText, value); }
    public bool IsRunning { get => isRunning; private set { if (Set(ref isRunning, value)) RefreshCommands(); } }
    public SolveResult? LatestResult { get => latestResult; private set { if (Set(ref latestResult, value)) { RefreshAlternativeOptima(); RefreshSensitivity(); } OnPropertyChanged(nameof(CanExport)); } }
    public OptimizationModel? LatestModel { get => latestModel; private set => Set(ref latestModel, value); }
    public bool UseKoreanResultTerms { get => useKoreanResultTerms; private set => Set(ref useKoreanResultTerms, value); }
    public bool ShowGraphIntercepts { get => showGraphIntercepts; private set => Set(ref showGraphIntercepts, value); }
    public int DecimalPlaces => decimalPlaces;
    public string AlternativeSummaryText { get => alternativeSummaryText; private set => Set(ref alternativeSummaryText, value); }
    public string SensitivitySummaryText { get => sensitivitySummaryText; private set => Set(ref sensitivitySummaryText, value); }
    public string LegacyKoptOutputText { get => legacyKoptOutputText; private set => Set(ref legacyKoptOutputText, value); }
    public bool CanExport => LatestResult is not null;
    public ObservableCollection<Diagnostic> Diagnostics { get; } = [];
    public ObservableCollection<VariableDisplayRow> Variables { get; } = [];
    public ObservableCollection<ConstraintDisplayRow> Constraints { get; } = [];
    public ObservableCollection<OptimalRangeDisplayRow> OptimalRanges { get; } = [];
    public ObservableCollection<RepresentativeSolutionDisplayRow> AlternativeSolutions { get; } = [];
    public ObservableCollection<SensitivityDisplayRow> ObjectiveSensitivity { get; } = [];
    public ObservableCollection<SensitivityDisplayRow> RightHandSideSensitivity { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ICommand ValidateCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }

    public void Load(SourceDocument document)
    {
        editorText = document.Text;
        OnPropertyChanged(nameof(EditorText));
        EncodingText = document.EncodingName;
        documentRevision++;
        ResetResults();
        Validate();
    }

    public void NewDocument() => Load(SourceDocument.FromText("MAX\r\n\r\nST\r\n\r\nEND\r\n"));
    public void MarkSaved() => StatusText = "저장됨 · UTF-8";
    public void SetKoreanResultTerms(bool value)
    {
        UseKoreanResultTerms = value;
        RefreshResultRows();
    }

    public void SetShowGraphIntercepts(bool value) => ShowGraphIntercepts = value;

    public void SetDecimalPlaces(int value)
    {
        if (!Set(ref decimalPlaces, ResultDisplayPrecision.Normalize(value), nameof(DecimalPlaces)) || LatestResult is null) return;
        ObjectiveText = DisplayNumber(LatestResult.ObjectiveValue);
        LegacyKoptOutputText = LegacyKoptReportFormatter.Format(LatestResult, DecimalPlaces);
        RefreshAlternativeOptima();
        if (LatestResult.Mip is not null)
            for (var index = 0; index < Logs.Count; index++)
                if (Logs[index].StartsWith("MIP gap: ", StringComparison.Ordinal)) Logs[index] = MipLogText(LatestResult.Mip);
    }

    private string DisplayNumber(double? value) => ResultNumberFormatter.Format(value, DecimalPlaces);
    private string MipLogText(MipInfo mip) => $"MIP gap: {DisplayNumber(mip.RelativeGap)} · 노드: {(mip.NodeCount is long nodes ? ResultNumberFormatter.Format(nodes) : "-")}";

    private void Validate()
    {
        var result = engine.Validate(SourceDocument.FromText(EditorText, EncodingText));
        ShowDiagnostics(result);
        if (result.Model is null)
        {
            OverviewText = "모델을 해석할 수 없습니다.";
            StatusText = $"오류 {Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)}개";
            return;
        }
        OverviewText = $"변수 {result.Model.Variables.Count}개 · 제약식 {result.Model.Constraints.Count}개 · " +
                       (result.Model.Variables.Any(v => v.Type == VariableType.Integer) ? "혼합정수모형" : "선형계획모형");
        StatusText = Diagnostics.Count == 0 ? "유효한 모델" : $"유효 · 경고 {Diagnostics.Count}개";
    }

    private async Task RunAsync()
    {
        if (IsRunning) return;
        var runRevision = documentRevision;
        var source = SourceDocument.FromText(EditorText, EncodingText);
        ResetResults();
        IsRunning = true;
        StatusText = "해석 중…";
        solveCancellation = new CancellationTokenSource();
        try
        {
            var run = await engine.RunAsync(source, new SolveOptions(), solveCancellation.Token);
            // Editing or loading a document invalidates every outcome of the previous run.
            if (runRevision != documentRevision) return;
            ShowDiagnostics(run.Parse);
            if (run.Solve is null)
            {
                LegacyKoptOutputText = "문법 오류를 수정한 뒤 다시 실행하십시오.";
                StatusText = $"실행 중지 · 문법 오류 {Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)}개";
                return;
            }
            LatestResult = run.Solve;
            LatestModel = run.Parse.Model;
            LegacyKoptOutputText = LegacyKoptReportFormatter.Format(run.Solve, DecimalPlaces);
            RefreshResultRows();
            ObjectiveText = DisplayNumber(run.Solve.ObjectiveValue);
            OverviewText = $"{run.Solve.Status} · 변수 {Variables.Count}개 · 제약식 {Constraints.Count}개 · {ShortAlternativeSummary(run.Solve.AlternativeOptima)}";
            Logs.Clear();
            Logs.Add($"상태: {run.Solve.Status}");
            Logs.Add($"솔버: {run.Solve.Solver.Name} {run.Solve.Solver.Version}");
            Logs.Add($"모델 SHA-256: {run.Solve.ModelHash}");
            Logs.Add($"파싱: {run.ParseTime.TotalMilliseconds:F2} ms · 해석: {run.Solve.SolveTime.TotalMilliseconds:F2} ms");
            if (run.Solve.Mip is not null)
                Logs.Add(MipLogText(run.Solve.Mip));
            if (run.Solve.AlternativeOptima is { } alternatives)
                Logs.Add($"복수 최적해: {AlternativeLogText(alternatives)} · 분석 {alternatives.AnalysisTime.TotalMilliseconds:F2} ms");
            StatusText = $"{KoreanStatus(run.Solve.Status)} · {ShortAlternativeSummary(run.Solve.AlternativeOptima)} · {run.TotalTime.TotalMilliseconds:F2} ms";
        }
        catch (OperationCanceledException) { if (runRevision == documentRevision) { LegacyKoptOutputText = "사용자가 해석을 취소했습니다."; StatusText = "사용자가 해석을 취소했습니다."; } }
        catch (Exception ex) { if (runRevision == documentRevision) { LegacyKoptOutputText = "해석 오류가 발생했습니다. 로그 탭을 확인하십시오."; StatusText = "해석 오류"; Logs.Add($"오류 ID: {Guid.NewGuid():N}"); Logs.Add(ex.Message); } }
        finally { solveCancellation?.Dispose(); solveCancellation = null; IsRunning = false; }
    }

    private void Cancel() { StatusText = "취소 요청 중…"; solveCancellation?.Cancel(); }
    private void ResetResults()
    {
        LatestResult = null;
        LatestModel = null;
        RefreshResultRows();
        Logs.Clear();
        Diagnostics.Clear();
        ObjectiveText = "-";
        OverviewText = "검증 필요";
        LegacyKoptOutputText = LegacyKoptReportFormatter.EmptyReport;
    }
    private void RefreshResultRows()
    {
        Variables.Clear();
        Constraints.Clear();
        if (LatestResult is null) return;
        foreach (var item in LatestResult.Variables)
            Variables.Add(new(item.Name, Localize(item.Type), item.Value, item.ReducedCost, item.Lower, item.Upper));
        foreach (var item in LatestResult.Constraints)
            Constraints.Add(new(item.Name, Localize(item.Relation), item.Activity, item.RightHandSide, item.Slack, item.Dual));
    }
    private void RefreshAlternativeOptima()
    {
        OptimalRanges.Clear();
        AlternativeSolutions.Clear();
        var alternatives = LatestResult?.AlternativeOptima;
        if (alternatives is null)
        {
            AlternativeSummaryText = LatestResult is null
                ? "최적화 실행 후 복수 최적해를 분석합니다."
                : "최적성이 증명된 결과에서만 복수 최적해를 분석합니다.";
            return;
        }

        AlternativeSummaryText = alternatives.Multiplicity switch
        {
            OptimumMultiplicity.Unique => "고유 최적해입니다.",
            OptimumMultiplicity.Multiple when alternatives.IsContinuousFamily =>
                $"복수 최적해입니다. 연속적인 최적해 범위가 있으며 대표해 {alternatives.RepresentativeSolutions.Count}개를 표시합니다.",
            OptimumMultiplicity.Multiple =>
                $"복수 최적해입니다. 대표해 {alternatives.RepresentativeSolutions.Count}개를 표시합니다.",
            _ => "복수 최적해 판정이 제한 시간 안에 완료되지 않았습니다. 확인된 범위만 표시합니다."
        };

        foreach (var range in alternatives.VariableRanges)
            OptimalRanges.Add(new(range.Name, range.Minimum, range.Maximum, RangeText(range.Minimum, range.Maximum)));
        foreach (var solution in alternatives.RepresentativeSolutions)
            AlternativeSolutions.Add(new(solution.Number,
                string.Join(", ", solution.Variables.Select(value => $"{value.Name}={DisplayNumber(value.Value)}"))));
    }
    private void RefreshSensitivity()
    {
        ObjectiveSensitivity.Clear();
        RightHandSideSensitivity.Clear();
        var sensitivity = LatestResult?.Sensitivity;
        if (sensitivity is null)
        {
            SensitivitySummaryText = LatestResult is null
                ? "최적화 실행 후 민감도 범위를 분석합니다."
                : "이 결과에는 민감도 분석 정보가 없습니다.";
            return;
        }

        SensitivitySummaryText = sensitivity.Message;
        if (!sensitivity.IsAvailable) return;
        foreach (var range in sensitivity.ObjectiveCoefficients)
            ObjectiveSensitivity.Add(new(range.Name, range.CurrentValue, range.LowerValue, range.UpperValue));
        foreach (var range in sensitivity.RightHandSides)
            RightHandSideSensitivity.Add(new(range.Name, range.CurrentValue, range.LowerValue, range.UpperValue));
    }
    private string RangeText(double? minimum, double? maximum)
    {
        if (minimum is null || maximum is null) return "미확인";
        if (NearlyEqual(minimum.Value, maximum.Value)) return "고정";
        return $"{DisplayNumber(minimum)} ~ {DisplayNumber(maximum)}";
    }
    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= 1e-7 * Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right)));
    private static string ShortAlternativeSummary(AlternativeOptimaInfo? alternatives) => alternatives?.Multiplicity switch
    {
        OptimumMultiplicity.Unique => "고유 최적해",
        OptimumMultiplicity.Multiple when alternatives.IsContinuousFamily => "복수 최적해(연속 범위)",
        OptimumMultiplicity.Multiple => "복수 최적해",
        OptimumMultiplicity.Undetermined => "복수 최적해 판정 미완료",
        _ => "복수 최적해 분석 없음"
    };
    private static string AlternativeLogText(AlternativeOptimaInfo alternatives) =>
        $"{ShortAlternativeSummary(alternatives)}, 대표해 {alternatives.RepresentativeSolutions.Count}/{alternatives.RequestedSolutionLimit}개, 범위 분석 {(alternatives.IsComplete ? "완료" : "미완료")}";
    private string Localize(VariableType type) => !UseKoreanResultTerms ? type.ToString() : type == VariableType.Integer ? "정수" : "연속";
    private string Localize(Relation relation) => !UseKoreanResultTerms ? relation.ToString() : relation switch { Relation.LessOrEqual => "이하(≤)", Relation.GreaterOrEqual => "이상(≥)", _ => "같음(=)" };
    private void ShowDiagnostics(ParseResult result) { Diagnostics.Clear(); foreach (var item in result.Diagnostics) Diagnostics.Add(item); }
    private void RefreshCommands() { (ValidateCommand as RelayCommand)?.RaiseCanExecuteChanged(); (RunCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged(); (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    private static string KoreanStatus(SolveStatus status) => status switch { SolveStatus.Optimal => "최적해 확인", SolveStatus.Feasible => "실현가능해", SolveStatus.Infeasible => "실행불가능", SolveStatus.Unbounded => "무한해", SolveStatus.TimeLimit => "시간 제한", _ => status.ToString() };

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    private const string SampleText = "MAX\r\n3 X1 + 2 X2\r\nST\r\nCAPACITY: 2 X1 + X2 <= 10\r\nDEMAND: X1 + 3 X2 <= 12\r\nEND\r\n";
}

public sealed record VariableDisplayRow(string Name, string Type, double Value, double ReducedCost, double Lower, double Upper);
public sealed record ConstraintDisplayRow(string Name, string Relation, double Activity, double RightHandSide, double Slack, double Dual);
public sealed record OptimalRangeDisplayRow(string Name, double? Minimum, double? Maximum, string Range);
public sealed record RepresentativeSolutionDisplayRow(int Number, string Values);
public sealed record SensitivityDisplayRow(string Name, double CurrentValue, double LowerValue, double UpperValue);

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public async void Execute(object? parameter) => await execute();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
