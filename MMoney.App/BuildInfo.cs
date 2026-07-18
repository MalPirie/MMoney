using System;
using System.Linq;
using System.Reflection;
using Microsoft.Maui.ApplicationModel;

namespace MMoney.App;

/// <summary>
/// Build-identity accessor for the Settings page (app-design §9). Surfaces the app <see cref="Version"/> (from
/// MAUI's <see cref="AppInfo"/>), the <see cref="CommitSha"/> that SourceLink appended to the assembly's
/// informational version ("&lt;version&gt;+&lt;sha&gt;"), and the <see cref="BuildDate"/> stamped in by the
/// <c>StampBuildDate</c> MSBuild target as an <c>[AssemblyMetadata("BuildDate", …)]</c> attribute. The SHA and
/// build date read from the app assembly's own attributes, so they identify exactly the built binary.
/// </summary>
public static class BuildInfo
{
    private static readonly Assembly AppAssembly = typeof(BuildInfo).Assembly;

    /// <summary>The user-facing version string (e.g. "1.0"), from MAUI's <see cref="AppInfo"/>.</summary>
    public static string Version => AppInfo.Current.VersionString;

    /// <summary>The platform build string (e.g. the Android version code), from MAUI's <see cref="AppInfo"/>.</summary>
    public static string Build => AppInfo.Current.BuildString;

    /// <summary>
    /// The short (7-char) commit SHA of the build, parsed from the SourceLink-appended informational version, or
    /// <c>null</c> when the build carries no SHA (e.g. built outside a git working tree).
    /// </summary>
    public static string? CommitSha { get; } = ParseCommitSha();

    /// <summary>The UTC instant the assembly was compiled, from the <c>BuildDate</c> assembly-metadata attribute, or <c>null</c> if absent.</summary>
    public static DateTimeOffset? BuildDate { get; } = ParseBuildDate();

    // SourceLink builds the informational version as "<nuget-version>+<full-sha>"; take the segment after the
    // last '+' and require it to look like a git SHA before trusting it (MAUI/other tooling can add other '+' metadata).
    private static string? ParseCommitSha()
    {
        var informational = AppAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
        {
            return null;
        }

        var plus = informational.LastIndexOf('+');
        if (plus < 0 || plus == informational.Length - 1)
        {
            return null;
        }

        var sha = informational[(plus + 1)..];
        return sha.Length >= 7 && sha.All(Uri.IsHexDigit) ? sha[..7] : null;
    }

    private static DateTimeOffset? ParseBuildDate()
    {
        var stamp = AppAssembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value;
        return DateTimeOffset.TryParse(stamp, out var when) ? when : null;
    }
}
