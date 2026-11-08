using System.Text;
using Kopt2026.Language;

namespace Kopt2026.Persistence;

public sealed class DocumentStore
{
    public async Task<SourceDocument> OpenAsync(string path, CancellationToken cancellationToken = default) =>
        SourceDocument.FromBytes(await File.ReadAllBytesAsync(path, cancellationToken));

    public async Task SaveUtf8Async(string path, string text, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new InvalidOperationException("저장 폴더를 확인할 수 없습니다.");
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
