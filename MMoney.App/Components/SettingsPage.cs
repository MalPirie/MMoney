using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using MauiReactor;
using MauiReactor.Shapes;
using Mobiorum.Material3;

namespace MMoney.App.Components;

internal sealed class SettingsState
{
    /// <summary>The theme option currently highlighted in the selector (mirrors <see cref="ThemePreference.Current"/>).</summary>
    public AppTheme Theme { get; set; }
}

/// <summary>
/// The Settings page (app-design §9): pushed over the shell (ADR-0005) with its own primary-coloured
/// <see cref="TopAppBar"/>. Content is grouped into rounded <c>surfaceContainer</c> boxes (the ledger day-box
/// idiom): an "About" box with the app version, and a "Theme" box with the tristate selector that used to live
/// on the Repeating tab. More options will slot into new boxes here.
/// </summary>
partial class SettingsPage : Component<SettingsState>
{
    protected override void OnMounted()
    {
        State.Theme = ThemePreference.Current;
        base.OnMounted();
    }

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;

        return ContentPage(
            Grid("Auto,*", "*",
                new TopAppBar()
                    .Title("Settings")
                    .Container(scheme.Primary)   // match the home banner
                    .OnContainer(scheme.OnPrimary)
                    .OnBack(() => _ = Navigation?.PopAsync())
                    .GridRow(0),
                ScrollView(
                    VStack(
                        AboutBox(scheme),
                        ThemeBox(scheme)
                    ).Spacing(24).Padding(16)
                ).GridRow(1)
            )
        ).HasNavigationBar(false);
    }

    // ---- About (version) ---------------------------------------------------------------------------------

    private static VisualNode AboutBox(MaterialScheme scheme)
    {
        var rows = new List<VisualNode>
        {
            DetailRow("Version", $"{BuildInfo.Version} ({BuildInfo.Build})", scheme),
        };

        if (BuildInfo.BuildDate is { } built)
        {
            rows.Add(DetailRow("Built", built.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), scheme));
        }

#if DEBUG
        // The commit SHA identifies the exact source of a dev build; release builds omit it (design §9).
        if (BuildInfo.CommitSha is { } sha)
        {
            rows.Add(DetailRow("Commit", sha, scheme));
        }
#endif

        return Section("About", scheme,
            VStack([.. rows]).Spacing(6).Padding(16, 14)
        );
    }

    // One "Label — value" line in the About box: a muted key on the left, the value filling the rest.
    private static VisualNode DetailRow(string label, string value, MaterialScheme scheme) =>
        Grid("Auto", "90,*",
            Label(label)
                .FontSize(14)
                .TextColor(scheme.OnSurfaceVariant)
                .VCenter()
                .GridColumn(0),
            Label(value)
                .FontSize(14)
                .TextColor(scheme.OnSurface)
                .VCenter()
                .GridColumn(1)
        );

    // ---- Theme selector ----------------------------------------------------------------------------------

    private VisualNode ThemeBox(MaterialScheme scheme) =>
        Section("Theme", scheme,
            Grid("Auto", "*,*,*",
                ThemeSegment("System", AppTheme.Unspecified, scheme).GridColumn(0),
                ThemeSegment("Light", AppTheme.Light, scheme).GridColumn(1),
                ThemeSegment("Dark", AppTheme.Dark, scheme).GridColumn(2)
            ).ColumnSpacing(8).Padding(16, 12)
        );

    // One pill in the tristate selector: the active option filled with primaryContainer, the rest a faint
    // surface pill. The box behind is surfaceContainer, so an unselected `surface` pill still reads as distinct.
    private VisualNode ThemeSegment(string label, AppTheme theme, MaterialScheme scheme)
    {
        var selected = State.Theme == theme;
        return Border(
            Label(label)
                .FontSize(13)
                .TextColor(selected ? scheme.OnPrimaryContainer : scheme.OnSurfaceVariant)
                .HCenter()
                .VCenter()
        )
        .BackgroundColor(selected ? scheme.PrimaryContainer : scheme.Surface)
        .StrokeThickness(0)
        .StrokeShape(new RoundRectangle().CornerRadius(20)) // full-height pill
        .HeightRequest(40)
        .OnTapped(() => SelectTheme(theme));
    }

    private void SelectTheme(AppTheme theme)
    {
        ThemePreference.Set(theme);
        SetState(s => s.Theme = theme);
    }

    // ---- section box (the rounded surfaceContainer idiom, with a subheader) -------------------------------

    private static VisualNode Section(string header, MaterialScheme scheme, VisualNode content) =>
        VStack(
            Label(header)
                .FontSize(14)
                .TextColor(scheme.OnSurfaceVariant)
                .Margin(4, 0, 0, 0),
            Border(content)
                .BackgroundColor(scheme.SurfaceContainer)
                .StrokeThickness(0)
                .StrokeShape(new RoundRectangle().CornerRadius(16))
        ).Spacing(8);
}
