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
using MMoney.App.Repeat;
using MMoney.Core;
using MMoney.Core.Repeat;

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

    /// <summary>
    /// The transaction to edit, or <see langword="null"/> to add a new one. Only one-off transactions are edited
    /// here (§7); occurrence editing needs the §8 scope dialog, so the shell only opens edit for one-offs.
    /// </summary>
    public Transaction? Edit { get; set; }
}

internal sealed class AddTransactionState
{
    public AccountManager? Manager { get; set; }
    public Transaction? Edit { get; set; }         // the one-off being edited (null in add mode)
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

    // Repeat rule (§8): the committed strategy/end, plus the session's pinned custom slot and the preset-dialog flag.
    public RepeatStrategy Strategy { get; set; } = new RepeatStrategy.Never();
    public RepeatEndCondition EndCondition { get; set; } = new RepeatEndCondition.Forever();
    public RepeatStrategy? CustomStrategy { get; set; }
    public RepeatEndCondition? CustomEnd { get; set; }
    public bool RepeatDialogOpen { get; set; }
}

/// <summary>
/// The Add/Edit-transaction page (app-design §7): a transaction pushed onto the nav stack (ADR-0005) either from the
/// FAB (add) or by tapping a one-off ledger row (edit, when <see cref="AddTransactionProps.Edit"/> is set). Fields —
/// Date (defaults today, ≥ edit lock), Amount (unsigned magnitude), Description, and the income/expense
/// <see cref="LabeledToggle"/> — with validate-on-save shown as inline field errors. On save, add calls
/// <see cref="Account.AddTransaction"/> and edit diffs against the original into <c>ChangeTransaction*</c> calls
/// (both auto-persist); the saved date is reported back through <c>OnClosed</c> so the shell can jump the ledger to
/// it. In edit mode the Repeat field is a read-only summary — a one-off carries no rule, and occurrence/repeat-rule
/// editing (with the This/This-and-following/All scope dialog) is deferred to §8. Delete / Copy are later slices.
/// </summary>
partial class AddTransactionPage : Component<AddTransactionState, AddTransactionProps>
{
    private static readonly CultureInfo Gb = CultureInfo.GetCultureInfo("en-GB");

    protected override void OnMounted()
    {
        State.Manager = Services.GetRequiredService<AccountManager>();

        if (Props.Edit is { } edit)
        {
            // Edit mode (§7): seed the fields from the stored one-off. A one-off carries no repeat rule, so the
            // Repeat field stays "Does not repeat" and read-only — changing the rule is deferred to §8.
            State.Edit = edit;
            State.Date = edit.Date;
            State.AmountText = Math.Abs(edit.Amount).ToString("0.##", Gb);
            State.IsIncome = edit.Amount > 0;
            State.Description = edit.Description;
        }
        else
        {
            State.Date = State.Manager.Today;
        }

        State.DateText = DateEntry.Format(State.Date);
        base.OnMounted();
    }

    private bool IsEdit => State.Edit is not null;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;

        var content = new List<VisualNode>
        {
            new TopAppBar()
                .Title(IsEdit ? "Edit transaction" : "Add transaction")
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
            // Modal dialogs (ADR-0007): each ModalHost stays in the tree spanning both rows, shows its scrim + centred
            // surface only while open, and owns the Android-back-to-dismiss. The surface mounts fresh on open.
            new ModalHost()
                .IsOpen(State.CalendarOpen)
                .OnDismiss(CancelCalendar)
                .Content(CalendarSurface())
                .GridRowSpan(2),
            new ModalHost()
                .IsOpen(State.RepeatDialogOpen)
                .OnDismiss(CloseRepeatDialog)
                .Content(RepeatDialog())
                .GridRowSpan(2),
        };

