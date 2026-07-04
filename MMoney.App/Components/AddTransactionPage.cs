using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using MauiReactor;
using MauiReactor.Shapes;
using Mobiorum.Material3;
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
    public DateOnly Date { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsIncome { get; set; } // default false = Expense (app-design §7)
    public string? AmountError { get; set; }
    public string? DescriptionError { get; set; }
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

    protected override void OnMounted()
    {
        State.Manager = Services.GetRequiredService<AccountManager>();
        State.Date = State.Manager.Today;
        base.OnMounted();
    }

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var account = State.Manager?.GetAccounts().FirstOrDefault();
        var editLock = account?.EarliestAllowedDate ?? DateOnly.MinValue;
        // MAUI's DatePicker floors at 1900; only push the minimum up to a real edit lock.
        var minDate = editLock.Year < 1900 ? new DateTime(1900, 1, 1) : editLock.ToDateTime(TimeOnly.MinValue);

        return ContentPage(
            Grid("Auto,*", "*",
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
                        DateField(scheme, minDate),
                        // Amount and its income/expense type share one full-width bordered field — the type drives
                        // the amount's sign, so the In/Out toggle lives inside the field's bounds, trailing the entry.
                        new TextField()
                            .Label("Amount")
                            .Text(State.AmountText)
                            .Placeholder("0.00")
                            .Keyboard(Keyboard.Numeric)
                            .Error(State.AmountError)
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
                            .OnTextChanged(OnDescriptionChanged),
                        RepeatField(scheme)
                    ).Spacing(20).Padding(16)
                ).GridRow(1)
            )
        ).HasNavigationBar(false);
    }

    // The date field, styled to match the filled TextField (label + surfaceContainer + bottom indicator). The picker
    // is MAUI's native DatePicker, which opens a modal dialog over the field — so it carries no focus highlight (you
    // wouldn't see it behind the dialog). A focus state arrives when we build the inline M3 date field.
    private VisualNode DateField(MaterialScheme scheme, DateTime minDate) =>
        Border(
            VStack(
                Label("Date").FontSize(12).TextColor(scheme.OnSurfaceVariant),
                DatePicker()
                    .Date(State.Date.ToDateTime(TimeOnly.MinValue))
                    .MinimumDate(minDate)
                    .Format("ddd d MMM yyyy")
                    .TextColor(scheme.OnSurface)
                    .OnDateSelected((MauiControls.DateChangedEventArgs e) => SetState(s => s.Date = DateOnly.FromDateTime(e.NewDate ?? DateTime.Today)))
            ).Spacing(2)
        )
        .BackgroundColor(scheme.SurfaceContainer)
        .Stroke(new MauiControls.SolidColorBrush(scheme.Outline))
        .StrokeThickness(1)
        .StrokeShape(new RoundRectangle().CornerRadius(8))
        .Padding(12, 8);

    // The Repeat field is a disabled placeholder for now — it will open the §8 repeat-strategy page in a later slice.
    private static VisualNode RepeatField(MaterialScheme scheme) =>
        Border(
            VStack(
                Label("Repeat").FontSize(12).TextColor(scheme.OnSurfaceVariant),
                Label("Does not repeat").FontSize(15).TextColor(scheme.OnSurfaceVariant)
            ).Spacing(2)
        )
        .BackgroundColor(scheme.SurfaceContainer)
        .Stroke(new MauiControls.SolidColorBrush(scheme.Outline))
        .StrokeThickness(1)
        .StrokeShape(new RoundRectangle().CornerRadius(8))
        .Padding(12, 8);

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

        var amountError = TryAmount(State.AmountText, out var magnitude) ? null : "Enter an amount greater than zero";
        var descriptionError = string.IsNullOrWhiteSpace(State.Description) ? "Enter a description" : null;

        if (amountError is not null || descriptionError is not null)
        {
            SetState(s =>
            {
                s.AmountError = amountError;
                s.DescriptionError = descriptionError;
            });
            return;
        }

        var signed = State.IsIncome ? magnitude : -magnitude;
        account.AddTransaction(State.Date, signed, State.Description.Trim());

        await DismissKeyboardThenPop();
        Props.OnClosed?.Invoke(new TransactionOutcome(TransactionResult.Saved, State.Date));
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
}
