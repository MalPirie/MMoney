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

/// <summary>
/// Everything needed to re-create a deleted transaction as a Snackbar "Undo" (§7). Undo always <em>creates new</em>
/// (a fresh sequence number, a detached one-off for a skipped occurrence) — never a true reversal. A null
/// <see cref="Strategy"/> re-creates a one-off; a non-null one re-creates a sequence from <see cref="Date"/>.
/// </summary>
internal readonly record struct DeletedUndo(
    DateOnly Date, decimal Amount, string Description, RepeatStrategy? Strategy, RepeatEndCondition? EndCondition);

/// <summary>The outcome a transaction editor hands back through its <c>OnClosed</c> callback.</summary>
/// <param name="Undo">Set when <see cref="TransactionResult.Deleted"/>, carrying the re-create payload for the Snackbar.</param>
internal readonly record struct TransactionOutcome(TransactionResult Result, DateOnly Date, DeletedUndo? Undo = null);

/// <summary>Props passed to a pushed <see cref="AddTransactionPage"/> (MauiReactor's props-initializer navigation).</summary>
internal sealed class AddTransactionProps
{
    /// <summary>Invoked as the page closes, reporting what it did so the shell can react.</summary>
    public Action<TransactionOutcome>? OnClosed { get; set; }

    /// <summary>
    /// The transaction to edit, or <see langword="null"/> to add a new one. For a one-off (§7) it is the transaction
    /// itself; for an occurrence (§8) it is the tapped occurrence; for a whole-series edit it is the sequence's
    /// origin transaction (paired with <see cref="WholeSeries"/>).
    /// </summary>
    public Transaction? Edit { get; set; }

    /// <summary>
    /// When <see langword="true"/> (and <see cref="Edit"/> is a sequence's origin transaction), edit the whole
    /// repeating series (§8): amount, description, repeat rule, and origin date are all editable and applied
    /// whole-series — no scope dialog. When <see langword="false"/> a sequence occurrence edits via the scope dialog.
    /// </summary>
    public bool WholeSeries { get; set; }
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

    // Sequence editing (§8): the owning sequence when the edit target belongs to one (null for a one-off), whether
    // this is a whole-series edit (vs a single occurrence), and whether the scope dialog is showing.
    public Sequence? EditSequence { get; set; }
    public bool WholeSeries { get; set; }
    public bool ScopeDialogOpen { get; set; }

    // Edit overflow (§7): the ⋮ menu, the Copy date-picker, and Delete's confirm/scope dialogs.
    public bool OverflowOpen { get; set; }
    public bool CopyCalendarOpen { get; set; }
    public bool DeleteConfirmOpen { get; set; }
    public bool DeleteScopeOpen { get; set; }

    // Discard-changes guard (§7): the initial field snapshot (captured after seeding) for the dirty check and the
    // dialog flag.
    public bool DiscardOpen { get; set; }
    public DateOnly InitialDate { get; set; }
    public string InitialAmountText { get; set; } = string.Empty;
    public string InitialDescription { get; set; } = string.Empty;
    public bool InitialIsIncome { get; set; }
    public RepeatStrategy InitialStrategy { get; set; } = new RepeatStrategy.Never();
    public RepeatEndCondition InitialEndCondition { get; set; } = new RepeatEndCondition.Forever();

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
/// it. Editing an occurrence (§8) routes Save through the This/This-and-following/All scope dialog for amount and
/// description; its date and repeat rule are read-only (occurrence date-move and rule changes deferred). A
/// whole-series edit (§8, from the Repeating tab — <see cref="AddTransactionProps.WholeSeries"/>) keeps every field
/// editable and applies to the series at once with no dialog. A one-off's Repeat field is a read-only "Does not
/// repeat". Delete / Copy are later slices.
/// </summary>
partial class AddTransactionPage : Component<AddTransactionState, AddTransactionProps>
{
    private static readonly CultureInfo Gb = CultureInfo.GetCultureInfo("en-GB");

