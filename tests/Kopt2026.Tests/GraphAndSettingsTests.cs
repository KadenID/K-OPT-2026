using System.IO;
using Kopt2026.Application;
using Kopt2026.Language;
using Kopt2026.Persistence;

namespace Kopt2026.Tests;

public sealed class GraphAndSettingsTests
{
    [Fact]
    public void Feasible_Region_Contains_The_Known_Optimum()
    {
        var parsed = new OptParser().Parse(SourceDocument.FromText("MAX\n3 X1 + 2 X2\nST\n2 X1 + X2 <= 10\nX1 + 3 X2 <= 12\nEND\n"));
        var model = parsed.Model!;
        var polygon = FeasibleRegionBuilder.Build(model, 8, 8);

        Assert.True(polygon.Count >= 3);
        Assert.All(polygon, point => Assert.True(FeasibleRegionBuilder.Contains(model, point, 1e-6)));
        Assert.True(FeasibleRegionBuilder.Contains(model, new PlotPoint(3.6, 2.8)));
        Assert.False(FeasibleRegionBuilder.Contains(model, new PlotPoint(8, 8)));
    }

    [Fact]
    public async Task Korean_Result_Term_Setting_RoundTrips()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kopt2026-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new UserSettingsStore(path);
            await store.SaveAsync(new(false, false));
            var loaded = await store.LoadAsync();
            Assert.False(loaded.UseKoreanResultTerms);
            Assert.False(loaded.ShowGraphIntercepts);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Existing_Settings_Default_Intercept_Display_To_On()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kopt2026-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, "{\"UseKoreanResultTerms\":false}");
            var loaded = await new UserSettingsStore(path).LoadAsync();
            Assert.False(loaded.UseKoreanResultTerms);
            Assert.True(loaded.ShowGraphIntercepts);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
