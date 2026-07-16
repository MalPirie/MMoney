using MauiReactor;

namespace Mobiorum.Material3;

/// <summary>
/// The MauiReactor node for <see cref="Native.ModalAwareContentPage"/>. Use it in place of <c>ContentPage(...)</c>
/// on any page that hosts a <see cref="ModalHost"/>, so hardware back dismisses the dialog rather than popping.
/// </summary>
[Scaffold(typeof(Native.ModalAwareContentPage))]
public partial class ModalAwareContentPage
{
}
