using MauiReactor;

namespace MMoney.App.Components;

/// <summary>
/// The app's navigation root: a <see cref="NavigationPage"/> hosting the <see cref="ShellPage"/>. Pushed pages —
/// Settings now, Add/Edit and the repeat-strategy page later (app-design §3, ADR-0005) — ride this stack. The
/// native navigation bar is hidden on every page (each draws its own primary-coloured banner or TopAppBar), so
/// this root exists only to provide the push/pop back stack.
/// </summary>
partial class AppRoot : Component
{
    public override VisualNode Render() =>
        NavigationPage(new ShellPage());
}
