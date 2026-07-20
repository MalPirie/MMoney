using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using MauiReactor.Shapes;
using Mobiorum.Material3;
using MMoney.App.Export;
using MMoney.App.Ledger;
using MMoney.App.Platform;
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

    /// <summary>Whether the "deleted" Snackbar is showing, its Undo payload, and a token guarding the auto-dismiss
    /// so a later delete's timer can't hide an earlier one (§7).</summary>
    public bool SnackbarOpen { get; set; }
    public DeletedUndo? SnackbarUndo { get; set; }
    public int SnackbarToken { get; set; }
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
                RenderFab(account).GridRow(1),
                RenderSnackbar().GridRow(1), // deleted-transaction Undo, bottom of the central area
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
        var currentMonth = MonthOnly.FromDate(today);
        var editLock = MonthOnly.FromDate(account.EarliestAllowedDate);

        // Lower bound of the (forward-infinite) scroller: the month of the first transaction, but never before the
        // edit lock and never after the current month — so a fresh/empty account, or one whose only activity is in
        // the future, still opens bounded at the current month rather than scrolling back to year one.
        var earliestContent = account.EarliestContentMonth() ?? currentMonth;
        var lowerContent = earliestContent.CompareTo(currentMonth) < 0 ? earliestContent : currentMonth;
        var firstMonth = editLock.CompareTo(lowerContent) > 0 ? editLock : lowerContent;

        return new TabbedPageView<MonthOnly>()
            .Selected(State.Month)
            .Next(m => m.Add(1))
            .Prev(m => m.CompareTo(firstMonth) <= 0 ? null : m.Add(-1))
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

        // The carried-balance anchor is not a dated transaction — pull it out of the day grouping and render it as its
        // own darker, header-less box at the foot of the month (it is always the earliest entry).
        var carried = entries.FirstOrDefault(e => e.Kind == LedgerEntryKind.CarriedBalance);
        var dated = entries.Where(e => e.Kind != LedgerEntryKind.CarriedBalance).ToList();

        var rows = new List<VisualNode>();
        foreach (var day in MonthLedger.ByDayDescending(dated))
        {
            rows.Add(DayHeader(scheme, day.Date));
            rows.Add(DayBox(scheme, day.Entries, today));
        }
        if (carried is not null)
        {
            rows.Add(CarriedBalanceBox(scheme, carried));
        }

        return VStack([.. rows]).Spacing(8).Padding(16, 16, 16, 88); // extra bottom room so the last rows clear the FAB
    }

    // The carried-balance anchor: its own box, darker than the day boxes and with no date header, since it has no real
    // date — it is the opening balance a month close rolled forward.
    private static VisualNode CarriedBalanceBox(MaterialScheme scheme, LedgerEntry carried) =>
        Border(
            Grid("*", "*,Auto",
                Label("Balance carried")
                    .FontSize(15)
                    .TextColor(scheme.OnSurface)
                    .VCenter()
                    .GridColumn(0),
                Label(Money(carried.Balance))
                    .FontSize(15)
                    .TextColor(carried.Amount > 0 ? scheme.Income : scheme.Expense)
                    .HEnd()
                    .VCenter()
                    .GridColumn(1)
            ).Padding(16, 14)
        )
        .BackgroundColor(scheme.SurfaceContainerHighest) // darker than the ordinary day boxes
        .StrokeThickness(0)
        .StrokeShape(new RoundRectangle().CornerRadius(16))
        .Margin(0, 8, 0, 0); // a little extra separation from the day boxes above

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

        // Each repeating transaction is its own rounded card (not one shared box), stacked with a small gap.
        return ScrollView(
            VStack([.. upcoming.Select(u =>
                Border(RepeatingRow(scheme, u))
                    .BackgroundColor(scheme.SurfaceContainer)
                    .StrokeThickness(0)
                    .StrokeShape(new RoundRectangle().CornerRadius(16))
            )]).Spacing(8).Margin(16, 16, 16, 88) // bottom room so the last card clears the FAB
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
            .OnTapped(() => OpenWholeSeries(seq)); // whole-series edit (§8)
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
                        new MenuItem(MaterialSymbols.Print, "Print").OnSelected(PrintStatement),
                        new MenuItem(MaterialSymbols.Download, "Export").OnSelected(ExportCsv),
                        new MenuItem(MaterialSymbols.Settings, "Settings").OnSelected(OpenSettings)
                    ])
                    .GridRow(1).GridColumn(1)
            )
            .InputTransparent(!open); // closed: let touches through; open: catch outside taps
    }

    private void CloseMenu() => SetState(s => s.MenuOpen = false);

    // Prefix for the exported statement's file name; the picked destination is the OS share sheet's concern.
    private const string ExportFilePrefix = "mmoney-statement";

    // Export the whole-account ledger as a CSV statement and hand it to the platform share sheet, which is what
    // chooses where the file goes (Drive, Files, email, …). The overflow menu's Export item (§4).
    private void ExportCsv()
    {
        CloseMenu();

        var manager = State.Manager;
        var account = manager?.GetAccounts().FirstOrDefault();
        if (manager is null || account is null)
        {
            return;
        }

        var csv = LedgerExport.ToCsv(LedgerExport.CollectRows(account, manager.Today));
        _ = ShareCsvAsync(csv, manager.Today);
    }

    // Write the CSV to a cache file (a UTF-8 BOM so spreadsheets detect the encoding), then raise the share sheet.
    // Fire-and-forget from ExportCsv: the share UI owns its own lifecycle and there is no ledger state to update.
    private static async Task ShareCsvAsync(string csv, DateOnly today)
    {
        // System.IO.Path fully qualified: bare Path binds MauiReactor's Component.Path() shape factory here.
        var path = System.IO.Path.Combine(FileSystem.CacheDirectory, $"{ExportFilePrefix}-{today:yyyyMMdd}.csv");
        await File.WriteAllTextAsync(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export transactions",
            File = new ShareFile(path),
        });
    }

    // Print the whole-account ledger via the platform print system (Android's print dialog, incl. Save as PDF).
    // Same rows and scope as Export — only the rendering differs (a human-readable HTML statement). The overflow
    // menu's Print item (§4).
    private void PrintStatement()
    {
        CloseMenu();

        var manager = State.Manager;
        var account = manager?.GetAccounts().FirstOrDefault();
        if (manager is null || account is null)
        {
            return;
        }

        var rows = LedgerExport.CollectRows(account, manager.Today);
        var title = string.IsNullOrWhiteSpace(account.Name) ? "MMoney" : account.Name;
        LedgerPrinter.Print(LedgerHtml.Build(rows, title, manager.Today), $"{title} statement");
    }

    // Dismiss the overflow menu, then push the Settings page onto the navigation stack (ADR-0005). Fire-and-forget:
    // the OnSelected callback is synchronous and the push manages its own transition.
    private void OpenSettings()
    {
        CloseMenu();
        _ = Navigation?.PushAsync<SettingsPage, SettingsProps>(props => props.OnChanged = OnSettingsChanged);
    }

    // Settings toggled "allow closing months", which reloads every account under the new mode (collapsed vs.
    // visible-read-only). Re-render so the ledger and balances reflect it; the clamp keeps the view at/above the edit
    // lock as a safety net.
    private void OnSettingsChanged() => SetState(s =>
    {
        if (s.Manager?.GetAccounts().FirstOrDefault() is { } account)
        {
            var editLock = MonthOnly.FromDate(account.EarliestAllowedDate);
            if (s.Month.CompareTo(editLock) < 0)
            {
                s.Month = editLock;
            }
        }
    });

    // The primary add FAB, with a secondary "close month" FAB stacked above it whenever the shown month is the one
    // that can be closed (§9). ClosableMonth returns null while closing is off (ignoreMonthClosed), so the secondary
    // FAB is implicitly gated by the Settings preference.
    private VisualNode RenderFab(Account? account)
    {
        var today = State.Manager?.Today ?? DateOnly.FromDateTime(DateTime.Today);
        var canCloseShownMonth = account?.ClosableMonth(today) is { } closable && closable == State.Month;

        // Each FAB is wrapped in a Grid because layout options on a Component root are no-ops (ADR-0004). Both hover
        // over the bottom nav bar at the same level: the primary is bottom-right, the small secondary sits to its left
        // (right margin = 16 + 56 + 16 gap) and is nudged down so its 40dp body centres against the 56dp primary.
        var children = new List<VisualNode>();
        if (canCloseShownMonth)
        {
            children.Add(
                Grid(new Fab().Small(true).Secondary(true).Icon(MaterialSymbols.Archive).OnClicked(CloseShownMonth))
                    .HEnd().VEnd().Margin(0, 0, 88, -20));
        }
        children.Add(
            Grid(new Fab().Icon(MaterialSymbols.Add).OnClicked(OpenAdd)) // the unified add flow (§7)
                .HEnd().VEnd().Margin(0, 0, 16, -28));

        return Grid([.. children]);
    }

    // Close the shown month (only reachable via the secondary FAB, which appears only when it is closable). No
    // confirmation: a close is reversible by turning "Allow closing months" off (the month reappears read-only). Snap
    // the view up to the advanced edit lock so we don't sit on the now-collapsed month.
    private void CloseShownMonth()
    {
        var account = State.Manager?.GetAccounts().FirstOrDefault();
        if (account is null || State.Manager is not { } manager)
        {
            return;
        }

        try
        {
            account.CloseMonth(State.Month, manager.Today); // auto-persists via AccountManager
        }
        catch (InvalidOperationException)
        {
            return; // stopped being closable under us
        }

        SetState(s =>
        {
            var editLock = MonthOnly.FromDate(account.EarliestAllowedDate);
            if (s.Month.CompareTo(editLock) < 0)
            {
                s.Month = editLock;
            }
        });
    }

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

    // Push the editor in whole-series mode (§8), seeded from the sequence's origin transaction. Every field is
    // editable and the change applies to the whole series (no scope dialog).
    private void OpenWholeSeries(Sequence sequence) =>
        _ = Navigation?.PushAsync<AddTransactionPage, AddTransactionProps>(props =>
        {
            props.Edit = new Transaction(sequence.Id, sequence.Amount, sequence.Description);
            props.WholeSeries = true;
            props.OnClosed = OnTransactionClosed;
        });

    // React to an editor closing. Saved: jump the ledger to its month. Deleted: jump to the month it was in and raise
    // the Undo Snackbar, auto-dismissing it after ~4s unless a newer delete has replaced it (token check).
    private async void OnTransactionClosed(TransactionOutcome outcome)
    {
        if (outcome.Result == TransactionResult.Saved)
        {
            SetState(s => s.Month = MonthOnly.FromDate(outcome.Date));
            return;
        }

        if (outcome.Result == TransactionResult.Deleted)
        {
            var token = State.SnackbarToken + 1;
            SetState(s =>
            {
                s.Month = MonthOnly.FromDate(outcome.Date);
                s.SnackbarOpen = true;
                s.SnackbarUndo = outcome.Undo;
                s.SnackbarToken = token;
            });

            await Task.Delay(4000);
            SetState(s =>
            {
                if (s.SnackbarToken == token)
                {
                    s.SnackbarOpen = false;
                }
            });
        }
    }

    // Undo re-creates the deleted transaction (a fresh sequence / detached one-off — never a true reversal, §7) and
    // hides the Snackbar, jumping the ledger to the re-created date. The payload is captured by the render-time
    // closure (RenderSnackbar), not read from State here — a deferred callback runs on a stale instance whose State
    // snapshot may not carry it (the MauiReactor field-vs-State trap).
    private void UndoDelete(DeletedUndo undo)
    {
        State.Manager?.GetAccounts().FirstOrDefault()
            ?.AddTransaction(undo.Date, undo.Amount, undo.Description, undo.Strategy, undo.EndCondition);
        SetState(s => { s.SnackbarOpen = false; s.Month = MonthOnly.FromDate(undo.Date); });
    }

    // The deleted-transaction Snackbar, pinned to the bottom of the central area (above the nav bar) via a VEnd
    // wrapper grid — VEnd on a Component root is a no-op (ADR-0004), so it rides a grid like the FAB does. An empty
    // node when hidden; the wrapper sizes to the bar so the ledger above stays tappable.
    private VisualNode RenderSnackbar()
    {
        if (!State.SnackbarOpen || State.SnackbarUndo is not { } undo)
        {
            return Grid();
        }

        return Grid(
            new Snackbar()
                .Message("Transaction deleted")
                .ActionText("Undo")
                .OnAction(() => UndoDelete(undo))
        ).VEnd();
    }
}
