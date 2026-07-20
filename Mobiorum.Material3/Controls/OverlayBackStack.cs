using System;
using System.Collections.Generic;

namespace Mobiorum.Material3;

/// <summary>
/// A live handle to one open overlay's dismiss action. <see cref="OnBack"/> is mutable because the host re-points
/// it at the live component instance on every render — MauiReactor recreates the component instance each render and
/// migrates only <c>State</c>, so a closure captured once would hold the discarded instance's prop.
/// </summary>
internal sealed class OverlayDismissHandle
{
    public Action? OnBack { get; set; }
}

/// <summary>
/// The shared hardware-back dismiss stack for overlays (<see cref="ModalHost"/> and <see cref="PopoverHost"/>). Each
/// registers a handle while open, outermost first, so back always dismisses the innermost overlay regardless of its
/// type. <see cref="Native.ModalAwareContentPage"/> drains it before letting the page pop. Kept in one place (not a
/// per-control stack) so a popover and a modal open together still resolve to a single, correctly-ordered stack.
///
/// This is the counterpart to MAUI's own back routing: AndroidX's <c>OnBackPressedDispatcher</c> is not usable here
/// — MAUI pops the page without ever delegating to it — so overlays must be reached through the page's
/// <c>OnBackButtonPressed</c> instead. See <see cref="Native.ModalAwareContentPage"/>.
/// </summary>
internal static class OverlayBackStack
{
    // The open overlays, outermost first — so back always hits the innermost.
    private static readonly List<OverlayDismissHandle> Open = [];

    public static void Push(OverlayDismissHandle handle) => Open.Add(handle);

    public static void Remove(OverlayDismissHandle handle) => Open.Remove(handle);

    /// <summary>
    /// Dismiss the innermost open overlay, if any. Returns true when one took the back press, so the caller
    /// (<see cref="Native.ModalAwareContentPage"/>) should consume it rather than let the page pop.
    /// </summary>
    public static bool TryDismissTopmost()
    {
        if (Open.Count == 0)
        {
            return false;
        }

        Open[^1].OnBack?.Invoke();
        return true;
    }
}
