using System.Text.Json;

namespace Kopt2026.Persistence;

public sealed record UserSettings
{
    public UserSettings() { }

    public UserSettings(bool useKoreanResultTerms, bool showGraphIntercepts = true)
    {
        UseKoreanResultTerms = useKoreanResultTerms;
        ShowGraphIntercepts = showGraphIntercepts;
    }

    public bool UseKoreanResultTerms { get; init; } = true;
    public bool ShowGraphIntercepts { get; init; } = true;
}

public sealed class UserSettingsStore
{
    private readonly string path;

    public UserSettingsStore(string? path = null)
    {
        this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "K-OPT 2026", "settings.json");
    }

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<UserSettings>(stream, cancellationToken: cancellationToken) ?? new();
        }
        catch (JsonException) { return new(); }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("설정 폴더를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
