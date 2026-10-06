using System.Reflection;

namespace BackingTrack.Api;

internal static class BuildInfo
{
    /// <summary>
    /// "&lt;version&gt;+&lt;git sha&gt;", e.g. "1.2.0+abc…" on a commit tagged v1.2.0 or
    /// "1.2.1-alpha.0.5+abc…" five commits after it. MinVer derives the version from the nearest
    /// <c>v*</c> tag; the SDK appends <c>SourceRevisionId</c>, which deploy.yml sets to
    /// <c>github.sha</c> (ADR 0046).
    /// </summary>
    /// <remarks>
    /// Not <c>Assembly.GetName().Version</c>: that is the four-part assembly version, which can't
    /// carry a pre-release label or commit hash.
    /// </remarks>
    internal static string Version { get; } =
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}