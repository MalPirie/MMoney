using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using MauiReactor.Shapes;
using Mobiorum.Material3;
using MMoney.App.Ledger;
using MMoney.Core;
using MMoney.Core.Repeat;
using MenuItem = Mobiorum.Material3.MenuItem; // disambiguate from MauiReactor.MenuItem

namespace MMoney.App.Components;

internal sealed class ShellState
{
    /// <summary>Selected destination: 0 = Transactions, 1 = Repeating.</summary>
    public int Tab { get; set; }

    /// <summary>The month shown by the Transactions TabbedPageView.</summary>
    public MonthOnly Month { get; set; }

    /// <summary>The event-sourced ledger, resolved from DI on mount.</summary>
    public AccountManager? Manager { get; set; }

    /// <summary>Whether the banner's overflow (three-dot) dropdown menu is open.</summary>
    public bool MenuOpen { get; set; }
}

/// <summary>
/// The app shell: a persistent brand banner, a swappable central area driven by the bottom navigation bar, and a
/// floating FAB. Banner / nav / FAB persist; only the central area changes. The Transactions tab is now bound to
/// the real event-sourced <see cref="Account"/> (app-design §5): month ledger rows, the real edit-lock floor, and
/// live banner balances.
/// </summary>
partial class ShellPage : Component<ShellState>
{
    private static readonly CultureInfo Gb = CultureInfo.GetCultureInfo("en-GB");

    protected override void OnMounted()
    {
        ThemePreference.Load();
        State.Manager = Services.GetRequiredService<AccountManager>();
        State.Month = MonthOnly.FromDate(State.Manager.Today);
        base.OnMounted();
    }

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var account = State.Manager?.GetAccounts().FirstOrDefault();

