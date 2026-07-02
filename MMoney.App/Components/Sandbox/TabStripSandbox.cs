using System;
using MauiReactor;
using Mobiorum.Material3;

namespace MMoney.App.Components.Sandbox;

/// <summary>Which synthetic range the sandbox drives the strip with — to exercise every edge shape.</summary>
internal enum RangeMode
{
    /// <summary>Open-ended forward, bounded back at 0 — MMoney's real shape (semi-infinite).</summary>
    Infinite,

    /// <summary>Bounded BOTH ends (0..15), wider than the viewport — the finite-range case (hard clamp, no slide).</summary>
    Finite,

    /// <summary>A tiny finite range (0..2) that fits entirely in the viewport — the no-scroll case.</summary>
    Fits,

    /// <summary>Three items that fit the viewport with <b>no Home anchor</b> — the no-scroll, no-home branch.</summary>
    NoHome,
}

internal sealed class TabStripSandboxState
{
    /// <summary>Host-owned selection — the real <see cref="TabStrip{TItem}"/> reports taps here (ADR-0003).</summary>
    public int Selected = 5;

    /// <summary>Which synthetic range is active — switched by the toggle buttons.</summary>
    public RangeMode Mode = RangeMode.Infinite;

    /// <summary>True for the single frame after a mode switch, during which the strip is dropped from the tree
    /// so it unmounts and then remounts fresh (a re-seed against the new sequence shape).</summary>
    public bool Rebuilding;
}

/// <summary>
/// Dev-only harness for the real <see cref="TabStrip{TItem}"/> (ADR-0003). Drives it with a synthetic
/// <see cref="int"/> sequence whose edge shape is switchable (see <see cref="RangeMode"/>) so the strip is
/// exercised across the <b>semi-infinite</b>, <b>finite-both-ends</b>, and <b>fits-the-viewport</b> cases, all
/// with deliberately variable-width labels and a <c>Home</c> anchor. <c>TabStrip</c> assumes a stable sequence,
/// so switching mode changes the sequence shape underneath it — which the control does not (and need not, for
/// MMoney's fixed month range) re-seed for. The harness therefore drops the strip from the tree for one frame
/// on a switch, forcing a genuine unmount→remount and a clean re-seed. Hosted from the Sandbox nav destination;
/// delete once <c>TabStrip</c> is proven on device.
/// </summary>
internal sealed partial class TabStripSandbox : Component<TabStripSandboxState>
{
    // Deliberately non-uniform label widths so content-sizing/windowing is genuinely exercised.
    private static readonly string[] Shapes =
        { "Jan", "September", "May 2026", "Q3", "Wednesday", "7", "Mid-month", "Oct" };

    // Short labels for the fits-in-viewport modes, so the three tabs genuinely fit even with the Home button
    // stealing the leading column (the wide Shapes would overflow it and make the strip marginally scrollable).
    private static readonly string[] ShortShapes = { "Jan", "Feb", "Mar" };

    private string TabLabel(int i) => State.Mode is RangeMode.Fits or RangeMode.NoHome
        ? ShortShapes[i % ShortShapes.Length]
        : $"{Shapes[i % Shapes.Length]} · {i}";

    // Back edge is bounded at 0 in every mode (the edit-lock analog); the forward edge varies by mode.
    private const int Floor = 0;

    // The forward bound for the active mode, or null when the range is open-ended forward.
    private int? Ceiling => State.Mode switch
    {
        RangeMode.Finite => 15,
        RangeMode.Fits => 2,
        RangeMode.NoHome => 2,
        _ => null,
    };

    private int? Next(int i) => Ceiling is { } max ? (i < max ? i + 1 : null) : i + 1;

    private int? Prev(int i) => i > Floor ? i - 1 : null;

    // A home anchor that always sits inside the active range, or null for the no-home mode (no leading button).
    private int? Home => State.Mode switch
    {
        RangeMode.Finite => 7,
        RangeMode.Fits => 1,
        RangeMode.NoHome => null,
        _ => 5,
    };

    private void SwitchMode(RangeMode mode)
    {
        SetState(s =>
        {
            s.Mode = mode;
            var ceiling = mode switch
            {
                RangeMode.Finite => 15,
                RangeMode.Fits or RangeMode.NoHome => 2,
                _ => int.MaxValue,
            };
            s.Selected = Math.Clamp(s.Selected, Floor, ceiling); // keep the selection inside the new range
            s.Rebuilding = true;                                 // drop the strip this frame …
        });

        // … and restore it next frame, so the intervening absence forces a fresh mount + re-seed.
        Microsoft.Maui.Controls.Application.Current?.Dispatcher.Dispatch(() => SetState(s => s.Rebuilding = false));
    }

    public override VisualNode Render() =>
        VStack(
            Label($"TabStrip — mode {State.Mode}, selected {State.Selected}")
                .FontSize(12)
                .TextColor(MaterialTheme.Current.OnSurfaceVariant),

            HStack(
                ModeButton(RangeMode.Infinite, "Infinite"),
                ModeButton(RangeMode.Finite, "Finite"),
                ModeButton(RangeMode.Fits, "Fits"),
                ModeButton(RangeMode.NoHome, "No home")
            ).Spacing(6),

            // Absent for one frame after a switch (Rebuilding) so the strip remounts fresh against the new range.
            State.Rebuilding
                ? Grid().HeightRequest(64)
                : new TabStrip<int>()
                    .Selected(State.Selected)
                    .Next(Next)
                    .Prev(Prev)
                    .Label(TabLabel)
                    .Home(Home)
                    .OnSelectedChanged(i => SetState(s => s.Selected = i))
        )
        .Spacing(8)
        .Padding(16);

    private VisualNode ModeButton(RangeMode mode, string label) =>
        Button(label)
            .FontSize(12)
            .TextColor(State.Mode == mode ? MaterialTheme.Current.OnPrimary : MaterialTheme.Current.OnSurface)
            .BackgroundColor(State.Mode == mode ? MaterialTheme.Current.Primary : MaterialTheme.Current.SurfaceContainer)
            .OnClicked(() => SwitchMode(mode));
}
