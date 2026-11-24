namespace MorningInk.Infrastructure;

/// <summary>
/// Where the source code lives: linked from the account page (AGPL section 13), named in the User-Agent and asked
/// for newer releases. A fork that runs modified code points <see cref="Url"/> to its own repository. Configured in
/// the "Repository" section.
/// </summary>
public sealed class RepositoryOptions
{
    public const string SectionName = "Repository";

    public string Url { get; set; } = "https://github.com/maxcarl/morning-ink";

    /// <summary>Off, nobody is asked for newer releases and no notice shows.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>The release tags in GitHub's format; empty takes them from GitHub's API for <see cref="Url"/>.</summary>
    public string? TagsUrl { get; set; }

    /// <summary>Where the release tags come from; null when <see cref="Url"/> is no GitHub repository and none is set.</summary>
    public Uri? Tags =>
        !string.IsNullOrWhiteSpace(TagsUrl) ? (Uri.TryCreate(TagsUrl, UriKind.Absolute, out var tags) ? tags : null)
        : Uri.TryCreate(Url, UriKind.Absolute, out var repository) && repository.Host == "github.com" && repository.AbsolutePath.Trim('/').Split('/') is [var owner, var name]
            ? new Uri($"https://api.github.com/repos/{owner}/{name}/tags?per_page=100")
            : null;
}