        return ContentPage(
            Grid("Auto,*,Auto", "*",
                RenderBanner(scheme, account).GridRow(0),
                RenderCentral(scheme, account).GridRow(1),
                RenderNavBar().GridRow(2),
                RenderFab().GridRow(1),
                RenderOverflowMenu(scheme).GridRowSpan(3) // overlay layer, on top of everything (ADR-0004)
            )
        ).HasNavigationBar(false); // we draw our own banner; the NavigationPage bar stays hidden (ADR-0005)
    }

    // ---- banner (§4) -------------------------------------------------------------------------------------

    // Today-anchored and static (the month scroller never changes it). Hero = balance today; Available = projected
    // month-end; Pending = the difference. Amounts uncoloured (on-primary).
    private VisualNode RenderBanner(MaterialScheme scheme, Account? account)
    {
        var today = State.Manager?.Today ?? DateOnly.FromDateTime(DateTime.Today);
        var hero = account?.BalanceOn(today) ?? 0m;
        var available = account?.BalanceOn(MonthOnly.FromDate(today).LastDay) ?? 0m;
        var pending = available - hero;

        return Grid("Auto,Auto", "*,Auto",
            Label(Money(hero))
                .FontSize(40)
                .TextColor(scheme.OnPrimary)
                .VCenter()
                .GridColumn(0).GridRowSpan(2),
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
                .VStart()
                .Margin(0, -4, -12, 0) // pull the 48dp target toward the top-right corner (≈16dp M3 inset)
                .OnClicked(() => SetState(s => s.MenuOpen = true))
                .GridColumn(1).GridRow(0),
            VStack(
                Label($"Available {Money(available)}").FontSize(12).TextColor(scheme.OnPrimary).HEnd(),
                Label($"Pending {Money(pending)}").FontSize(12).TextColor(scheme.OnPrimary).HEnd()
            ).Spacing(2).GridColumn(1).GridRow(1)
        ).BackgroundColor(scheme.Primary).Padding(20, 12);
    }

    private VisualNode RenderCentral(MaterialScheme scheme, Account? account) => State.Tab switch
    {
        0 => RenderTransactions(scheme, account),
        _ => RenderRepeating(scheme),
    };

    // ---- transactions ledger (§5) ------------------------------------------------------------------------

    // The generic TabbedPageView over the account's months: open-ended forward, bounded back at the real edit lock
    // (Prev returns null there). Each page is the month's ledger. When no month has been closed the edit lock is
    // DateOnly.MinValue, so the strip is effectively unbounded back (the pager's own back-cap is the safety bound).
    private VisualNode RenderTransactions(MaterialScheme scheme, Account? account)
    {
        if (account is null)
        {
            return Grid();
        }

        var today = State.Manager?.Today ?? DateOnly.FromDateTime(DateTime.Today);
        var editLock = MonthOnly.FromDate(account.EarliestAllowedDate);

        return new TabbedPageView<MonthOnly>()
            .Selected(State.Month)
            .Next(m => m.Add(1))
            .Prev(m => m.CompareTo(editLock) <= 0 ? null : m.Add(-1))
            .Label(MonthLabel)
            .Home(MonthOnly.FromDate(today))    // pinned "jump to the current month" button …
            .HomeIcon(MaterialSymbols.Today)    // … shown with a calendar-with-today icon
            .Page(m => MonthPage(scheme, account, m))
            .OnSelectedChanged(m => SetState(s => s.Month = m))
            .WithKey("transactions-pager"); // stable identity so a Month change reuses the control, not rebuilds it
    }

    // Strip label: months in the current year read as a long name with no year ("July"); other years use a short
    // name and short year ("Jul 25").
    private string MonthLabel(MonthOnly month)
    {
        var currentYear = State.Manager?.Today.Year ?? DateTime.Today.Year;
        return month.Year == currentYear
            ? month.FirstDay.ToString("MMMM", Gb)
            : month.FirstDay.ToString("MMM yy", Gb);
    }

    // One month's ledger: real LedgerEntry rows, day-grouped and reversed to newest-first (MonthLedger). Content
    // only — TabbedPageView wraps each page in its own vertical scroller (the CarouselView owns the horizontal axis).
    private VisualNode MonthPage(MaterialScheme scheme, Account account, MonthOnly month)
    {
        var entries = account.GetMonth(month);
        if (entries.Count == 0)
        {
            return Grid(
                Label("No transactions this month")
                    .FontSize(14)
                    .TextColor(scheme.OnSurfaceVariant)
                    .HCenter()
                    .VCenter()
            ).Padding(24).HeightRequest(200);
        }

        var today = State.Manager?.Today ?? DateOnly.FromDateTime(DateTime.Today);
        var rows = new List<VisualNode>();
        foreach (var day in MonthLedger.ByDayDescending(entries))
        {
            rows.Add(DayHeader(scheme, day.Date));
            rows.Add(DayBox(scheme, day.Entries, today));
        }

        return VStack([.. rows]).Spacing(8).Padding(16, 16, 16, 88); // extra bottom room so the last rows clear the FAB
    }

    private static VisualNode DayHeader(MaterialScheme scheme, DateOnly date) =>
        Label(date.ToString("ddd d MMMM", Gb))
            .FontSize(12)
            .FontAttributes(MauiControls.FontAttributes.Bold)
            .TextColor(scheme.OnSurfaceVariant)
            .Padding(4, 8, 0, 0);

    // All of a day's rows share ONE rounded container (no dividers between rows).
    private VisualNode DayBox(MaterialScheme scheme, IReadOnlyList<LedgerEntry> entries, DateOnly today) =>
        Border(VStack([.. entries.Select(entry => LedgerRow(scheme, entry, today))]).Spacing(0))
            .BackgroundColor(scheme.SurfaceContainer)
            .StrokeThickness(0)
            .StrokeShape(new RoundRectangle().CornerRadius(16)); // Border clips its content to this shape

    // One row's CONTENT (the day box provides the container): [income accent bar] description (+ trailing repeat
    // icon on occurrences) … signed amount / running balance. Metrics follow M3 list-item padding: 16dp leading and
    // trailing, 16dp between the leading element and the text, 12dp vertical; the accent bar keeps a small left
    // margin off the container edge and a vertical inset so it clears the rounded corners.
    private VisualNode LedgerRow(MaterialScheme scheme, LedgerEntry entry, DateOnly today)
    {
        var isIncome = entry.Amount > 0;

        // Accent bar for income only: green when received (date ≤ today), grey when still upcoming. Expenses get
        // a transparent bar so the left inset stays consistent.
        var accent = isIncome
            ? (entry.Date <= today ? scheme.Income : scheme.OnSurfaceVariant)
            : Colors.Transparent;

        // Columns: accent bar · gap · description(fill, wraps to 2 lines with an inline repeat glyph) · gap · amount
        // (fixed-width so amounts right-align in a tidy column).
        var row = Grid("*", "4,10,*,16,96",
                Border()
                    .BackgroundColor(accent)
                    .StrokeThickness(0)
                    .StrokeShape(new RoundRectangle().CornerRadius(2))
                    .VFill()
                    .GridColumn(0),
                DescriptionLabel(scheme, entry).GridColumn(2),
                VStack(
                    Label(Money(entry.Amount))
                        .FontSize(15)
                        .TextColor(isIncome ? scheme.Income : scheme.Expense)
                        .HEnd(),
                    Label(Money(entry.Balance))
                        .FontSize(12)
                        .TextColor(scheme.OnSurfaceVariant)
                        .HEnd()
                ).Spacing(2).VCenter().GridColumn(4)
            )
            .Padding(8, 12, 16, 12);

        // One-offs (§7) and occurrences (§8) open the editor; a carried-balance row is not a real transaction, so it
        // stays non-tappable. Occurrence edits route through the scope dialog inside the editor.
        return entry.Kind == LedgerEntryKind.CarriedBalance
            ? row
            : row.OnTapped(() => OpenEdit(entry.Transaction));
    }

    // Description wraps to two lines then truncates. On an occurrence a muted repeat glyph is an INLINE trailing span
    // (FormattedString), so it flows right after the text — and after the last wrapped word — rather than sitting in
    // a rigid column. Built with the functional Label(Span(...), Span(...)) form; the collection-initializer form
    // does not build a FormattedString.
    private static VisualNode DescriptionLabel(MaterialScheme scheme, LedgerEntry entry)
    {
        if (entry.Kind != LedgerEntryKind.Occurrence)
        {
            return Label(entry.Description)
                .FontSize(15)
                .TextColor(scheme.OnSurface)
                .MaxLines(2)
                .LineBreakMode(LineBreakMode.TailTruncation)
                .VCenter();
        }

        // The description then an inline trailing repeat glyph (its own font), as MauiReactor spans wrapped in a
        // FormattedString node — the spans must live inside FormattedString(...), not directly under the Label.
        return Label(
                FormattedString(
                    Span().Text(entry.Description).TextColor(scheme.OnSurface),
                    Span().Text("  " + MaterialSymbols.Repeat)
                        .FontFamily(MaterialSymbols.FontFamily)
                        .FontSize(12)
                        .TextColor(scheme.OnSurfaceVariant)
                )
            )
            .FontSize(15)
            .MaxLines(2)
            .LineBreakMode(LineBreakMode.TailTruncation)
            .VCenter();
    }

    private static string Money(decimal value) => value.ToString("C", Gb);

    // ---- repeating tab (§6): the active repeating sequences ----------------------------------------------

    // Upcoming sequences (those with an occurrence on/after today), filtered and sorted next-due ascending by
    // Account.GetUpcomingSequences. One rounded surfaceContainer box (the day-box idiom) holds the rows; each row
    // shows description · signed amount over recurrence (+ end condition) · next-due. Tapping a row will open
    // whole-series edit (§8) — a no-op target for now.
    private VisualNode RenderRepeating(MaterialScheme scheme)
    {
        var account = State.Manager?.GetAccounts().FirstOrDefault();
        var today = State.Manager?.Today ?? DateOnly.FromDateTime(DateTime.Today);
        var upcoming = account?.GetUpcomingSequences(today) ?? [];

        if (upcoming.Count == 0)
        {
            return Grid(
                Label("No repeating transactions")
                    .FontSize(14)
                    .TextColor(scheme.OnSurfaceVariant)
                    .HCenter()
                    .VCenter()
            ).Padding(24);
        }

        return ScrollView(
            Border(VStack([.. upcoming.Select(u => RepeatingRow(scheme, u))]).Spacing(0))
                .BackgroundColor(scheme.SurfaceContainer)
                .StrokeThickness(0)
                .StrokeShape(new RoundRectangle().CornerRadius(16))
                .Margin(16, 16, 16, 88) // bottom room so the last row clears the FAB
        );
    }

    private VisualNode RepeatingRow(MaterialScheme scheme, UpcomingSequence upcoming)
    {
        var seq = upcoming.Sequence;
        var isIncome = seq.Amount > 0;
        var recurrence = RepeatDescription.Describe(seq.Strategy, seq.Origin)
            + RepeatDescription.DescribeEndCondition(seq.EndCondition, seq.Schedule.EndDate());

        return Grid("Auto,Auto", "*,Auto",
                Label(seq.Description)
                    .FontSize(15)
                    .TextColor(scheme.OnSurface)
                    .MaxLines(1)
                    .LineBreakMode(LineBreakMode.TailTruncation)
                    .VCenter()
                    .GridRow(0).GridColumn(0),
                Label(Money(seq.Amount))
                    .FontSize(15)
                    .TextColor(isIncome ? scheme.Income : scheme.Expense)
                    .HEnd()
                    .VCenter()
                    .GridRow(0).GridColumn(1),
                Label(recurrence)
                    .FontSize(12)
                    .TextColor(scheme.OnSurfaceVariant)
                    .MaxLines(1)
                    .LineBreakMode(LineBreakMode.TailTruncation)
                    .GridRow(1).GridColumn(0),
                Label($"next: {upcoming.NextDue.ToString("d MMM", Gb)}")
                    .FontSize(12)
                    .TextColor(scheme.OnSurfaceVariant)
                    .HEnd()
                    .GridRow(1).GridColumn(1)
            )
            .Padding(16, 12)
            .ColumnSpacing(16)
            .OnTapped(() => { }); // whole-series edit is §8
    }

    // ---- chrome ------------------------------------------------------------------------------------------

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

    // The banner's three-dot overflow, as an M3 dropdown (ADR-0004). An app-owned overlay layer: a transparent
    // full-page tap-catcher that dismisses on an outside tap, with the generic Menu surface anchored top-right so it
    // covers the trigger. Menus are not modal, so there is no dimming scrim (that is reserved for dialogs). The layer
    // stays in the tree so the open/close fade+scale animates, and is input-transparent while closed so touches pass
    // through to the banner and ledger beneath.
    private VisualNode RenderOverflowMenu(MaterialScheme scheme)
    {
        var open = State.MenuOpen;

        // Deterministic placement via fixed inset tracks + GridRow/GridColumn (which work on a Component, unlike
        // the IView-only HEnd/Margin). The button's measured bounds put its top-right corner 8dp in from the top
        // and 8dp in from the right of the content area, so 8dp inset tracks align the menu's top-right to it.
        return Grid("8,Auto,*", "*,Auto,8",
                Border()
                    .BackgroundColor(Colors.Transparent) // non-null ⇒ catches outside taps; no dim
                    .StrokeThickness(0)
                    .OnTapped(CloseMenu)
                    .GridRowSpan(3).GridColumnSpan(3),
                new Menu()
                    .IsOpen(open) // the open/close fade+scale is the control's own concern now
                    .Items([
                        new MenuItem(MaterialSymbols.Print, "Print").OnSelected(CloseMenu),
                        new MenuItem(MaterialSymbols.Download, "Export").OnSelected(CloseMenu),
                        new MenuItem(MaterialSymbols.Settings, "Settings").OnSelected(OpenSettings)
                    ])
                    .GridRow(1).GridColumn(1)
            )
            .InputTransparent(!open); // closed: let touches through; open: catch outside taps
    }

    private void CloseMenu() => SetState(s => s.MenuOpen = false);

    // Dismiss the overflow menu, then push the Settings page onto the navigation stack (ADR-0005). Fire-and-forget:
    // the OnSelected callback is synchronous and the push manages its own transition.
    private void OpenSettings()
    {
        CloseMenu();
        _ = Navigation?.PushAsync<SettingsPage>();
    }

    private VisualNode RenderFab() =>
        Grid(
            new Fab()
                .Icon(MaterialSymbols.Add)
                .OnClicked(OpenAdd) // the unified add flow (§7)
        )
        .HEnd()
        .VEnd()
        .Margin(0, 0, 16, -28);

    // Push the Add-transaction page (§7). We push a pre-configured instance (not PushAsync<T>, which mints its own
    // and has no props hook) so the page carries the OnClosed callback that reports its outcome back to the shell.
    private void OpenAdd() =>
        _ = Navigation?.PushAsync<AddTransactionPage, AddTransactionProps>(props => props.OnClosed = OnTransactionClosed);

    // Push the same page in edit mode, seeded from the tapped transaction — a one-off (§7) or an occurrence (§8, where
    // Save routes through the scope dialog). On save it jumps the ledger to the date via the same OnClosed callback.
    private void OpenEdit(Transaction transaction) =>
        _ = Navigation?.PushAsync<AddTransactionPage, AddTransactionProps>(props =>
        {
            props.Edit = transaction;
            props.OnClosed = OnTransactionClosed;
        });

    // When a transaction was saved, jump the ledger to its month (SetState also re-renders, so the new row shows).
    private void OnTransactionClosed(TransactionOutcome outcome)
    {
        if (outcome.Result == TransactionResult.Saved)
        {
            SetState(s => s.Month = MonthOnly.FromDate(outcome.Date));
        }
    }
}
