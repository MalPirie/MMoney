using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using MauiReactor;
using MauiReactor.Shapes;
using Mobiorum.Material3;
using MMoney.App.Input;
using MMoney.App.Platform;
using MMoney.App.Repeat;
using MMoney.Core.Repeat;

namespace MMoney.App.Components;

/// <summary>Props for the pushed <see cref="RepeatStrategyPage"/> — the origin context, the seed rule, and the callback.</summary>
internal sealed class RepeatStrategyProps
{
    public DateOnly Origin { get; set; }
    public DateOnly? MinDate { get; set; }
    public DateOnly Today { get; set; }
    public RepeatStrategy Strategy { get; set; } = new RepeatStrategy.Never();
    public RepeatEndCondition EndCondition { get; set; } = new RepeatEndCondition.Forever();

    /// <summary>Invoked as the page closes: the chosen rule on Done, or null on back/cancel.</summary>
    public Action<(RepeatStrategy Strategy, RepeatEndCondition End)?>? OnClosed { get; set; }
}

internal enum RepeatUnit { Day, Week, Month, Year }

internal enum EndKind { Never, After, On }

internal enum ComboKind { None, Unit, Monthly }

internal sealed class RepeatStrategyState
{
    public RepeatUnit Unit { get; set; }
    public string IntervalText { get; set; } = "1";
    public int WeekMask { get; set; }          // DaysOfWeek bits (bit0 = Monday)
    public DayInMonth MonthOption { get; set; } // selected Monthly day-in-month
    public EndKind EndKind { get; set; }
    public string CountText { get; set; } = "10";
    public DateOnly UntilDate { get; set; }          // committed (last-good) end date
    public string UntilText { get; set; } = string.Empty; // the end-date field's masked text buffer
    public bool DateActive { get; set; }             // the end-date field reads as the active/focused one
    public bool CalendarOpen { get; set; }
    public ComboKind ComboOpen { get; set; }   // which combo's floating list is showing
    public Rect ComboAnchor { get; set; }      // the tapped combo's on-screen rect, to anchor its dropdown
}

/// <summary>
/// The §8 custom repeat-strategy editor (ADR-0007): a pushed page building a <see cref="RepeatStrategy"/> +
/// <see cref="RepeatEndCondition"/>. Rows: interval field + unit <see cref="ComboBox"/>; Weekly →
/// <see cref="DayOfWeekPicker"/>; Monthly → a day-in-month <see cref="ComboBox"/>; Ends radios (Never / After N /
/// On date), the date picked via the <see cref="Calendar"/> in a <see cref="ModalHost"/>. Numbers coerce to ≥ 1 on
/// Done, so Done is always valid; back discards. The chosen rule returns through <c>OnClosed</c>.
/// </summary>
partial class RepeatStrategyPage : Component<RepeatStrategyState, RepeatStrategyProps>
{
    protected override void OnMounted()
    {
        SeedFrom(Props.Strategy, Props.EndCondition);
        base.OnMounted();
    }

    private void SeedFrom(RepeatStrategy strategy, RepeatEndCondition end)
    {
        // Frequency defaults for the fields a given strategy doesn't carry.
        State.WeekMask = (int)RepeatEditing.ToDaysOfWeek(Props.Origin.DayOfWeek);
        State.MonthOption = DayInMonth.DayOfMonth;

        switch (strategy)
        {
            case RepeatStrategy.Daily d: State.Unit = RepeatUnit.Day; State.IntervalText = d.Interval.ToString(); break;
            case RepeatStrategy.Weekly w: State.Unit = RepeatUnit.Week; State.IntervalText = w.Interval.ToString(); State.WeekMask = (int)w.Days; break;
            case RepeatStrategy.Monthly m: State.Unit = RepeatUnit.Month; State.IntervalText = m.Interval.ToString(); State.MonthOption = m.DayInMonth; break;
            case RepeatStrategy.Yearly y: State.Unit = RepeatUnit.Year; State.IntervalText = y.Interval.ToString(); break;
            default: State.Unit = RepeatUnit.Month; State.IntervalText = "1"; break; // Never shouldn't reach here
        }

        State.UntilDate = Props.Origin.AddYears(1);
        switch (end)
        {
            case RepeatEndCondition.AfterOccurrences a: State.EndKind = EndKind.After; State.CountText = a.Occurrences.ToString(); break;
            case RepeatEndCondition.UntilDate u: State.EndKind = EndKind.On; State.UntilDate = u.Date; break;
            default: State.EndKind = EndKind.Never; break;
        }

        State.UntilText = DateEntry.Format(State.UntilDate);
    }

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;

        var body = new List<VisualNode> { FrequencyRow(scheme) };
        if (State.Unit == RepeatUnit.Week)
        {
            body.Add(SectionCaption("Repeat on", scheme));
            body.Add(new DayOfWeekPicker().Selected(State.WeekMask).OnChanged(m =>
            {
                SoftKeyboard.Hide();
                SetState(s => s.WeekMask = m);
            }));
        }
        else if (State.Unit == RepeatUnit.Month)
        {
            body.Add(MonthlyRow(scheme));
        }

