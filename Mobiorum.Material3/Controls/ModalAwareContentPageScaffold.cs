using MauiReactor;

namespace Mobiorum.Material3;

/// <summary>
/// The MauiReactor node for <see cref="Native.ModalAwareContentPage"/>. Use it in place of <c>ContentPage(...)</c>
/// on any page that hosts a <see cref="ModalHost"/>, so hardware back dismisses the dialog rather than popping.
/// </summary>
[Scaffold(typeof(Native.ModalAwareContentPage))]
public partial class ModalAwareContentPage
{
    private Func<bool>? _backGuard;

    /// <summary>Sets the page's hardware-back claim (see <see cref="Native.ModalAwareContentPage.BackGuard"/>).
    /// A delegate CLR property the scaffold generator does not surface, so it is applied by hand on update.</summary>
    public ModalAwareContentPage BackGuard(Func<bool>? guard)
    {
        _backGuard = guard;
        return this;
    }

    protected override void OnUpdate()
    {
        if (NativeControl is Native.ModalAwareContentPage page)
        {
            page.BackGuard = _backGuard;
        }

        base.OnUpdate();
    }
}
