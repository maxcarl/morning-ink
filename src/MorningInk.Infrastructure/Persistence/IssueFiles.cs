using Microsoft.Extensions.Options;
using MorningInk.Application.Publishing;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>Rendered issues live in the issue directory under the data directory.</summary>
internal sealed class IssueFiles(IOptions<StorageOptions> storage) : IIssueFiles
{
    public string PathFor(string fileName)
    {
        // Callers pass generated names; a path component here would be a bug, not user input.
        if (Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("Issue file names must not contain a path.", nameof(fileName));
        }

        var directory = storage.Value.IssueDirectory;
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}
