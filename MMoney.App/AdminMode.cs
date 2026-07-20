namespace MMoney.App;

/// <summary>
/// Whether the hidden Settings admin section (raw account export / import) is unlocked. In-memory and
/// session-scoped: it survives navigating around within a run but resets on process restart — a fresh launch starts
/// locked — and is never persisted. The section gates a destructive Import, so it should not sit permanently armed.
/// Unlocked by tapping the Settings "About" box five times quickly.
/// </summary>
public static class AdminMode
{
    /// <summary>Whether the admin section is currently revealed. Resets to <see langword="false"/> on app restart.</summary>
    public static bool Enabled { get; set; }
}
