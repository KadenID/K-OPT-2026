using System.ComponentModel;
using Kopt2026.App;
using Kopt2026.Application;
using Kopt2026.Domain;
using Kopt2026.Language;
using Kopt2026.Solver.Abstractions;

namespace Kopt2026.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task Precision_Changes_Refresh_Current_Results_Without_Recomputing_Or_Rounding_Data()
    {
        var calls = 0;
        var vm = CreateViewModel(model =>
        {
            calls++;
            return Task.FromResult(Result(model) with
            {
                ObjectiveValue = 1.23456789,
                Mip = new(1, 0.123456789, 1234),
                AlternativeOptima = new(OptimumMultiplicity.Multiple, true, true,
                    [new("X1", 0.123456789, 1.23456789)],
                    [new(1, [new("X1", 1.23456789)])], 10, TimeSpan.Zero)
            });
        });
        await Run(vm);
        var original = vm.LatestResult;
        var status = vm.StatusText;
        Assert.Equal(6, vm.DecimalPlaces);
        Assert.Equal("1.234568", vm.ObjectiveText);

        vm.SetDecimalPlaces(2);
        Assert.Equal("1.23", vm.ObjectiveText);
        Assert.Equal("X1=1.23", Assert.Single(vm.AlternativeSolutions).Values);
        Assert.Equal("0.12 ~ 1.23", Assert.Single(vm.OptimalRanges).Range);
        Assert.Contains("목적함수 값 : 1.23", vm.LegacyKoptOutputText);
        Assert.Contains(vm.Logs, line => line == "MIP gap: 0.12 · 노드: 1,234");
        Assert.Equal(status, vm.StatusText);
        Assert.Same(original, vm.LatestResult);
        Assert.Equal(1.23456789, vm.LatestResult!.ObjectiveValue);
        Assert.Equal(1, calls);

        vm.SetDecimalPlaces(6);
        Assert.Equal("1.234568", vm.ObjectiveText);
        Assert.Equal("X1=1.234568", Assert.Single(vm.AlternativeSolutions).Values);
        vm.EditorText += "\n# edit";
        vm.SetDecimalPlaces(3);
        AssertEmptyResults(vm);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("load")]
    [InlineData("new")]
    public async Task Document_Changes_Clear_All_Results(string change)
    {
        var vm = CreateViewModel(model => Task.FromResult(Result(model)));
        await Run(vm);
        Assert.NotEmpty(vm.Variables);
        Assert.NotEmpty(vm.Constraints);
        Assert.NotEmpty(vm.Logs);
        Assert.NotEmpty(vm.AlternativeSolutions);
        Assert.NotEmpty(vm.ObjectiveSensitivity);
        Assert.NotEmpty(vm.RightHandSideSensitivity);
        Assert.True(vm.CanExport);

        switch (change)
        {
            case "edit": vm.EditorText += "\n# changed"; break;
            case "load": vm.Load(SourceDocument.FromText(vm.EditorText)); break;
            case "new": vm.NewDocument(); break;
        }

        AssertEmptyResults(vm);
        vm.SetKoreanResultTerms(false);
        AssertEmptyResults(vm);
        if (change == "edit") Assert.Empty(vm.Diagnostics);
    }

    [Theory]
    [InlineData("complete", false)]
    [InlineData("error", false)]
    [InlineData("cancel", false)]
    [InlineData("complete", true)]
    public async Task Ignores_Old_Run_After_Edit_Or_Load(string outcome, bool load)
    {
        var completion = new TaskCompletionSource<SolveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        OptimizationModel? model = null;
        var vm = CreateViewModel(input => { model = input; return completion.Task; });
        vm.EditorText = "MAX\nX1 + X1\nST\nX1 <= 10\nEND";
        var finished = Run(vm);
        Assert.True(vm.IsRunning);
        var original = vm.EditorText;
        if (load) vm.Load(SourceDocument.FromText(original));
        else
        {
            vm.EditorText += "\n# edited";
            vm.EditorText = original;
        }
        var changedStatus = vm.StatusText;
        var changedDiagnostics = vm.Diagnostics.ToArray();
        switch (outcome)
        {
            case "complete": completion.SetResult(Result(model!)); break;
            case "error": completion.SetException(new InvalidOperationException("old error")); break;
            case "cancel": completion.SetCanceled(); break;
        }
        await finished;

        AssertEmptyResults(vm);
        Assert.Equal(changedDiagnostics, vm.Diagnostics);
        Assert.Equal(changedStatus, vm.StatusText);
        Assert.False(vm.IsRunning);
        Assert.True(vm.RunCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("error")]
    [InlineData("cancel")]
    public async Task Failed_Rerun_Does_Not_Leave_Previous_Result(string outcome)
    {
        var calls = 0;
        var vm = CreateViewModel(model => ++calls == 1 ? Task.FromResult(Result(model)) : outcome == "error"
            ? Task.FromException<SolveResult>(new InvalidOperationException("current error"))
            : Task.FromCanceled<SolveResult>(new CancellationToken(true)));
        await Run(vm);
        Assert.True(vm.CanExport);
        await Run(vm);

        Assert.Null(vm.LatestResult);
        Assert.Null(vm.LatestModel);
        Assert.Equal("-", vm.ObjectiveText);
        Assert.Empty(vm.Variables);
        Assert.Empty(vm.Constraints);
        Assert.Empty(vm.AlternativeSolutions);
        Assert.Empty(vm.ObjectiveSensitivity);
        Assert.False(vm.CanExport);
        Assert.Contains(outcome == "error" ? "오류" : "취소", vm.StatusText);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task Current_Document_Can_Run_Again_After_Edit()
    {
        var vm = CreateViewModel(model => Task.FromResult(Result(model)));
        await Run(vm);
        vm.EditorText += "\n# changed";
        AssertEmptyResults(vm);
        await Run(vm);

        Assert.True(vm.CanExport);
        Assert.NotEqual("-", vm.ObjectiveText);
        Assert.Equal(SourceDocument.FromText(vm.EditorText).Hash, vm.LatestResult!.ModelHash);
    }

    private static MainViewModel CreateViewModel(Func<OptimizationModel, Task<SolveResult>> solve) =>
        new(new KoptEngine(new OptParser(), new StubSolver(solve)));

    private static async Task Run(MainViewModel vm)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(vm.IsRunning) && !vm.IsRunning) completion.TrySetResult();
        }
        vm.PropertyChanged += Changed;
        try
        {
            vm.RunCommand.Execute(null);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { vm.PropertyChanged -= Changed; }
    }

    private static void AssertEmptyResults(MainViewModel vm)
    {
        Assert.Null(vm.LatestResult);
        Assert.Null(vm.LatestModel);
        Assert.False(vm.CanExport);
        Assert.Equal("-", vm.ObjectiveText);
        Assert.Equal(LegacyKoptReportFormatter.EmptyReport, vm.LegacyKoptOutputText);
        Assert.Empty(vm.Variables);
        Assert.Empty(vm.Constraints);
        Assert.Empty(vm.Logs);
        Assert.Empty(vm.OptimalRanges);
        Assert.Empty(vm.AlternativeSolutions);
        Assert.Empty(vm.ObjectiveSensitivity);
        Assert.Empty(vm.RightHandSideSensitivity);
    }

    private static SolveResult Result(OptimizationModel model) => new(
        SolveStatus.Feasible, 5,
        model.Variables.Select(v => new VariableResult(v.Name, v.Type, 1, 0, v.Lower, v.Upper)).ToList(),
        model.Constraints.Select(c => new ConstraintResult(c.Name, c.Relation, 1, c.RightHandSide, 0, 1)).ToList(),
        null, TimeSpan.Zero, model.SourceHash, new("Stub", "1"), new(), [],
        new(OptimumMultiplicity.Multiple, true, true, [new("X1", 0, 5)],
            [new(1, [new("X1", 1)])], 10, TimeSpan.Zero),
        Sensitivity: new(true, "available", [new("X1", 3, 1, 5)], [new("C1", 10, 5, 15)]));

    private sealed class StubSolver(Func<OptimizationModel, Task<SolveResult>> solve) : ISolverAdapter
    {
        public SolverIdentity Identity => new("Stub", "1");
        public Task<SolveResult> SolveAsync(OptimizationModel model, SolveOptions options, CancellationToken cancellationToken) => solve(model);
    }
}