    protected override void OnMounted()
    {
        State.Manager = Services.GetRequiredService<AccountManager>();

        if (Props.Edit is { } edit)
        {
            // Edit mode (§7/§8): seed the fields from the stored transaction. If it is a projected occurrence, resolve
            // its owning sequence so the Repeat field shows the real rule and Save can offer the scope dialog. A
            // one-off has no sequence, so its Repeat field stays a read-only "Does not repeat".
            State.Edit = edit;
            State.Date = edit.Date;
            State.AmountText = Math.Abs(edit.Amount).ToString("0.##", Gb);
            State.IsIncome = edit.Amount > 0;
            State.Description = edit.Description;

            var account = State.Manager.GetAccounts().FirstOrDefault();
            State.EditSequence = account?.GetSequences().FirstOrDefault(s => s.Number == edit.Sequence);
            State.WholeSeries = Props.WholeSeries;
            if (State.EditSequence is { } seq)
            {
                State.Strategy = seq.Strategy;
                State.EndCondition = seq.EndCondition;

                // Both occurrence and whole-series editing expose the Repeat field; if the current rule isn't one of
                // the built-in presets (e.g. specific weekdays or a bounded end), pin it as the custom slot so it
                // shows and stays selected.
                if (!MatchesDefaultPreset(seq.Strategy, seq.EndCondition))
                {
                    State.CustomStrategy = seq.Strategy;
                    State.CustomEnd = seq.EndCondition;
                }
            }
        }
        else
        {
            State.Date = State.Manager.Today;
        }

        State.DateText = DateEntry.Format(State.Date);

        // Snapshot the seeded fields so back-when-dirty can tell whether anything changed (§7), and claim the
        // hardware back button so a dirty exit shows the discard dialog instead of popping.
        State.InitialDate = State.Date;
        State.InitialAmountText = State.AmountText;
        State.InitialDescription = State.Description;
        State.InitialIsIncome = State.IsIncome;
        State.InitialStrategy = State.Strategy;
        State.InitialEndCondition = State.EndCondition;

        base.OnMounted();
    }

    // Whether any field differs from its seeded value (drives the discard-changes guard).
    private bool IsDirty =>
        State.Date != State.InitialDate
        || State.AmountText != State.InitialAmountText
        || State.Description != State.InitialDescription
        || State.IsIncome != State.InitialIsIncome
        || State.Strategy != State.InitialStrategy
        || State.EndCondition != State.InitialEndCondition;

    private bool IsEdit => State.Edit is not null;

    // Editing a projected occurrence (§8): every field is editable (date-move and rule change included), and Save
    // routes through the scope dialog. A whole-series edit also has a sequence but applies without a dialog.
    private bool IsOccurrence => State.EditSequence is not null && !State.WholeSeries;

    private bool IsWholeSeries => State.EditSequence is not null && State.WholeSeries;

    // The Repeat field is read-only only for a one-off edit (a one-off has no sequence, so it stays "Does not
    // repeat"). Add mode and any sequence edit (occurrence or whole-series) keep it interactive.
    private bool RepeatReadOnly => IsEdit && State.EditSequence is null;

    // Whether a rule equals one of the Repeat preset defaults (all Forever-ended). Used to decide whether an existing
    // whole-series rule needs pinning as the custom slot.
    private bool MatchesDefaultPreset(RepeatStrategy strategy, RepeatEndCondition end) =>
        end is RepeatEndCondition.Forever
        && (strategy is RepeatStrategy.Never
            || strategy == RepeatEditing.EveryDay()
            || strategy == RepeatEditing.EveryWeek(State.Date)
            || strategy == RepeatEditing.EveryMonth()
            || strategy == RepeatEditing.EveryYear());

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;

