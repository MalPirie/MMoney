using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using MauiReactor;
using Mobiorum.Material3;
using MMoney.App.Input;
using MMoney.Core;

namespace MMoney.App.Components;

/// <summary>What a pushed transaction editor did, reported back to the shell so it can react (jump/refresh).</summary>
internal enum TransactionResult
{
    /// <summary>Dismissed with no change (back/cancel).</summary>
    None,

    /// <summary>A transaction was added or edited on <see cref="TransactionOutcome.Date"/>.</summary>
    Saved,

    /// <summary>A transaction was deleted (edit mode — §7 later slice).</summary>
    Deleted,
}

/// <summary>The outcome a transaction editor hands back through its <c>OnClosed</c> callback.</summary>
internal readonly record struct TransactionOutcome(TransactionResult Result, DateOnly Date);

/// <summary>Props passed to a pushed <see cref="AddTransactionPage"/> (MauiReactor's props-initializer navigation).</summary>
internal sealed class AddTransactionProps
{
    /// <summary>Invoked as the page closes, reporting what it did so the shell can react.</summary>
    public Action<TransactionOutcome>? OnClosed { get; set; }
}

internal sealed class AddTransactionState
{
    public AccountManager? Manager { get; set; }
    public DateOnly Date { get; set; }             // the committed (last-good) date
    public string DateText { get; set; } = string.Empty; // the date field's raw/masked text buffer
    public string AmountText { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsIncome { get; set; } // default false = Expense (app-design §7)
    public bool DateActive { get; set; } // the date field reads as the active/focused one (survives opening the calendar)
    public string? DateError { get; set; }
    public string? AmountError { get; set; }
    public string? DescriptionError { get; set; }
    public bool CalendarOpen { get; set; } // whether the M3 calendar dialog is showing (ADR-0006)
}

/// <summary>
/// The Add-transaction page (app-design §7, first slice): a one-off transaction pushed from the FAB onto the nav
/// stack (ADR-0005). Fields — Date (defaults today, ≥ edit lock), Amount (unsigned magnitude), Description, and
/// the income/expense <see cref="LabeledToggle"/> — with validate-on-save shown as inline field errors. On save it
/// calls <see cref="Account.AddTransaction"/> (which auto-persists) and reports the saved date back through
/// <c>OnClosed</c> so the shell can jump the ledger to it. Repeat is pinned to "Does not repeat" (the §8 strategy
/// page is deferred), and Edit / Delete / Copy are later slices.
/// </summary>
partial class AddTransactionPage : Component<AddTransactionState, AddTransactionProps>
{
    private static readonly CultureInfo Gb = CultureInfo.GetCultureInfo("en-GB");

#if ANDROID
    // Android hardware-back handler: while enabled it cancels the open calendar dialog instead of letting back pop
    // the whole page (ADR-0006). MauiReactor 4.0.15 exposes no page back hook, so we drive it at the platform layer.
    private CalendarBackCallback? _backCallback;
#endif

    protected override void OnMounted()
    {
        State.Manager = Services.GetRequiredService<AccountManager>();
        State.Date = State.Manager.Today;
        State.DateText = DateEntry.Format(State.Date);

#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is AndroidX.Activity.ComponentActivity activity)
        {
            _backCallback = new CalendarBackCallback(CancelCalendar);
            activity.OnBackPressedDispatcher.AddCallback(_backCallback);
        }
#endif
        base.OnMounted();
    }

    protected override void OnWillUnmount()
    {
#if ANDROID
        _backCallback?.Remove();
        _backCallback = null;
#endif
        base.OnWillUnmount();
    }

    // Enable back-interception only while the dialog is open, so a closed-dialog back still pops the page normally.
    private void SetBackInterception(bool enabled)
    {
#if ANDROID
        if (_backCallback is not null)
        {
            _backCallback.Enabled = enabled;
        }
#endif
    }

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;

