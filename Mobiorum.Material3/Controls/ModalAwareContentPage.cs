using MauiReactor;

namespace Mobiorum.Material3.Native;

/// <summary>
/// A <see cref="Microsoft.Maui.Controls.ContentPage"/> that gives an open <see cref="Mobiorum.Material3.ModalHost"/>
/// first refusal on the hardware back button — dismissing the innermost dialog instead of popping the page — and
/// then a registered <see cref="Mobiorum.Material3.PageBackGuard"/> (e.g. an editor guarding unsaved changes).
///
/// MauiReactor's own <c>ContentPage</c> exposes no back hook under a <c>NavigationPage</c>, so the override has to
/// live on a custom native page (the pattern MauiReactor documents for hardware back). AndroidX's
/// <c>OnBackPressedDispatcher</c> is NOT an alternative: MAUI pops the page without ever delegating to it, so a
/// callback registered there never fires however it is ordered or enabled — device-verified.
/// </summary>
public class ModalAwareContentPage : Microsoft.Maui.Controls.ContentPage
{
    /// <summary>
    /// This page's own claim on hardware back, consulted after any open <see cref="Mobiorum.Material3.ModalHost"/>.
    /// Returns whether it consumed the press (e.g. to show a "discard changes?" dialog). Per-page, so a page pushed
    /// on top never triggers a page underneath. Set by the hosting component each render.
    /// </summary>
    public Func<bool>? BackGuard { get; set; }

    protected override bool OnBackButtonPressed() =>
        Mobiorum.Material3.ModalHost.TryDismissTopmost()
        || (BackGuard?.Invoke() ?? false)
        || base.OnBackButtonPressed();
}
