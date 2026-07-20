using System;
using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>
/// State for <see cref="ModalHost"/>. The dismiss handle lives here rather than in a field because MauiReactor
/// recreates the component instance on every render and migrates only <c>State</c> — a plain field would be null
/// on every instance after the first (the one <c>OnMounted</c> ran on).
/// </summary>
public sealed class ModalHostState
{
    internal OverlayDismissHandle? Handle { get; set; }

    /// <summary>Whether <see cref="Handle"/> is currently on the shared back stack.</summary>
    internal bool Armed { get; set; }
}

/// <summary>
/// The generic modal overlay mechanism (ADR-0007): a dimmed scrim with a centred content child, dismissed by a
/// scrim tap or hardware-back. It stays in the host's tree; while closed it renders nothing and lets touches
/// through, while open it shows the scrim and centres <see cref="Content"/> — which is therefore mounted fresh
/// each open, so dialog surfaces (<see cref="Calendar"/>, <see cref="ChoiceDialog"/>) reseed their draft state
/// naturally. The host owns <see cref="IsOpen"/> and reacts to <see cref="OnDismiss"/>. Seed-agnostic.
///
/// Back-to-dismiss needs the page to be a <see cref="ModalAwareContentPage"/>, which drains the shared
/// <see cref="OverlayBackStack"/> (this host registers on it while open) before the pop. This type stays
/// platform-free: the hook is MAUI's own <c>Page.OnBackButtonPressed</c>.
/// </summary>
public sealed partial class ModalHost : Component<ModalHostState>
{
    /// <summary>Whether the modal is showing. Set via <c>.IsOpen(...)</c>.</summary>
    [Prop] bool _isOpen;

    /// <summary>The centred dialog surface to host. Set via <c>.Content(...)</c>.</summary>
    [Prop] VisualNode? _content;

    /// <summary>Invoked on scrim tap or hardware-back. Set via <c>.OnDismiss(...)</c>.</summary>
    [Prop] Action? _onDismiss;

    protected override void OnMounted()
    {
        // Mutating State directly (no SetState) because this seeds the handle rather than reacting to a change —
        // the first render follows regardless.
        State.Handle = new OverlayDismissHandle();
        base.OnMounted();
    }

    protected override void OnWillUnmount()
    {
        if (State is { Armed: true, Handle: { } handle })
        {
            OverlayBackStack.Remove(handle);
            State.Armed = false;
        }

        State.Handle = null;
        base.OnWillUnmount();
    }

    public override VisualNode Render()
    {
        if (State.Handle is { } handle)
        {
            // Re-point at this instance's OnDismiss: the closure would otherwise hold the prop of the discarded
            // instance OnMounted ran on.
            handle.OnBack = () => _onDismiss?.Invoke();

            // Push on open / pop on close, so the stack order matches nesting.
            if (_isOpen && !State.Armed)
            {
                OverlayBackStack.Push(handle);
                State.Armed = true;
            }
            else if (!_isOpen && State.Armed)
            {
                OverlayBackStack.Remove(handle);
                State.Armed = false;
            }
        }

        if (!_isOpen)
        {
            return Grid().InputTransparent(true); // closed: nothing shown, touches pass through
        }

        // Star tracks around an Auto centre cell centre the surface (GridRow/GridColumn placement works on a
        // Component; HCenter/VCenter on a component root does not — ADR-0004).
        return Grid("*,Auto,*", "*,Auto,*",
            Border()
                .BackgroundColor(Colors.Black.WithAlpha(0.32f)) // M3 modal scrim
                .StrokeThickness(0)
                .OnTapped(() => _onDismiss?.Invoke())
                .GridRowSpan(3).GridColumnSpan(3),
            (_content ?? Grid()).GridRow(1).GridColumn(1)
        );
    }
}
