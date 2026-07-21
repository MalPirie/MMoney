using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Maui.Storage;

namespace MMoney.App;

/// <summary>
/// A persistent, on-device crash log. The unhandled-exception handlers wired in <see cref="MauiProgram"/> append
/// each crash here <em>synchronously</em> (the process is usually dying), so it survives the crash and can be shared
/// for support from the Settings admin section. Captures managed .NET exceptions — including the Java-surfaced ones
/// that become a fatal on Android; a true native crash still only lands in Android's system tombstone/logcat.
/// </summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    /// <summary>The crash log file, in the app's private data directory (persists across restarts).</summary>
    public static string Path => System.IO.Path.Combine(FileSystem.AppDataDirectory, "crash.log");

    /// <summary>Whether any crash has been recorded.</summary>
    public static bool Exists => File.Exists(Path);

    /// <summary>Appends a crash entry — UTC timestamp, source, app version/commit, and the full exception — never
    /// throwing itself (we are likely already crashing).</summary>
    public static void Log(Exception? exception, string source)
    {
        try
        {
            var entry = new StringBuilder()
                .Append("==== ")
                .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Append("  [").Append(source).Append("] ====\n")
                .Append("MMoney ").Append(BuildInfo.Version).Append(" (").Append(BuildInfo.Build).Append(')')
                .Append(BuildInfo.CommitSha is { } sha ? $"  commit {sha}" : string.Empty)
                .Append('\n')
                .Append(exception?.ToString() ?? "(no exception object)")
                .Append("\n\n")
                .ToString();

            lock (Gate)
            {
                File.AppendAllText(Path, entry);
            }
        }
        catch
        {
            // Crash logging must never throw — swallow anything (disk full, already tearing down, …).
        }
    }

    /// <summary>Deletes the crash log.</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
            // Best-effort.
        }
    }
}