        // ModalAwareContentPage, not ContentPage: it routes hardware back to the open ModalHost (ADR-0007).
        return new ModalAwareContentPage(
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

    // The Calendar surface (ModalHost supplies the scrim + centring + back-to-dismiss).
    private VisualNode CalendarSurface() =>
        new Mobiorum.Material3.Calendar()
            .SelectedDate(State.Date)
            .MinDate(MinDate())
            .Today(State.Manager?.Today ?? DateOnly.FromDateTime(DateTime.Today))
            .OnConfirm(ConfirmCalendar)
            .OnCancel(CancelCalendar);

    // ---- repeat rule (§8) --------------------------------------------------------------------------------

    private DateOnly Today() => State.Manager?.Today ?? DateOnly.FromDateTime(DateTime.Today);

    // The Repeat field: the current rule's summary in the outlined frame. In add mode it is tappable (with a dropdown
    // affordance) to open the preset dialog; in edit mode it is a read-only summary — a one-off carries no rule, and
    // changing an existing transaction's repeat rule is deferred to §8, so there is nothing to open.
    private VisualNode RepeatField(MaterialScheme scheme)
    {
        var summary = Grid(
            Label(RepeatSummary())
                .FontSize(16)
                .TextColor(IsEdit ? scheme.OnSurfaceVariant : scheme.OnSurface)
                .VCenter());

        if (IsEdit)
        {
            return new TextField().Label("Repeat").Content(summary);
        }

        return new TextField()
            .Label("Repeat")
            .Content(summary.OnTapped(OpenRepeatDialog))
            .Trailing(
                Grid(
                    Label(MaterialSymbols.ArrowDropDown)
                        .FontFamily(MaterialSymbols.FontFamily).FontSize(22)
                        .TextColor(scheme.OnSurfaceVariant).VCenter()
                ).OnTapped(OpenRepeatDialog)
            );
    }

    // The committed rule as human text (origin-relative — see RepeatDescription), with a leading capital.
    private string RepeatSummary() =>
        Capitalize(RepeatDescription.Describe(State.Strategy, State.Date)
            + RepeatDescription.DescribeEndCondition(State.EndCondition, null));

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // The preset choices, custom slot first when one exists, then the five defaults and "Custom…".
    private sealed record RepeatChoice(string Label, RepeatStrategy? Strategy, RepeatEndCondition? End, bool OpensEditor);

    private List<RepeatChoice> RepeatChoices()
    {
        var forever = new RepeatEndCondition.Forever();
        var choices = new List<RepeatChoice>();
        if (State.CustomStrategy is not null && State.CustomEnd is not null)
        {
            var label = Capitalize(RepeatDescription.Describe(State.CustomStrategy, State.Date)
                + RepeatDescription.DescribeEndCondition(State.CustomEnd, null));
            choices.Add(new RepeatChoice(label, State.CustomStrategy, State.CustomEnd, false));
        }

        choices.Add(new RepeatChoice("Does not repeat", new RepeatStrategy.Never(), forever, false));
        choices.Add(new RepeatChoice("Every day", RepeatEditing.EveryDay(), forever, false));
        choices.Add(new RepeatChoice("Every week", RepeatEditing.EveryWeek(State.Date), forever, false));
        choices.Add(new RepeatChoice("Every month", RepeatEditing.EveryMonth(), forever, false));
        choices.Add(new RepeatChoice("Every year", RepeatEditing.EveryYear(), forever, false));
        choices.Add(new RepeatChoice("Custom…", null, null, true));
        return choices;
    }

    // Which radio is pre-selected: the choice whose rule equals the committed one; else "Does not repeat".
    private int SelectedRepeatIndex(List<RepeatChoice> choices)
    {
        var index = choices.FindIndex(c => !c.OpensEditor && c.Strategy == State.Strategy && c.End == State.EndCondition);
        return index >= 0 ? index : choices.FindIndex(c => c.Strategy is RepeatStrategy.Never);
    }

    private VisualNode RepeatDialog()
    {
        var choices = RepeatChoices();
        return new ChoiceDialog()
            .Title("Repeat")
            .Options([.. choices.Select(c => c.Label)])
            .SelectedIndex(SelectedRepeatIndex(choices))
            .OnConfirm(ApplyRepeatChoice)
            .OnCancel(CloseRepeatDialog);
    }

    private void OpenRepeatDialog()
    {
        HideKeyboard();
        SetState(s => s.RepeatDialogOpen = true);
    }

    private void CloseRepeatDialog() => SetState(s => s.RepeatDialogOpen = false);

    // OK on a preset/custom applies it; OK on "Custom…" pushes the editor.
    private void ApplyRepeatChoice(int index)
    {
        var choice = RepeatChoices()[index];
        if (choice.OpensEditor)
        {
            OpenRepeatEditor();
            return;
        }

        SetState(s =>
        {
            s.Strategy = choice.Strategy!;
            s.EndCondition = choice.End!;
            s.RepeatDialogOpen = false;
        });
    }

    // Push the §8 editor, seeded from the committed rule (or Monthly when currently non-repeating).
    private void OpenRepeatEditor()
    {
        SetState(s => s.RepeatDialogOpen = false);

        var seedStrategy = State.Strategy is RepeatStrategy.Never ? RepeatEditing.EveryMonth() : State.Strategy;
        var seedEnd = State.Strategy is RepeatStrategy.Never ? new RepeatEndCondition.Forever() : State.EndCondition;

        _ = Navigation?.PushAsync<RepeatStrategyPage, RepeatStrategyProps>(p =>
        {
            p.Origin = State.Date;
            p.MinDate = MinDate();
            p.Today = Today();
            p.Strategy = seedStrategy;
            p.EndCondition = seedEnd;
            p.OnClosed = OnRepeatEditorClosed;
        });
    }

    // Done on the editor: apply the custom rule and pin it as the session's custom slot. Cancel returns null.
    private void OnRepeatEditorClosed((RepeatStrategy Strategy, RepeatEndCondition End)? result)
    {
        if (result is not { } rule)
        {
            return;
        }

        SetState(s =>
        {
            s.Strategy = rule.Strategy;
            s.EndCondition = rule.End;
            s.CustomStrategy = rule.Strategy;
            s.CustomEnd = rule.End;
        });
    }

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

    // Open the calendar dialog; dismiss the soft keyboard first so it doesn't fight the modal (ModalHost owns back).
    private void OpenCalendar()
    {
        HideKeyboard();
        SetState(s =>
        {
            s.CalendarOpen = true;
            s.DateActive = true; // the calendar belongs to the date field — light it and keep it lit
        });
    }

    private void CancelCalendar() => SetState(s => s.CalendarOpen = false);

    // OK: the picked date overwrites both the committed value and the text buffer, and closes the dialog.
    private void ConfirmCalendar(DateOnly date) =>
        SetState(s =>
        {
            s.Date = date;
            s.DateText = DateEntry.Format(date);
            s.DateError = null;
            s.CalendarOpen = false;
        });

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
        if (State.Edit is { } original)
        {
            ApplyEdit(account, original, date, signed, State.Description.Trim());
        }
        else
        {
            // Never → a plain one-off (null strategy); any real rule → the sequence.
            var strategy = State.Strategy is RepeatStrategy.Never ? null : State.Strategy;
            var endCondition = strategy is null ? null : State.EndCondition;
            account.AddTransaction(date, signed, State.Description.Trim(), strategy, endCondition);
        }

        await DismissKeyboardThenPop();
        Props.OnClosed?.Invoke(new TransactionOutcome(TransactionResult.Saved, date));
    }

    // Applies an edit to an existing one-off by diffing against the original. Each Core method no-ops when its value
    // is unchanged, so the calls are unconditional. Order matters: the id-preserving changes (description, amount)
    // go first, then the date last — a date change re-keys the transaction, invalidating the original's id.
    private static void ApplyEdit(Account account, Transaction original, DateOnly newDate, decimal newAmount, string newDescription)
    {
        account.ChangeTransactionDescription(original, newDescription);
        account.ChangeTransactionAmount(original, newAmount);
        account.ChangeTransactionDate(original, newDate);
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
    private static bool HideKeyboard() => Platform.SoftKeyboard.Hide();
}
