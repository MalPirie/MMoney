using System;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Mobiorum.Material3;
using MMoney.App.Components.Sandbox;
using MMoney.Core;

namespace MMoney.App.Components;

internal sealed class ShellState
{
    /// <summary>Selected destination: 0 = Transactions, 1 = Repeating, 2 = Sandbox (dev-only, ADR-0003 spike).</summary>
    public int Tab { get; set; }

    /// <summary>The month shown by the Transactions strip + pager (demonstrator state).</summary>
    public MonthOnly Month { get; set; }
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
        State.Month = MonthOnly.FromDate(DateOnly.FromDateTime(DateTime.Today));
        base.OnMounted();
    }

    // Placeholder edit-lock for the demonstrator: 24 months back. The real floor comes from the Core read
    // model when §5 is wired (this is throwaway scaffolding around the generic StripPager).
    private static readonly MonthOnly EditLock =
        MonthOnly.FromDate(DateOnly.FromDateTime(DateTime.Today)).Add(-24);

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

    private VisualNode RenderCentral(MaterialScheme scheme) => State.Tab switch
    {
        0 => RenderTransactions(scheme),
        1 => RenderRepeating(scheme),
        2 => RenderSandbox(),
        _ => RenderPagedSandbox(),
    };

    // Dev-only: hosts the real TabStrip (ADR-0003) for on-device validation, page area absent. Remove with the
    // Sandbox nav destination once TabStrip is proven on device.
    private static VisualNode RenderSandbox() =>
        new TabStripSandbox().WithKey("tabstrip-sandbox");

    // Dev-only: hosts the TabbedPageView composite (TabStrip + swipeable CarouselView body) for on-device
    // validation. Remove with the Paged nav destination once TabbedPageView is proven on device.
    private static VisualNode RenderPagedSandbox() =>
        new TabbedPageViewSandbox().WithKey("tabbedpageview-sandbox");

    // Transactions tab: the generic StripPager driven by a self-contained MonthOnly sequence (open-ended
    // forward, bounded back at the placeholder EditLock). Page bodies are throwaway scrollable placeholders —
    // the real ledger rows arrive with §5.
    private VisualNode RenderTransactions(MaterialScheme scheme) =>
        new StripPager<MonthOnly>()
            .Selected(State.Month)
            .Next(m => m.Add(1))
            .Prev(m => m.CompareTo(EditLock) <= 0 ? null : m.Add(-1))
            .Label(MonthLabel)
            .Page(m => MonthPage(scheme, m))
            .OnSelectedChanged(m => SetState(s => s.Month = m))
            .WithKey("transactions-pager"); // stable identity so a Month change reuses the control, not rebuilds it

    private static string MonthLabel(MonthOnly month) => month.FirstDay.ToString("MMM yy");

    private static VisualNode MonthPage(MaterialScheme scheme, MonthOnly month)
    {
        var rows = Enumerable.Range(1, 30).Select(i =>
            (VisualNode)Border(
                Label($"{MonthLabel(month)} — item {i}")
                    .FontSize(15)
                    .TextColor(scheme.OnSurface)
                    .VCenter()
                    .Padding(16, 0)
            )
            .BackgroundColor(scheme.SurfaceContainer)
            .StrokeThickness(0)
            .HeightRequest(56)
        );

        // Return content only — StripPager wraps it in its own vertical scroller (it owns the scroll so the
        // pan can arbitrate horizontal paging vs vertical scrolling).
        return VStack([.. rows]).Spacing(8).Padding(16);
    }

    private VisualNode RenderRepeating(MaterialScheme scheme) =>
        VStack(
            Label("Repeating").FontSize(20).TextColor(scheme.OnSurface).HCenter(),
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
                    .OnSelected(() => SetState(s => s.Tab = 1)),
                // Dev-only destinations for ADR-0003: the TabStrip alone, and the TabbedPageView composite.
                new NavDestination(MaterialSymbols.ChevronRight, "Sandbox")
                    .Selected(State.Tab == 2)
                    .OnSelected(() => SetState(s => s.Tab = 2)),
                new NavDestination(MaterialSymbols.List, "Paged")
                    .Selected(State.Tab == 3)
                    .OnSelected(() => SetState(s => s.Tab = 3))
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
