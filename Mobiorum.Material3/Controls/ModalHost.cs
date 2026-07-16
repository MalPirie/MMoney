using System;
using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>
/// The generic modal overlay mechanism (ADR-0007): a dimmed scrim with a centred content child, dismissed by a
/// scrim tap or Android hardware-back. It stays in the host's tree; while closed it renders nothing and lets
/// touches through, while open it shows the scrim and centres <see cref="Content"/> — which is therefore mounted
/// fresh each open, so dialog surfaces (<see cref="Calendar"/>, <see cref="ChoiceDialog"/>) reseed their draft
/// state naturally. The host owns <see cref="IsOpen"/> and reacts to <see cref="OnDismiss"/>. Seed-agnostic.
/// </summary>
public sealed partial class ModalHost : Component
{
    /// <summary>Whether the modal is showing. Set via <c>.IsOpen(...)</c>.</summary>
    [Prop] bool _isOpen;

    /// <summary>The centred dialog surface to host. Set via <c>.Content(...)</c>.</summary>
    [Prop] VisualNode? _content;

    /// <summary>Invoked on scrim tap or hardware-back. Set via <c>.OnDismiss(...)</c>.</summary>
    [Prop] Action? _onDismiss;

#if ANDROID
    private DismissBackCallback? _backCallback;
#endif

    protected override void OnMounted()
    {
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is AndroidX.Activity.ComponentActivity activity)
        {
            _backCallback = new DismissBackCallback(() => _onDismiss?.Invoke());
            activity.OnBackPressedDispatcher.AddCallback(_backCallback);
        }
#endif
        base.OnMounted();
    }

    protected override void OnWillUnmount()
    {
#if ANDROID
        _backCallback?.Remove();
        _backCallback = null;
#endif
        base.OnWillUnmount();
    }

    public override VisualNode Render()
    {
#if ANDROID
        // Arm hardware-back only while open, so a closed host lets back fall through to the normal page pop.
        if (_backCallback is not null)
        {
            _backCallback.Enabled = _isOpen;
        }
#endif
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

#if ANDROID
    private sealed class DismissBackCallback(Action onBack) : AndroidX.Activity.OnBackPressedCallback(enabled: false)
    {
        public override void HandleOnBackPressed() => onBack();
    }
#endif
}
