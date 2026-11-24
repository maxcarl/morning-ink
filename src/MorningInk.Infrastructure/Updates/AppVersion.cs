using System.Reflection;

namespace MorningInk.Infrastructure.Updates;

/// <summary>
/// The running version: "1.2.0" for a release, "1.2.0-3-gabc1234" for a build from main three commits after it,
/// "dev" for a local build. The image build sets it from <c>git describe</c>.
/// </summary>
public sealed record AppVersion(string Value)
{
    /// <summary>The version this build was given; the part after "+" is a commit id the SDK may add.</summary>
    public static AppVersion FromAssembly() =>
        new(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] is { Length: > 0 } version
            ? version
            : "dev");

    /// <summary>The release this build is or comes after, e.g. 1.2.0 for "1.2.0-3-gabc1234"; null for a local build.</summary>
    public Version? Release => Parse(Value.Split('-')[0]);

    /// <summary>"1.2.0" or "v1.2.0" as a version; null for anything else, e.g. a pre-release.</summary>
    internal static Version? Parse(string text) =>
        Version.TryParse(text.TrimStart('v'), out var version) && version.Build >= 0 && version.Revision < 0 ? version : null;
}
