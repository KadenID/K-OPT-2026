using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Kopt2026.App;

internal sealed class WorkspaceDialogs(Window owner) : IWorkspaceDialogs
{
    public async Task<string?> PickOpenAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "K-OPT 모델 열기", AllowMultiple = false,
            FileTypeFilter = [new("K-OPT 모델") { Patterns = ["*.opt"] }, FilePickerFileTypes.All]
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath() ?? throw new NotSupportedException("Local files only.");
    }
    public async Task<string?> PickSaveAsync(string? name, bool export)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = export ? "결과 내보내기" : "모델 저장", SuggestedFileName = name ?? "model.opt",
            DefaultExtension = export ? "txt" : "opt", ShowOverwritePrompt = true,
            FileTypeChoices = export
                ? [new("텍스트 결과") { Patterns = ["*.txt"] }, new("CSV") { Patterns = ["*.csv"] }, new("JSON") { Patterns = ["*.json"] }]
                : [new("K-OPT 모델") { Patterns = ["*.opt"] }]
        });
        return file?.TryGetLocalPath() ?? (file is null ? null : throw new NotSupportedException("Local files only."));
    }
    public async Task<UnsavedDecision> ConfirmUnsavedAsync()
    {
        var dialog = CreateDialog("변경 내용 저장", "편집한 내용을 저장하시겠습니까?", out var buttons);
        AddButton(buttons, "저장", () => dialog.Close(UnsavedDecision.Save), isDefault: true);
        AddButton(buttons, "저장 안 함", () => dialog.Close(UnsavedDecision.Discard));
        AddButton(buttons, "취소", () => dialog.Close(UnsavedDecision.Cancel), isCancel: true);
        return await dialog.ShowDialog<UnsavedDecision>(owner);
    }
    public async Task MessageAsync(string title, string message)
    {
        var dialog = CreateDialog(title, message, out var buttons);
        AddButton(buttons, "확인", dialog.Close, isDefault: true, isCancel: true);
        await dialog.ShowDialog(owner);
    }
    private static Window CreateDialog(string title, string message, out StackPanel buttons)
    {
        buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        return new Window
        {
            Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 20,
                Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons }
            }
        };
    }
    private static void AddButton(StackPanel panel, string title, Action action, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = title, MinWidth = 76, IsDefault = isDefault, IsCancel = isCancel };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }
}
