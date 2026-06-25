using System;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Mobiorum.Material3;

namespace MMoney.App.Components;

internal sealed class ShellState
{
    /// <summary>Selected destination: 0 = Transactions, 1 = Repeating.</summary>
    public int Tab { get; set; }
}

/// <summary>
/// The app shell (walking skeleton): a persistent brand banner, a swappable central area driven by the bottom
/// navigation bar, and a floating FAB. Banner / nav / FAB persist; only the central area changes.
/// </summary>
partial class ShellPage : Component<ShellState>
{
    protected override void OnMounted()
    {
        LoadPersistedTheme();
        base.OnMounted();
    }

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;

        return ContentPage(
            Grid("Auto,*,Auto", "*",
                RenderBanner(scheme).GridRow(0),
                RenderCentral(scheme).GridRow(1),
                RenderNavBar().GridRow(2),
                RenderFab().GridRow(1)
            )
        );
    }

    private static VisualNode RenderBanner(MaterialScheme scheme) =>
        Grid("Auto,Auto", "*,Auto",
            // Hero: left, vertically centred across the whole banner.
            Label("£0.00")
                .FontSize(40)
                .TextColor(scheme.OnPrimary)
                .VCenter()
                .GridColumn(0).GridRowSpan(2),
            // Overflow: top-right, a proper 48dp M3 icon-button target.
            Button(MaterialSymbols.MoreVert)
                .FontFamily(MaterialSymbols.FontFamily)
                .FontSize(24)
                .BackgroundColor(Colors.Transparent)
                .TextColor(scheme.OnPrimary)
                .Padding(0)
                .CornerRadius(24)
                .WidthRequest(48)
                .HeightRequest(48)
                .HEnd()
                .GridColumn(1).GridRow(0),
            // Balances: right, below the overflow.
            VStack(
                Label("Available £0.00").FontSize(12).TextColor(scheme.OnPrimary).HEnd(),
                Label("Pending £0.00").FontSize(12).TextColor(scheme.OnPrimary).HEnd()
            ).Spacing(2).GridColumn(1).GridRow(1)
        ).BackgroundColor(scheme.Primary).Padding(20, 12);

    private VisualNode RenderCentral(MaterialScheme scheme) =>
        VStack(
            Label(State.Tab == 0 ? "Transactions" : "Repeating").FontSize(20).TextColor(scheme.OnSurface).HCenter(),
            Label("Walking skeleton — content goes here").FontSize(13).TextColor(scheme.OnSurfaceVariant).HCenter(),
            Label("Theme (temporary)").FontSize(12).TextColor(scheme.OnSurfaceVariant).HCenter(),
            HStack(
                ThemeButton("System", AppTheme.Unspecified),
                ThemeButton("Light", AppTheme.Light),
                ThemeButton("Dark", AppTheme.Dark)
            ).Spacing(8).HCenter()
        ).Spacing(12).Padding(24).VCenter().HCenter();

    // Destinations are packed to the start so the floating FAB has clear space on the trailing side.
    private VisualNode RenderNavBar() =>
        new NavigationBar()
            .Destinations([
                new NavDestination(MaterialSymbols.List, "Transactions")
                    .Selected(State.Tab == 0)
                    .OnSelected(() => SetState(s => s.Tab = 0)),
                new NavDestination(MaterialSymbols.Repeat, "Repeating")
                    .Selected(State.Tab == 1)
                    .OnSelected(() => SetState(s => s.Tab = 1))
            ])
            .Arrangement(NavArrangement.Start);

    // The FAB control is layout-agnostic; the shell positions it bottom-end, hovering half over the nav bar.
    private static VisualNode RenderFab() =>
        Grid(
            new Fab()
                .Icon(MaterialSymbols.Add)
                .OnClicked(() => { }) // add flow not wired yet (walking skeleton)
        )
        .HEnd()
        .VEnd()
        .Margin(0, 0, 16, -28);

    private VisualNode ThemeButton(string label, AppTheme theme) =>
        Button(label).FontSize(12).OnClicked(() => SetTheme(theme));

    private void SetTheme(AppTheme theme)
    {
        Preferences.Default.Set(ThemePreferenceKey, theme.ToString());
        if (MauiControls.Application.Current is { } app)
        {
            app.UserAppTheme = theme;
        }

        SetState(_ => { }); // re-render against the newly applied scheme
    }

    private static void LoadPersistedTheme()
    {
        var name = Preferences.Default.Get(ThemePreferenceKey, nameof(AppTheme.Unspecified));
        if (Enum.TryParse<AppTheme>(name, out var theme) && MauiControls.Application.Current is { } app)
        {
            app.UserAppTheme = theme;
        }
    }

    private const string ThemePreferenceKey = "app_theme";
}