        body.Add(SectionCaption("Ends", scheme));
        body.Add(EndSection(scheme));

        var content = new List<VisualNode>
        {
            new TopAppBar()
                .Title("Custom repeat")
                .Container(scheme.Primary)
                .OnContainer(scheme.OnPrimary)
                .OnBack(Cancel)
                .ActionText("Done")
                .OnAction(Done)
                .GridRow(0),
            ScrollView(VStack([.. body]).Spacing(20).Padding(16)).GridRow(1),
            new PopoverHost()
                .IsOpen(State.ComboOpen != ComboKind.None)
                .OnDismiss(CloseCombo)
                .Anchor(State.ComboAnchor)
                .Content(ComboList(scheme))
                .GridRowSpan(2),
            new ModalHost()
                .IsOpen(State.CalendarOpen)
                .OnDismiss(() => SetState(s => s.CalendarOpen = false))
                .Content(EndDateCalendar())
                .GridRowSpan(2),
        };

        return ContentPage(Grid("Auto,*", "*", [.. content])).HasNavigationBar(false);
    }

    private static readonly string[] UnitLabels = ["Day", "Week", "Month", "Year"];

    // "Every [N]" field + unit combo.
    private VisualNode FrequencyRow(MaterialScheme scheme) =>
        Grid("Auto", "110,*",
            new TextField()
                .Label("Every")
                .Text(State.IntervalText)
                .Keyboard(Keyboard.Numeric)
                .OnFocused(() => SetState(s => s.DateActive = false))
                .OnTextChanged(t => SetState(s => s.IntervalText = t))
                .GridColumn(0),
            new ComboBox()
                .Label("Period")
                .Value(UnitLabels[(int)State.Unit])
                .Open(State.ComboOpen == ComboKind.Unit)
                .OnTap(rect => OpenCombo(ComboKind.Unit, rect))
                .GridColumn(1)
        ).ColumnSpacing(12);

    // The Monthly day-in-month combo, its options derived from the origin.
    private VisualNode MonthlyRow(MaterialScheme scheme) =>
        new ComboBox()
            .Label("On")
            .Value(RepeatDescription.DescribeMonthlyOption(State.MonthOption, Props.Origin))
            .Open(State.ComboOpen == ComboKind.Monthly)
            .OnTap(rect => OpenCombo(ComboKind.Monthly, rect));

    // The floating tap-to-select list for whichever combo is open (ModalHost centres it; tap picks and closes).
    private VisualNode ComboList(MaterialScheme scheme)
    {
        var (labels, selected, pick) = State.ComboOpen switch
        {
            ComboKind.Unit => (UnitLabels, (int)State.Unit,
                (Action<int>)(i => SetState(s => { s.Unit = (RepeatUnit)i; s.ComboOpen = ComboKind.None; }))),
            ComboKind.Monthly => MonthlyComboData(),
            _ => (Array.Empty<string>(), 0, (Action<int>)(_ => { })),
        };

        var rows = labels.Select((label, i) =>
            Grid("48", "*", Label(label).FontSize(15).TextColor(scheme.OnSurface).VCenter())
                .Padding(20, 0)
                .BackgroundColor(i == selected ? scheme.SurfaceVariant : Colors.Transparent)
                .OnTapped(() => pick(i)));

        return Border(VStack([.. rows]).Spacing(0))
            .BackgroundColor(scheme.SurfaceContainer)
            .StrokeThickness(0)
            .StrokeShape(new RoundRectangle().CornerRadius(8))
            .Shadow(Elevation.Level2)
            .Padding(0, 8); // width comes from the anchor (matches the field) via PopoverHost
    }

    private (string[] Labels, int Selected, Action<int> Pick) MonthlyComboData()
    {
        var options = RepeatEditing.MonthlyOptions(Props.Origin);
        var labels = options.Select(o => RepeatDescription.DescribeMonthlyOption(o, Props.Origin)).ToArray();
        var selected = Math.Max(0, options.ToList().IndexOf(State.MonthOption));
        return (labels, selected, i => SetState(s => { s.MonthOption = options[i]; s.ComboOpen = ComboKind.None; }));
    }

    private void OpenCombo(ComboKind kind, Rect anchor)
    {
        SoftKeyboard.Hide();
        SetState(s => { s.ComboOpen = kind; s.ComboAnchor = anchor; });
    }

    private void CloseCombo() => SetState(s => s.ComboOpen = ComboKind.None);

    private VisualNode EndSection(MaterialScheme scheme)
    {
        // Each option's input sits directly under its own radio (indented), shown only when that option is selected.
        var rows = new List<VisualNode> { EndRadio(EndKind.Never, "Never", scheme) };

        rows.Add(EndRadio(EndKind.After, "After a number of occurrences", scheme));
        if (State.EndKind == EndKind.After)
        {
            rows.Add(Indented(new TextField()
                .Label("Occurrences")
                .Text(State.CountText)
                .Keyboard(Keyboard.Numeric)
                .OnFocused(() => SetState(s => s.DateActive = false))
                .OnTextChanged(t => SetState(s => s.CountText = t))));
        }

        rows.Add(EndRadio(EndKind.On, "On a date", scheme));
        if (State.EndKind == EndKind.On)
        {
            rows.Add(Indented(EndDateField(scheme)));
        }

        return VStack([.. rows]).Spacing(8);
    }

    // Indent a sub-input so it reads as belonging to the radio option above it (past the radio + gap).
    private static VisualNode Indented(VisualNode child) => Grid(child).Margin(32, 0, 0, 0);

    private VisualNode EndRadio(EndKind kind, string label, MaterialScheme scheme) =>
        Grid("Auto", "Auto,*",
            Radio(State.EndKind == kind, scheme).GridColumn(0),
            Label(label).FontSize(15).TextColor(scheme.OnSurface).VCenter().GridColumn(1)
        )
        .ColumnSpacing(12)
        .MinimumHeightRequest(44)
        .OnTapped(() =>
        {
            SoftKeyboard.Hide();
            SetState(s => s.EndKind = kind);
        });

    // The end date as an editable dd/MM/yyyy field with a trailing calendar button — the same dual-mode field as the
    // transaction page: type it or pick it. Weekday rides as the supporting hint.
    private VisualNode EndDateField(MaterialScheme scheme) =>
        new TextField()
            .Label("Date")
            .Text(State.UntilText)
            .Placeholder("dd/mm/yyyy")
            .Keyboard(Keyboard.Numeric)
            .Supporting(DateEntry.Weekday(State.UntilDate))
            .Active(State.DateActive)
            .OnFocused(() => SetState(s => s.DateActive = true))
            .OnTextChanged(OnUntilChanged)
            .Trailing(Grid(Label(MaterialSymbols.CalendarMonth).FontFamily(MaterialSymbols.FontFamily).FontSize(22)
                .TextColor(scheme.OnSurfaceVariant).VCenter()).OnTapped(OpenCalendar));

    // Mask as typed; live-commit when a complete date on/after the origin is entered (keeping the last-good otherwise).
    private void OnUntilChanged(string text)
    {
        var masked = DateEntry.Mask(text);
        SetState(s =>
        {
            s.UntilText = masked;
            if (DateEntry.TryParse(masked, out var date) && date >= Props.Origin)
            {
                s.UntilDate = date;
            }
        });
    }

    private VisualNode EndDateCalendar() =>
        new Mobiorum.Material3.Calendar()
            .SelectedDate(State.UntilDate)
            .MinDate(Props.Origin) // the stop must be on/after the origin
            .Today(Props.Today)
            .OnConfirm(d => SetState(s => { s.UntilDate = d; s.UntilText = DateEntry.Format(d); s.CalendarOpen = false; }))
            .OnCancel(() => SetState(s => s.CalendarOpen = false));

    private void OpenCalendar()
    {
        SoftKeyboard.Hide();
        SetState(s => { s.CalendarOpen = true; s.DateActive = true; });
    }

    private static VisualNode SectionCaption(string text, MaterialScheme scheme) =>
        Label(text).FontSize(12).FontAttributes(MauiControls.FontAttributes.Bold).TextColor(scheme.OnSurfaceVariant);

    // A 20dp radio ring with a 10dp filled dot when selected.
    private static VisualNode Radio(bool selected, MaterialScheme scheme)
    {
        var inner = new List<VisualNode>();
        if (selected)
        {
            inner.Add(Border().BackgroundColor(scheme.Primary).StrokeThickness(0)
                .StrokeShape(new RoundRectangle().CornerRadius(5)).WidthRequest(10).HeightRequest(10).HCenter().VCenter());
        }

        return Border(Grid([.. inner]))
            .BackgroundColor(Colors.Transparent)
            .Stroke(new MauiControls.SolidColorBrush(selected ? scheme.Primary : scheme.OnSurfaceVariant))
            .StrokeThickness(2)
            .StrokeShape(new RoundRectangle().CornerRadius(10))
            .WidthRequest(20).HeightRequest(20)
            .VCenter();
    }

    private void Cancel()
    {
        Props.OnClosed?.Invoke(null);
        _ = Navigation?.PopAsync();
    }

    // Build the rule (numbers coerced to ≥ 1), hand it back, and pop.
    private void Done()
    {
        var interval = RepeatEditing.CoerceCount(State.IntervalText);
        RepeatStrategy strategy = State.Unit switch
        {
            RepeatUnit.Day => new RepeatStrategy.Daily(interval),
            RepeatUnit.Week => new RepeatStrategy.Weekly(interval, (DaysOfWeek)State.WeekMask),
            RepeatUnit.Month => new RepeatStrategy.Monthly(interval, State.MonthOption),
            _ => new RepeatStrategy.Yearly(interval),
        };

        RepeatEndCondition end = State.EndKind switch
        {
            EndKind.After => new RepeatEndCondition.AfterOccurrences(RepeatEditing.CoerceCount(State.CountText)),
            EndKind.On => new RepeatEndCondition.UntilDate(State.UntilDate),
            _ => new RepeatEndCondition.Forever(),
        };

        Props.OnClosed?.Invoke((strategy, end));
        _ = Navigation?.PopAsync();
    }
}