        var content = new List<VisualNode>
        {
            new TopAppBar()
                .Title("Add transaction")
                .Container(scheme.Primary)
                .OnContainer(scheme.OnPrimary)
                .OnBack(Cancel)
                .ActionText("Save")
                .OnAction(Save)
                .GridRow(0),
            ScrollView(
                    VStack(
                        DateField(scheme),
                        // Amount and its income/expense type share one full-width bordered field — the type drives
                        // the amount's sign, so the In/Out toggle lives inside the field's bounds, trailing the entry.
                        new TextField()
                            .Label("Amount")
                            .Text(State.AmountText)
                            .Placeholder("0.00")
                            .Keyboard(Keyboard.Numeric)
                            .Error(State.AmountError)
                            .OnFocused(() => SetState(s => s.DateActive = false))
                            .OnTextChanged(OnAmountChanged)
                            .Trailing(
                                Grid(
                                    new LabeledToggle()
                                        .OffLabel("Out").OnLabel("In")
                                        .OffColor(scheme.Expense).OnColor(scheme.Income)
                                        .IsOn(State.IsIncome)
                                        .OnToggled(v => SetState(s => s.IsIncome = v))
                                ).WidthRequest(120).VCenter()
                            ),
                        new TextField()
                            .Label("Description")
                            .Text(State.Description)
                            .Placeholder("e.g. Groceries")
                            .Keyboard(Keyboard.Create(KeyboardFlags.CapitalizeSentence)) // leading capital
                            .Error(State.DescriptionError)
                            .OnFocused(() => SetState(s => s.DateActive = false))
                            .OnTextChanged(OnDescriptionChanged),
                        RepeatField(scheme)
                    ).Spacing(20).Padding(16)
                ).GridRow(1),
        };

        // The M3 calendar dialog is the app's first modal overlay (ADR-0006): a dimmed scrim + centred card layered
        // over the page's rows, hosted here on the pushed page (not the shell), and in the tree only while open.
        if (State.CalendarOpen)
        {
            content.Add(CalendarOverlay(scheme).GridRowSpan(2));
        }

        return ContentPage(
            Grid("Auto,*", "*", [.. content])
        ).HasNavigationBar(false);
    }

    // The edit-lock floor as a nullable minimum: null when no month has been closed (unbounded back), otherwise the
    // earliest allowed date. Drives both the calendar's disabled days and manual-entry range validation.
    private DateOnly? MinDate()
    {
        var account = State.Manager?.GetAccounts().FirstOrDefault();
        var editLock = account?.EarliestAllowedDate ?? DateOnly.MinValue;
        return editLock.Year < 1900 ? null : editLock;
    }

    private bool InRange(DateOnly date) => MinDate() is not { } min || date >= min;

    // The scrim + centred Calendar. Star tracks around an Auto centre cell centre the card (HCenter/VCenter don't
    // apply to a Component's root; GridRow/GridColumn placement does — ADR-0004).
    private VisualNode CalendarOverlay(MaterialScheme scheme) =>
        Grid("*,Auto,*", "*,Auto,*",
            Border()
                .BackgroundColor(Colors.Black.WithAlpha(0.32f)) // M3 modal scrim
                .StrokeThickness(0)
                .OnTapped(CancelCalendar)
                .GridRowSpan(3).GridColumnSpan(3),
            new Mobiorum.Material3.Calendar()
                .SelectedDate(State.Date)
                .MinDate(MinDate())
                .Today(State.Manager?.Today ?? DateOnly.FromDateTime(DateTime.Today))
                .OnConfirm(ConfirmCalendar)
                .OnCancel(CancelCalendar)
                .GridRow(1).GridColumn(1)
        );

    // The dual-mode date field (ADR-0006): the outlined TextField in text mode for manual dd/MM/yyyy entry, with a
    // trailing calendar button that opens the M3 Calendar dialog. The weekday rides as the supporting hint (yielding
    // to the error line). Tapping the entry types; tapping the button picks.
    private VisualNode DateField(MaterialScheme scheme) =>
        new TextField()
            .Label("Date")
            .Text(State.DateText)
            .Placeholder("dd/mm/yyyy")
            .Keyboard(Keyboard.Numeric)
            .Supporting(DateEntry.Weekday(State.Date))
            .Error(State.DateError)
            .Active(State.DateActive) // stays lit through the calendar and after (see OpenCalendar / the other fields)
            .OnFocused(() => SetState(s => s.DateActive = true))
            .OnTextChanged(OnDateChanged)
            .Trailing(
                Button(MaterialSymbols.CalendarMonth)
                    .FontFamily(MaterialSymbols.FontFamily)
                    .FontSize(22)
                    .BackgroundColor(Colors.Transparent)
                    .TextColor(scheme.OnSurfaceVariant)
                    .Padding(0)
                    .CornerRadius(20)
                    .WidthRequest(40)
                    .HeightRequest(40)
                    .VCenter()
                    .OnClicked(OpenCalendar)
            );

    // The Repeat field is a disabled placeholder for now — it will open the §8 repeat-strategy page in a later slice.
    // Its static "Does not repeat" value rides the same outlined frame via the Content slot.
    private static VisualNode RepeatField(MaterialScheme scheme) =>
        new TextField()
            .Label("Repeat")
            .Content(
                Label("Does not repeat")
                    .FontSize(16)
                    .TextColor(scheme.OnSurfaceVariant)
                    .VCenter()
            );