        var content = new List<VisualNode>
        {
            new TopAppBar()
                .Title(IsWholeSeries ? "Edit repeating transaction" : IsEdit ? "Edit transaction" : "Add transaction")
                .Container(scheme.Primary)
                .OnContainer(scheme.OnPrimary)
                .OnBack(Cancel)
                .ActionText("Save")
                .OnAction(Save)
                .OnOverflow(IsEdit ? OpenOverflow : (Action?)null) // Copy / Delete live in an overflow, edit mode only
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
            new ModalHost()
                .IsOpen(State.ScopeDialogOpen)
                .OnDismiss(CloseScopeDialog)
                .Content(ScopeDialog())
                .GridRowSpan(2),
            new ModalHost()
                .IsOpen(State.DiscardOpen)
                .OnDismiss(CloseDiscard)
                .Content(DiscardDialog())
                .GridRowSpan(2),
            new ModalHost()
                .IsOpen(State.CopyCalendarOpen)
                .OnDismiss(CloseCopy)
                .Content(CopyCalendarSurface())
                .GridRowSpan(2),
            new ModalHost()
                .IsOpen(State.DeleteConfirmOpen)
                .OnDismiss(CloseDelete)
                .Content(DeleteDialog())
                .GridRowSpan(2),
            new ModalHost()
                .IsOpen(State.DeleteScopeOpen)
                .OnDismiss(CloseDeleteScope)
                .Content(DeleteScopeDialog())
                .GridRowSpan(2),
        };

        // The edit overflow (⋮) menu overlay sits on top; input-transparent while closed (ADR-0004).
        if (IsEdit)
        {
            content.Add(OverflowMenu(scheme).GridRowSpan(2));
        }

        // ModalAwareContentPage, not ContentPage: it routes hardware back to the open ModalHost (ADR-0007), then to
        // BackGuard (set fresh each render) so a dirty exit shows the discard dialog instead of popping (§7).
        return new ModalAwareContentPage(
            Grid("Auto,*", "*", [.. content])
        ).BackGuard(HandleHardwareBack).HasNavigationBar(false);
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
                .TextColor(RepeatReadOnly ? scheme.OnSurfaceVariant : scheme.OnSurface)
                .VCenter());

        // Read-only only for a one-off ("Does not repeat"). Any sequence edit (occurrence or whole-series) keeps it
        // interactive so the schedule can be changed.
        if (RepeatReadOnly)
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

