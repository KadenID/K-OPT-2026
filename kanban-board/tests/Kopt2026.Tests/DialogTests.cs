using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Kopt2026.App;

namespace Kopt2026.Tests;

public sealed class DialogTests
{
    [AvaloniaTheory]
    [InlineData(Key.Escape, UnsavedDecision.Cancel)]
    [InlineData(Key.Enter, UnsavedDecision.Save)]
    public async Task Unsaved_Dialog_Responds_To_Keyboard_Without_Button_Focus(Key key, UnsavedDecision expected)
    {
        var owner = new Window { Width = 600, Height = 400 };
        owner.Show();
        var pending = new WorkspaceDialogs(owner).ConfirmUnsavedAsync();
        try
        {
            var dialog = Assert.Single(owner.OwnedWindows);
            using var frame = dialog.CaptureRenderedFrame();
            dialog.Focusable = true;
            dialog.Focus();
            dialog.KeyPress(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape, null);
            Assert.Equal(expected, await pending.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            foreach(var window in owner.OwnedWindows.ToArray()) window.Close();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(Key.Escape)]
    [InlineData(Key.Enter)]
    public async Task Information_Dialog_Can_Be_Dismissed_From_The_Keyboard(Key key)
    {
        var owner = new Window { Width = 600, Height = 400 };
        owner.Show();
        var pending = new WorkspaceDialogs(owner).MessageAsync("확인", "한글 안내 메시지");
        try
        {
            var dialog = Assert.Single(owner.OwnedWindows);
            using var frame = dialog.CaptureRenderedFrame();
            dialog.Focusable = true;
            dialog.Focus();
            dialog.KeyPress(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape, null);
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            foreach(var window in owner.OwnedWindows.ToArray()) window.Close();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