    // Mask to dd/MM/yyyy as typed; live-commit on a complete, in-range date (keeping the last-good Date otherwise)
    // and clear the error once valid. The below-lock / incomplete cases are surfaced on Save, per the page's pattern.
    private void OnDateChanged(string text)
    {
        var masked = DateEntry.Mask(text);
        SetState(s =>
        {
            s.DateText = masked;
            if (DateEntry.TryParse(masked, out var date) && InRange(date))
            {
                s.Date = date;
                s.DateError = null;
            }
        });
    }

    // Open the calendar dialog; dismiss the soft keyboard first so it doesn't fight the modal, and arm hardware-back.
    private void OpenCalendar()
    {
        HideKeyboard();
        SetBackInterception(true);
        SetState(s =>
        {
            s.CalendarOpen = true;
            s.DateActive = true; // the calendar belongs to the date field — light it and keep it lit
        });
    }

    private void CancelCalendar()
    {
        SetBackInterception(false);
        SetState(s => s.CalendarOpen = false);
    }

    // OK: the picked date overwrites both the committed value and the text buffer, and closes the dialog.
    private void ConfirmCalendar(DateOnly date)
    {
        SetBackInterception(false);
        SetState(s =>
        {
            s.Date = date;
            s.DateText = DateEntry.Format(date);
            s.DateError = null;
            s.CalendarOpen = false;
        });
    }

    // Strip any minus as typed (unsigned magnitude — sign is the toggle's job) and clear the error once valid.
    private void OnAmountChanged(string text)
    {
        var cleaned = text.Replace("-", string.Empty);
        SetState(s =>
        {
            s.AmountText = cleaned;
            if (TryAmount(cleaned, out _))
            {
                s.AmountError = null;
            }
        });
    }

    private void OnDescriptionChanged(string text) =>
        SetState(s =>
        {
            s.Description = text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                s.DescriptionError = null;
            }
        });

    // Parses the amount text to a positive magnitude; false if blank/unparseable/zero.
    private static bool TryAmount(string text, out decimal magnitude)
    {
        if (decimal.TryParse(text, NumberStyles.Number, Gb, out var value))
        {
            magnitude = Math.Abs(value);
            return magnitude > 0;
        }

        magnitude = 0;
        return false;
    }

    private async void Cancel()
    {
        await DismissKeyboardThenPop();
        Props.OnClosed?.Invoke(new TransactionOutcome(TransactionResult.None, State.Date));
    }

    private async void Save()
    {
        var account = State.Manager?.GetAccounts().FirstOrDefault();
        if (account is null)
        {
            return;
        }

        var dateError = !DateEntry.TryParse(State.DateText, out var date) ? "Enter a valid date"
            : !InRange(date) ? "Date is in a locked month"
            : null;
        var amountError = TryAmount(State.AmountText, out var magnitude) ? null : "Enter an amount greater than zero";
        var descriptionError = string.IsNullOrWhiteSpace(State.Description) ? "Enter a description" : null;

        if (dateError is not null || amountError is not null || descriptionError is not null)
        {
            SetState(s =>
            {
                s.DateError = dateError;
                s.AmountError = amountError;
                s.DescriptionError = descriptionError;
            });
            return;
        }

        var signed = State.IsIncome ? magnitude : -magnitude;
        account.AddTransaction(date, signed, State.Description.Trim());

        await DismissKeyboardThenPop();
        Props.OnClosed?.Invoke(new TransactionOutcome(TransactionResult.Saved, date));
    }

    // Dismiss the soft keyboard and let it finish animating away before popping — otherwise the reappearing shell is
    // laid out mid-dismiss and its banner settles/snaps as the keyboard closes. Only wait when a field was focused.
    private async Task DismissKeyboardThenPop()
    {
        if (HideKeyboard())
        {
            await Task.Delay(220);
        }

        if (Navigation is { } nav)
        {
            await nav.PopAsync();
        }
    }

    // Hides the soft keyboard; returns whether anything was focused (i.e. the keyboard was up).
    private static bool HideKeyboard()
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var focus = activity?.CurrentFocus;
        if (activity is not null && focus is not null)
        {
            var imm = (Android.Views.InputMethods.InputMethodManager?)
                activity.GetSystemService(Android.Content.Context.InputMethodService);
            imm?.HideSoftInputFromWindow(focus.WindowToken, Android.Views.InputMethods.HideSoftInputFlags.None);
            focus.ClearFocus();
            return true;
        }
#endif
        return false;
    }

#if ANDROID
    // Cancels the open calendar dialog on hardware back. Registered disabled; armed only while the dialog is open,
    // so a closed-dialog back falls through to the normal page pop.
    private sealed class CalendarBackCallback(Action onBack) : AndroidX.Activity.OnBackPressedCallback(enabled: false)
    {
        public override void HandleOnBackPressed() => onBack();
    }
#endif
}
