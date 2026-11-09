using Avalonia;

namespace Kopt2026.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--smoke-test"])
            return SmokeTest.RunAsync(Console.Out).GetAwaiter().GetResult();
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Usage: Kopt2026.App [--smoke-test]");
            return 2;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DesktopApp>().UsePlatformDetect().LogToTrace();
}
