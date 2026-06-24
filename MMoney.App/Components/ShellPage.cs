using System;
using MauiReactor.Shapes;
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
                RenderNavBar(scheme).GridRow(2),
                RenderFab(scheme).GridRow(1)
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

    private VisualNode RenderNavBar(MaterialScheme scheme) =>
        Grid("80", "*", // M3 navigation bar container height is 80dp
            HStack(
                NavItem(MaterialSymbols.List, "Transactions", 0, scheme),
                NavItem(MaterialSymbols.Repeat, "Repeating", 1, scheme)
            ).Spacing(4).Padding(8, 0).HStart().VCenter()
        ).BackgroundColor(scheme.SurfaceContainer);

    private VisualNode NavItem(string glyph, string label, int index, MaterialScheme scheme)
    {
        var selected = State.Tab == index;
        return VStack(
            Grid("32", "64",
                Border()
                    .BackgroundColor(scheme.SecondaryContainer)
                    .StrokeThickness(0)
                    .StrokeShape(new RoundRectangle().CornerRadius(16))
                    .WidthRequest(56)
                    .HeightRequest(32)
                    .HCenter()
                    .VCenter()
                    .Scale(selected ? 1 : 0.5)
                    .Opacity(selected ? 1 : 0)
                    .WithAnimation(duration: 200),
                Label(glyph)
                    .FontFamily(MaterialSymbols.FontFamily)
                    .FontSize(22)
                    .TextColor(selected ? scheme.OnSecondaryContainer : scheme.OnSurfaceVariant)
                    .HCenter()
                    .VCenter()
            ),
            Label(label)
                .FontSize(11)
                .TextColor(selected ? scheme.OnSurface : scheme.OnSurfaceVariant)
                .HCenter()
        ).Spacing(2).Padding(6, 0).OnTapped(() => SetState(s => s.Tab = index));
    }

    private static VisualNode RenderFab(MaterialScheme scheme) =>
        Button(MaterialSymbols.Add)
            .FontFamily(MaterialSymbols.FontFamily)
            .BackgroundColor(scheme.PrimaryContainer)
            .TextColor(scheme.OnPrimaryContainer)
            .FontSize(24)
            .CornerRadius(28)
            .WidthRequest(56)
            .HeightRequest(56)
            .HEnd()
            .VEnd()
            .Margin(0, 0, 16, -28); // half over the nav bar

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
