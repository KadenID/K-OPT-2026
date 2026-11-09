using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kopt2026.App;
using Kopt2026.Application;
using Kopt2026.Export;
using Kopt2026.Language;
using Kopt2026.Persistence;
using Kopt2026.Solver.Highs;

namespace Kopt2026.App;

public enum UnsavedDecision { Cancel, Save, Discard }
public interface IWorkspaceDialogs
{
    Task<string?> PickOpenAsync();
    Task<string?> PickSaveAsync(string? name, bool export);
    Task<UnsavedDecision> ConfirmUnsavedAsync();
    Task MessageAsync(string title, string message);
}

public sealed class DesktopWorkspace : INotifyPropertyChanged
{
    private readonly IWorkspaceDialogs dialogs;
    private readonly DocumentStore documents = new();
    private readonly LegacyKoptImporter importer = new();
    private readonly ResultExporter exporter = new();
    private readonly UserSettingsStore settings;
    private string savedText;
    private bool requiresSaveAs;
    private bool isFileBusy;
    private Task? initialization;
    private Task settingsSave = Task.CompletedTask;
    public MainViewModel ViewModel { get; }
    public string? CurrentPath { get; private set; }
    public bool IsDirty => requiresSaveAs || ViewModel.EditorText != savedText;
    public bool IsFileBusy { get => isFileBusy; private set { isFileBusy = value; Changed(); Changed(nameof(CanUseFiles)); } }
    public bool CanUseFiles => !IsFileBusy;
    public bool SettingsReady { get; private set; }
    public string Title => $"{(IsDirty ? "* " : "")}{(CurrentPath is null ? "새 문서" : Path.GetFileName(CurrentPath))} — K-OPT 2026";
    public event PropertyChangedEventHandler? PropertyChanged;

    public DesktopWorkspace(IWorkspaceDialogs dialogs, UserSettingsStore? settings = null, MainViewModel? viewModel = null)
    {
        this.dialogs = dialogs;
        this.settings = settings ?? new UserSettingsStore();
        ViewModel = viewModel ?? new MainViewModel(new KoptEngine(new OptParser(), new HighsSolverAdapter()));
        savedText = ViewModel.EditorText;
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.EditorText)) NotifyDocument();
        };
    }

    public Task InitializeAsync() => initialization ??= LoadSettingsAsync();
    private async Task LoadSettingsAsync()
    {
        var loaded = await settings.LoadAsync();
        ApplySettings(loaded);
        SettingsReady = true;
        Changed(nameof(SettingsReady));
    }
    private void ApplySettings(UserSettings value)
    {
        ViewModel.SetKoreanResultTerms(value.UseKoreanResultTerms);
        ViewModel.SetShowGraphIntercepts(value.ShowGraphIntercepts);
        ViewModel.SetDecimalPlaces(value.DecimalPlaces);
    }
    public Task SetSettingsAsync(UserSettings value)
    {
        ApplySettings(value);
        settingsSave = SaveSettingsAfterAsync(settingsSave, value);
        return settingsSave;
    }
    private async Task SaveSettingsAfterAsync(Task previous, UserSettings value)
    {
        await previous;
        try { await settings.SaveAsync(value); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { await dialogs.MessageAsync("설정 저장 오류", "설정을 저장하지 못했습니다. 저장 위치와 권한을 확인해 주세요."); }
    }
    public Task FlushSettingsAsync() => settingsSave;
    public Task RunAsync() => ViewModel.RunAsync();

    public Task OpenAsync() => WithFileOperation(async () =>
    {
        var path = await dialogs.PickOpenAsync();
        if (path is not null && await MayReplaceAsync()) await LoadPathAsync(path);
    });
    internal Task OpenPathAsync(string path) => WithFileOperation(async () =>
    {
        if (await MayReplaceAsync()) await LoadPathAsync(path);
    });
    private async Task LoadPathAsync(string path)
    {
        var document = await documents.OpenAsync(path);
        var imported = importer.Import(document);
        ViewModel.Load(imported.Document);
        savedText = imported.Document.Text;
        requiresSaveAs = imported.WasConverted;
        CurrentPath = imported.WasConverted ? null : path;
        NotifyDocument();
        if (imported.WasConverted)
            await dialogs.MessageAsync("구형 K-OPT 파일 가져오기", imported.Message + "\n원본 파일을 보존하기 위해 새 이름으로 저장합니다.");
    }
    public Task NewAsync() => WithFileOperation(async () =>
    {
        if (!await MayReplaceAsync()) return;
        ViewModel.NewDocument();
        CurrentPath = null;
        savedText = ViewModel.EditorText;
        requiresSaveAs = false;
        NotifyDocument();
    });
    public Task SaveAsync(bool saveAs = false) => WithFileOperation(async () => { await SaveCoreAsync(saveAs); });
    private async Task<bool> SaveCoreAsync(bool saveAs)
    {
        var path = saveAs || CurrentPath is null ? await dialogs.PickSaveAsync(CurrentPath is null ? null : Path.GetFileName(CurrentPath), false) : CurrentPath;
        if (path is null) return false;
        var text = ViewModel.EditorText;
        await documents.SaveUtf8Async(path, text);
        CurrentPath = path;
        savedText = text;
        requiresSaveAs = false;
        if (ViewModel.EditorText == text) ViewModel.MarkSaved();
        NotifyDocument();
        return true;
    }
    public Task ExportAsync() => WithFileOperation(async () =>
    {
        var result = ViewModel.LatestResult;
        if (result is null) { await dialogs.MessageAsync("결과 없음", "모델을 계산한 뒤 결과를 내보내 주세요."); return; }
        var path = await dialogs.PickSaveAsync("result.txt", true);
        if (path is null) return;
        if (!ReferenceEquals(result, ViewModel.LatestResult))
        { await dialogs.MessageAsync("입력 변경", "모델이 변경되었습니다. 다시 계산한 뒤 내보내 주세요."); return; }
        await exporter.ExportAsync(result, path);
    });
    private async Task<bool> MayReplaceAsync()
    {
        if (!IsDirty) return true;
        return await dialogs.ConfirmUnsavedAsync() switch
        {
            UnsavedDecision.Discard => true,
            UnsavedDecision.Save => await SaveCoreAsync(false),
            _ => false
        };
    }
    public async Task<bool> CanCloseAsync()
    {
        if (IsFileBusy) return false;
        IsFileBusy = true;
        try
        {
            if (!await MayReplaceAsync()) return false;
            if (ViewModel.IsRunning) ViewModel.CancelCommand.Execute(null);
            await settingsSave;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { await dialogs.MessageAsync("저장 오류", "저장하지 못했습니다. 저장 위치와 권한을 확인해 주세요."); return false; }
        finally { IsFileBusy = false; }
    }
    private async Task WithFileOperation(Func<Task> operation)
    {
        if (IsFileBusy) return;
        IsFileBusy = true;
        try { await operation(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { await dialogs.MessageAsync("파일 처리 오류", "파일을 처리하지 못했습니다. 파일 내용, 저장 위치와 접근 권한을 확인해 주세요."); }
        finally { IsFileBusy = false; }
    }
    private void NotifyDocument() { Changed(nameof(IsDirty)); Changed(nameof(CurrentPath)); Changed(nameof(Title)); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
