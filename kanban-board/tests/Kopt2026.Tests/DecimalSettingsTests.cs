using System.IO;
using Kopt2026.Persistence;

namespace Kopt2026.Tests;

public sealed class DecimalSettingsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(15)]
    public async Task Restores_Precision_From_Disk_In_A_New_Store(int digits)
    {
        var directory = Path.Combine(Path.GetTempPath(), "kopt-decimal-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            await new UserSettingsStore(path).SaveAsync(new(false, false, digits));
            var loaded = await new UserSettingsStore(path).LoadAsync();
            Assert.Equal(digits, loaded.DecimalPlaces);
            Assert.False(loaded.UseKoreanResultTerms);
            Assert.False(loaded.ShowGraphIntercepts);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [Theory]
    [InlineData("{\"UseKoreanResultTerms\":false,\"ShowGraphIntercepts\":false}")]
    [InlineData("{\"UseKoreanResultTerms\":false,\"ShowGraphIntercepts\":false,\"DecimalPlaces\":-1}")]
    [InlineData("{\"UseKoreanResultTerms\":false,\"ShowGraphIntercepts\":false,\"DecimalPlaces\":16}")]
    public async Task Old_Or_Out_Of_Range_Settings_Use_Six_And_Preserve_Other_Options(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "kopt-decimal-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, json);
            var loaded = await new UserSettingsStore(path).LoadAsync();
            Assert.Equal(6, loaded.DecimalPlaces);
            Assert.False(loaded.UseKoreanResultTerms);
            Assert.False(loaded.ShowGraphIntercepts);
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [Fact]
    public async Task Missing_And_Corrupt_Settings_Default_To_Six()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kopt-decimal-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            Assert.Equal(6, (await new UserSettingsStore(path).LoadAsync()).DecimalPlaces);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, "{not json}");
            Assert.Equal(6, (await new UserSettingsStore(path).LoadAsync()).DecimalPlaces);
            await new UserSettingsStore(path).SaveAsync(new() { DecimalPlaces = 99 });
            Assert.Equal(6, (await new UserSettingsStore(path).LoadAsync()).DecimalPlaces);
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }
}
