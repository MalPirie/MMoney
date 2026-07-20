using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using MauiReactor;
using MauiReactor.Shapes;
using Mobiorum.Material3;
using MMoney.Core;

namespace MMoney.App.Components;

/// <summary>Props for the pushed <see cref="SettingsPage"/>.</summary>
internal sealed class SettingsProps
{
    /// <summary>Invoked after a change that the shell must reflect (e.g. closing a month advances the edit lock).</summary>
    public Action? OnChanged { get; set; }
}

internal sealed class SettingsState
{
    /// <summary>The theme option currently highlighted in the selector (mirrors <see cref="ThemePreference.Current"/>).</summary>
    public AppTheme Theme { get; set; }

    /// <summary>The event-sourced ledger, resolved from DI on mount (to apply the allow-close preference live).</summary>
    public AccountManager? Manager { get; set; }

    /// <summary>Whether closing months is allowed (mirrors <see cref="MonthClosePreference.Allowed"/>).</summary>
    public bool AllowClose { get; set; }
}

/// <summary>
/// The Settings page (app-design §9): pushed over the shell (ADR-0005) with its own primary-coloured
/// <see cref="TopAppBar"/>. Content is grouped into rounded <c>surfaceContainer</c> boxes (the ledger day-box
/// idiom): an "About" box with the app version, a "Theme" box with the tristate selector, and a "Close month" box
/// that collapses the oldest open month into a carried balance (§9 / Core month-close). More options slot in here.
/// </summary>
partial class SettingsPage : Component<SettingsState, SettingsProps>
{
    protected override void OnMounted()
    {
        State.Theme = ThemePreference.Current;
        State.Manager = Services.GetRequiredService<AccountManager>();
        State.AllowClose = MonthClosePreference.Allowed;
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
                        ThemeBox(scheme),
                        CloseMonthBox(scheme)
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

    // ---- Close months (Core month-close) -----------------------------------------------------------------

    // A persisted switch. When on, closing is allowed: closed months collapse into a carried balance and the ledger
    // shows a close button on the oldest open month. When off, past months stay visible but read-only.
    private VisualNode CloseMonthBox(MaterialScheme scheme) =>
        Section("Close months", scheme,
            Grid("Auto", "*,Auto",
                VStack(
                    Label("Allow closing months")
                        .FontSize(15)
                        .TextColor(scheme.OnSurface),
                    Label("Collapse a finished month into a carried balance. A close button appears on the oldest open month.")
                        .FontSize(12)
                        .TextColor(scheme.OnSurfaceVariant)
                ).Spacing(2).VCenter().GridColumn(0),
                Switch()
                    .IsToggled(State.AllowClose)
                    .OnColor(scheme.Primary)
                    .ThumbColor(scheme.OnPrimary)
                    .VCenter()
                    .Margin(12, 0, 0, 0)
                    .OnToggled(() => ToggleAllowClose(!State.AllowClose))
                    .GridColumn(1)
            ).Padding(16, 12)
        );

    // Persist the preference and apply it live: SetIgnoreMonthClosed reloads every account under the new mode
    // (collapsed vs. visible-read-only), then the shell re-renders on return via OnChanged.
    private void ToggleAllowClose(bool allowed)
    {
        MonthClosePreference.Allowed = allowed;
        State.Manager?.SetIgnoreMonthClosed(!allowed);
        Props.OnChanged?.Invoke();
        SetState(s => s.AllowClose = allowed);
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