    // The committed rule as human text (origin-relative — see RepeatDescription), with a leading capital. When
    // editing an occurrence the rule belongs to the sequence, so it reads from the sequence's origin, not the
    // occurrence's date.
    private string RepeatSummary() =>
        Capitalize(RepeatDescription.Describe(State.Strategy, State.EditSequence?.Origin ?? State.Date)
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
    private VisualNode DateField(MaterialScheme scheme)
    {
        return new TextField()
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
    }

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

    // The app-bar back arrow: guard unsaved changes with the discard dialog, otherwise just leave (§7).
    private void Cancel()
    {
        if (IsDirty)
        {
            HideKeyboard();
            SetState(s => s.DiscardOpen = true);
            return;
        }

        _ = LeaveWithoutSaving();
    }

    // The hardware back button (routed here by ModalAwareContentPage once no dialog is open): consume the press to
    // show the discard dialog when dirty; otherwise let the page pop.
    private bool HandleHardwareBack()
    {
        if (!IsDirty)
        {
            return false;
        }

        HideKeyboard();
        SetState(s => s.DiscardOpen = true);
        return true;
    }

    private void CloseDiscard() => SetState(s => s.DiscardOpen = false);

    // "Discard" confirms the exit; "Keep editing" is the dismiss (handled by CloseDiscard via the ModalHost).
    private async void ConfirmDiscard()
    {
        SetState(s => s.DiscardOpen = false);
        await LeaveWithoutSaving();
    }

    private VisualNode DiscardDialog() =>
        new AlertDialog()
            .Title("Discard changes?")
            .Message("Your changes will be lost.")
            .ConfirmText("Discard")
            .DismissText("Keep editing")
            .Destructive(true)
            .OnConfirm(ConfirmDiscard)
            .OnDismiss(CloseDiscard);

    private async Task LeaveWithoutSaving()
    {
        await DismissKeyboardThenPop();
        Props.OnClosed?.Invoke(new TransactionOutcome(TransactionResult.None, State.Date));
    }

    // ---- edit overflow: Copy and Delete (§7) -------------------------------------------------------------

    private void OpenOverflow()
    {
        HideKeyboard();
        SetState(s => s.OverflowOpen = true);
    }

    private void CloseOverflow() => SetState(s => s.OverflowOpen = false);

    private Mobiorum.Material3.MenuItem[] OverflowItems() =>
    [
        new Mobiorum.Material3.MenuItem(MaterialSymbols.ContentCopy, "Copy").OnSelected(OpenCopy),
        new Mobiorum.Material3.MenuItem(MaterialSymbols.Delete, "Delete").OnSelected(OpenDelete),
    ];

    // The overflow overlay: a transparent tap-catcher that dismisses on an outside tap, with the Menu anchored to the
    // overflow (⋮) button's top-right corner so it grows out of the button — matching the main page. The button is
    // VCenter in the 64dp TopAppBar (top edge 8dp) and its trailing HStack insets it 4dp from the right, so 8dp/4dp
    // inset tracks put the menu's top-right on it. Input-transparent while closed so touches pass through (ADR-0004).
    private VisualNode OverflowMenu(MaterialScheme scheme) =>
        Grid("8,Auto,*", "*,Auto,4",
            Border()
                .BackgroundColor(Colors.Transparent)
                .StrokeThickness(0)
                .OnTapped(CloseOverflow)
                .GridRowSpan(3).GridColumnSpan(3),
            new Menu()
                .IsOpen(State.OverflowOpen)
                .Items(OverflowItems())
                .GridRow(1).GridColumn(1)
        ).InputTransparent(!State.OverflowOpen);

    // Copy: a date-picker whose OK reads "Copy"; it always adds a one-off on the chosen date using the form's
    // current amount/description/sign, then pops (the original is left untouched).
    private void OpenCopy() => SetState(s => { s.OverflowOpen = false; s.CopyCalendarOpen = true; });

    private void CloseCopy() => SetState(s => s.CopyCalendarOpen = false);

    private VisualNode CopyCalendarSurface() =>
        new Mobiorum.Material3.Calendar()
            .SelectedDate(State.Date)
            .MinDate(MinDate())
            .Today(Today())
            .ConfirmText("Copy")
            .OnConfirm(ConfirmCopy)
            .OnCancel(CloseCopy);

    private async void ConfirmCopy(DateOnly date)
    {
        var account = State.Manager?.GetAccounts().FirstOrDefault();
        if (account is null || !TryAmount(State.AmountText, out var magnitude) || string.IsNullOrWhiteSpace(State.Description))
        {
            SetState(s => s.CopyCalendarOpen = false);
            return;
        }

        var signed = State.IsIncome ? magnitude : -magnitude;
        account.AddTransaction(date, signed, State.Description.Trim()); // always a one-off (§7)
        SetState(s => s.CopyCalendarOpen = false);
        await DismissKeyboardThenPop();
        Props.OnClosed?.Invoke(new TransactionOutcome(TransactionResult.Saved, date));
    }

    // Delete: a one-off / whole-series goes straight to a confirm AlertDialog; an occurrence goes to the scope
    // dialog (This / This-and-following / All), whose "Delete" button is the confirmation.
    private void OpenDelete() =>
        SetState(s =>
        {
            s.OverflowOpen = false;
            if (IsOccurrence)
            {
                s.DeleteScopeOpen = true;
            }
            else
            {
                s.DeleteConfirmOpen = true;
            }
        });

    private void CloseDelete() => SetState(s => s.DeleteConfirmOpen = false);

    private VisualNode DeleteDialog() =>
        new AlertDialog()
            .Title(IsWholeSeries ? "Delete repeating transaction?" : "Delete transaction?")
            .Message(DeleteSummary())
            .ConfirmText("Delete")
            .DismissText("Cancel")
            .Destructive(true)
            .OnConfirm(ConfirmDelete)
            .OnDismiss(CloseDelete);

    private string DeleteSummary()
    {
        if (State.Edit is not { } target)
        {
            return string.Empty;
        }

        var amount = target.Amount.ToString("C", Gb);
        return IsWholeSeries
            ? $"The whole “{target.Description}” series ({amount}) will be removed."
            : $"“{target.Description}” ({amount}) will be deleted.";
    }

    // Confirm on the AlertDialog (one-off or whole-series only — occurrences confirm via the scope dialog).
    private void ConfirmDelete()
    {
        var account = State.Manager?.GetAccounts().FirstOrDefault();
        if (account is null || State.Edit is not { } target)
        {
            return;
        }

        if (IsWholeSeries && State.EditSequence is { } seq)
        {
            var originTx = new Transaction(seq.Id, seq.Amount, seq.Description);
            var from = seq.Origin > account.EarliestAllowedDate ? seq.Origin : account.EarliestAllowedDate;
            DeleteWith(() => account.RemoveSequence(originTx, from),
                new DeletedUndo(from, seq.Amount, seq.Description, seq.Strategy, seq.EndCondition));
        }
        else
        {
            DeleteWith(() => account.RemoveTransaction(target),
                new DeletedUndo(target.Date, target.Amount, target.Description, null, null));
        }
    }

    private void CloseDeleteScope() => SetState(s => s.DeleteScopeOpen = false);

    // The occurrence delete-scope dialog. Unlike the edit scope dialog, "This" is always offered (deleting a single
    // occurrence never depends on a pending rule edit); This-and-following still collapses to All at the origin.
    private VisualNode DeleteScopeDialog()
    {
        var choices = DeleteScopes();
        return new ChoiceDialog()
            .Title("Delete repeating transaction")
            .Options([.. choices.Select(c => c.Label)])
            .SelectedIndex(0)
            .ConfirmText("Delete")
            .OnConfirm(i => ApplyDeleteScope(choices[i].Scope))
            .OnCancel(CloseDeleteScope);
    }

    private List<(string Label, EditScope Scope)> DeleteScopes()
    {
        var choices = new List<(string, EditScope)> { ("This transaction", EditScope.This) };
        if (State.Edit is { } occ && State.EditSequence is { } seq && occ.Date != seq.Origin)
        {
            choices.Add(("This and following", EditScope.ThisAndFollowing));
        }

        choices.Add(("All in the series", EditScope.All));
        return choices;
    }

    // Scope → Core for delete (app-design §8): This skips the occurrence (undo becomes a detached one-off); the
    // sequence scopes remove from the occurrence or the origin (undo re-creates a fresh sequence).
    private void ApplyDeleteScope(EditScope scope)
    {
        var account = State.Manager?.GetAccounts().FirstOrDefault();
        if (account is null || State.Edit is not { } occ || State.EditSequence is not { } seq)
        {
            return;
        }

        switch (scope)
        {
            case EditScope.This:
                DeleteWith(() => account.RemoveTransaction(occ),
                    new DeletedUndo(occ.Date, occ.Amount, occ.Description, null, null));
                break;

            case EditScope.ThisAndFollowing:
                DeleteWith(() => account.RemoveSequence(occ, occ.Date),
                    new DeletedUndo(occ.Date, seq.Amount, seq.Description, seq.Strategy, seq.EndCondition));
                break;

            case EditScope.All:
                var from = seq.Origin > account.EarliestAllowedDate ? seq.Origin : account.EarliestAllowedDate;
                DeleteWith(() => account.RemoveSequence(occ, from),
                    new DeletedUndo(from, seq.Amount, seq.Description, seq.Strategy, seq.EndCondition));
                break;
        }
    }

    // Run the removal, close the delete dialogs, pop, and report Deleted with the undo payload so the shell can raise
    // the Snackbar. A refused removal (e.g. below the edit lock) is swallowed after closing the dialogs.
    private async void DeleteWith(Action remove, DeletedUndo undo)
    {
        try
        {
            remove();
        }
        catch (Exception)
        {
            SetState(s => { s.DeleteConfirmOpen = false; s.DeleteScopeOpen = false; });
            return;
        }

        SetState(s => { s.DeleteConfirmOpen = false; s.DeleteScopeOpen = false; });
        await DismissKeyboardThenPop();
        Props.OnClosed?.Invoke(new TransactionOutcome(TransactionResult.Deleted, undo.Date, undo));
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
        var description = State.Description.Trim();

        // Whole-series edit (§8): amount, description, rule, and origin date all apply to the series at once — no
        // scope dialog. State.Strategy/EndCondition carry any rule change made through the Repeat field.
        if (IsWholeSeries && State.EditSequence is { } series)
        {
            ApplyWholeSeriesEdit(account, series, date, signed, description, State.Strategy, State.EndCondition);
            await ClosePage(date);
            return;
        }

        // Editing an occurrence (§8): the change needs a scope — This / This-and-following / All. If nothing changed,
        // just leave; if only one scope applies (e.g. a rule change at the origin), apply it directly; otherwise hand
        // off to the scope dialog.
        if (State.Edit is { } occ && IsOccurrence && State.EditSequence is { } seq)
        {
            var ruleChanged = State.Strategy != seq.Strategy || State.EndCondition != seq.EndCondition;
            if (signed == occ.Amount && description == occ.Description && date == occ.Date && !ruleChanged)
            {
                await ClosePage(occ.Date);
                return;
            }

            var scopes = AvailableScopes();
            if (scopes.Count == 1)
            {
                await CommitOccurrence(scopes[0].Scope);
                return;
            }

            HideKeyboard();
            SetState(s => s.ScopeDialogOpen = true);
            return;
        }

        if (State.Edit is { } original)
        {
            ApplyEdit(account, original, date, signed, description);
        }
        else
        {
            // Never → a plain one-off (null strategy); any real rule → the sequence.
            var strategy = State.Strategy is RepeatStrategy.Never ? null : State.Strategy;
            var endCondition = strategy is null ? null : State.EndCondition;
            account.AddTransaction(date, signed, description, strategy, endCondition);
        }

        await ClosePage(date);
    }

    // Dismiss the keyboard, pop, and report the saved date so the shell jumps the ledger to it.
    private async Task ClosePage(DateOnly date)
    {
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

    // ---- occurrence scope dialog (§8) --------------------------------------------------------------------

    /// <summary>Which occurrences a change applies to (app-design §8's scope → Core mapping).</summary>
    private enum EditScope { This, ThisAndFollowing, All }

    // The scopes offered, top to bottom, for the current edit (app-design §8). "This" is dropped when the rule
    // changed (an occurrence has no rule of its own); "This and following" collapses into All at the origin.
    private List<(string Label, EditScope Scope)> AvailableScopes()
    {
        // Built every render (the scope ModalHost is always in the tree), so stay null-safe when this isn't an
        // occurrence edit — the dialog is only ever opened for one.
        if (State.Edit is not { } occ || State.EditSequence is not { } seq)
        {
            return [];
        }

        var atOrigin = occ.Date == seq.Origin;
        var ruleChanged = State.Strategy != seq.Strategy || State.EndCondition != seq.EndCondition;

        var choices = new List<(string, EditScope)>();
        if (!ruleChanged)
        {
            choices.Add(("This transaction", EditScope.This));
        }

        if (!atOrigin)
        {
            choices.Add(("This and following", EditScope.ThisAndFollowing));
        }

        choices.Add(("All in the series", EditScope.All));
        return choices;
    }

    private VisualNode ScopeDialog()
    {
        var choices = AvailableScopes();
        return new ChoiceDialog()
            .Title("Change repeating transaction")
            .Options([.. choices.Select(c => c.Label)])
            .SelectedIndex(0) // default to the first (least broad) available scope
            .ConfirmText("Change")
            .OnConfirm(i => { SetState(s => s.ScopeDialogOpen = false); _ = CommitOccurrence(choices[i].Scope); })
            .OnCancel(CloseScopeDialog);
    }

    private void CloseScopeDialog() => SetState(s => s.ScopeDialogOpen = false);

    // Apply the occurrence edit at the chosen scope, then pop. A refused date-move (moving onto a date the sequence
    // already holds) is the one expected failure — surface it as a date error and keep the page open.
    private async Task CommitOccurrence(EditScope scope)
    {
        var account = State.Manager?.GetAccounts().FirstOrDefault();
        if (account is null || State.Edit is not { } occ || State.EditSequence is not { } seq
            || !DateEntry.TryParse(State.DateText, out var date) || !TryAmount(State.AmountText, out var magnitude))
        {
            return;
        }

        var signed = State.IsIncome ? magnitude : -magnitude;
        try
        {
            ApplyOccurrenceScope(account, occ, seq, scope, date, signed, State.Description.Trim(),
                State.Strategy, State.EndCondition);
        }
        catch (InvalidOperationException)
        {
            SetState(s => s.DateError = "That date already has an occurrence in this series");
            return;
        }

        await ClosePage(date);
    }

    // The §8 scope → Core mapping, carrying every edited field (amount, description, date, rule). "This" writes
    // per-occurrence overrides and stays linked to the sequence; the sequence scopes change the rule in place (All,
    // amount/desc only with an editable origin) or re-issue the sequence once from a cut point.
    private static void ApplyOccurrenceScope(Account account, Transaction occ, Sequence seq, EditScope scope,
        DateOnly newDate, decimal newAmount, string newDescription, RepeatStrategy newStrategy, RepeatEndCondition newEnd)
    {
        var dateChanged = newDate != occ.Date;
        var ruleChanged = newStrategy != seq.Strategy || newEnd != seq.EndCondition;
        var dateMovedOnly = dateChanged && !ruleChanged;

        switch (scope)
        {
            case EditScope.This:
                // Per-occurrence overrides. Move first (it re-keys the occurrence but keeps its sequence number, so it
                // stays linked) so a collision with a sibling aborts before any override is written.
                var handle = occ;
                if (dateChanged)
                {
                    account.ChangeTransactionDate(occ, newDate);
                    handle = occ with { Id = occ.Id with { Date = newDate } };
                }

                account.ChangeTransactionDescription(handle, newDescription);
                account.ChangeTransactionAmount(handle, newAmount);
                break;

            case EditScope.ThisAndFollowing:
            {
                var anchor = dateChanged ? newDate : occ.Date;
                Reissue(account, occ, occ.Date, anchor, newAmount, newDescription,
                    AdjustForDateMove(newStrategy, anchor, dateMovedOnly), newEnd);
                break;
            }

            case EditScope.All:
            {
                var editLock = account.EarliestAllowedDate;
                var fromDate = seq.Origin > editLock ? seq.Origin : editLock;
                if (!dateChanged && !ruleChanged && fromDate == seq.Origin)
                {
                    // Editable origin, amount/description only: change in place, keeping the number and overrides.
                    if (newAmount != seq.Amount)
                    {
                        account.ChangeSequenceAmount(occ, seq.Origin, newAmount);
                    }

                    if (newDescription != seq.Description)
                    {
                        account.ChangeSequenceDescription(occ, seq.Origin, newDescription);
                    }
                }
                else
                {
                    var anchor = dateChanged ? (newDate > editLock ? newDate : editLock) : fromDate;
                    Reissue(account, occ, fromDate, anchor, newAmount, newDescription,
                        AdjustForDateMove(newStrategy, anchor, dateMovedOnly), newEnd);
                }

                break;
            }
        }
    }

    // Truncate the sequence at <paramref name="cut"/> and restart it at <paramref name="anchor"/> with the new fields.
    // A single re-issue (not chained ChangeSequence* calls) so a combined amount/description/rule/date change can't
    // split the sequence more than once. A Never strategy restarts it as a plain one-off.
    private static void Reissue(Account account, Transaction handle, DateOnly cut, DateOnly anchor,
        decimal amount, string description, RepeatStrategy strategy, RepeatEndCondition end)
    {
        account.RemoveSequence(handle, cut);
        if (strategy is RepeatStrategy.Never)
        {
            account.AddTransaction(anchor, amount, description);
        }
        else
        {
            account.AddTransaction(anchor, amount, description, strategy, end);
        }
    }

    // "New day wins" for a pure date-move: a Weekly rule re-anchored onto a weekday it doesn't already cover adopts
    // that weekday (collapsing a multi-day rule to the moved-to day). The other strategies are origin-relative and
    // re-derive on their own; an explicit rule edit (dateMovedOnly false) is left exactly as the user set it.
    private static RepeatStrategy AdjustForDateMove(RepeatStrategy strategy, DateOnly anchor, bool dateMovedOnly)
    {
        if (dateMovedOnly && strategy is RepeatStrategy.Weekly weekly)
        {
            var day = RepeatEditing.ToDaysOfWeek(anchor.DayOfWeek);
            if (!weekly.Days.HasFlag(day))
            {
                return weekly with { Days = day };
            }
        }

        return strategy;
    }

    // ---- whole-series edit (§8, from the Repeating tab) --------------------------------------------------

    // Apply an edit to the whole series (app-design §8's first edit path). Everything applies from max(origin, lock):
    // a true in-place change when the origin is editable and only the amount/description changed; otherwise a single
    // re-issue that truncates at the cut and restarts the series with every new field (a rule change, an origin
    // re-anchor, or a locked origin all take this path). "Does not repeat" collapses the series into a one-off.
    private static void ApplyWholeSeriesEdit(Account account, Sequence seq, DateOnly newDate, decimal newAmount,
        string newDescription, RepeatStrategy newStrategy, RepeatEndCondition newEnd)
    {
        // Any occurrence of the sequence serves as the Core handle; the origin transaction is the natural one.
        var originTx = new Transaction(seq.Id, seq.Amount, seq.Description);
        var editLock = account.EarliestAllowedDate;
        var fromDate = seq.Origin > editLock ? seq.Origin : editLock;

        var dateChanged = newDate != seq.Origin;
        var ruleChanged = newStrategy != seq.Strategy || newEnd != seq.EndCondition;

        if (!dateChanged && !ruleChanged && fromDate == seq.Origin)
        {
            // Editable origin, amount/description only: change in place, keeping the sequence number and overrides.
            if (newAmount != seq.Amount)
            {
                account.ChangeSequenceAmount(originTx, seq.Origin, newAmount);
            }

            if (newDescription != seq.Description)
            {
                account.ChangeSequenceDescription(originTx, seq.Origin, newDescription);
            }

            return;
        }

        // Re-issue once from the (clamped) new origin, carrying every field (a pure date-move on a Weekly rule adopts
        // the new weekday). One truncate-and-re-add, so a combined rule/date/amount change can't split more than once.
        var anchor = dateChanged ? (newDate > editLock ? newDate : editLock) : fromDate;
        Reissue(account, originTx, fromDate, anchor, newAmount, newDescription,
            AdjustForDateMove(newStrategy, anchor, dateChanged && !ruleChanged), newEnd);
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
