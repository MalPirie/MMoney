using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>The <see cref="Calendar"/>'s transient presentational state: the in-flight draft selection and the
/// month currently on screen. Seeded from the props on mount; the host never sees it until OK (ADR-0006 / ADR-0001).</summary>
public sealed class CalendarState
{
    /// <summary>The highlighted draft day — committed only when OK is pressed.</summary>
    public DateOnly Draft { get; set; }

    /// <summary>The visible month's year.</summary>
    public int Year { get; set; }

    /// <summary>The visible month (1–12).</summary>
    public int Month { get; set; }
}

/// <summary>
/// A Material 3 modal date-picker surface: a rounded card holding a month grid (Monday-start), a "July 2026"
/// title with prev/next month arrows, and Cancel/OK. Presentational and seed-agnostic — it reads role colours
/// from <see cref="MaterialTheme.Current"/> and depends only on BCL <see cref="DateOnly"/> (no app types). It
/// owns its transient draft selection and visible month (ADR-0001); the host passes the committed
/// <see cref="SelectedDate"/>, the edit-lock <see cref="MinDate"/>, the app clock's <see cref="Today"/>, and
/// receives the pick through <see cref="OnConfirm"/> (OK) or a dismissal through <see cref="OnCancel"/>. The
/// host owns the scrim/overlay and modality (ADR-0006). The year-list view is deferred; navigate with the arrows.
/// </summary>
public sealed partial class Calendar : Component<CalendarState>
{
    private const double Cell = 40; // day-cell (and header/arrow) box, dp
    private const int Weeks = 6;    // always render six week-rows so the card height is constant across months

    // Monday-start single-letter weekday headers.
    private static readonly string[] WeekdayLetters = ["M", "T", "W", "T", "F", "S", "S"];

    /// <summary>The committed date to seed the draft/visible month from. Set via <c>.SelectedDate(...)</c>.</summary>
    [Prop] DateOnly _selectedDate;

    /// <summary>The earliest selectable date (the edit lock); earlier days disable, as does the prev arrow at its month. Null = unbounded. Set via <c>.MinDate(...)</c>.</summary>
    [Prop] DateOnly? _minDate;

    /// <summary>The app clock's "today", for the today-ring — injected, not read from the system. Set via <c>.Today(...)</c>.</summary>
    [Prop] DateOnly _today;

    /// <summary>Invoked with the picked date when OK is pressed. Set via <c>.OnConfirm(...)</c>.</summary>
    [Prop] Action<DateOnly>? _onConfirm;

    /// <summary>Invoked when Cancel is pressed (the host also calls this on scrim-tap/back). Set via <c>.OnCancel(...)</c>.</summary>
    [Prop] Action? _onCancel;

    protected override void OnMounted()
    {
        // Fresh mount each open (the dialog is conditionally rendered), so the draft always reseeds from the
        // committed value and a cancelled session leaves nothing behind.
        State.Draft = _selectedDate;
        State.Year = _selectedDate.Year;
        State.Month = _selectedDate.Month;
        base.OnMounted();
    }

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;

        return Border(
            VStack(
                Header(scheme),
                WeekdayHeader(scheme),
                DayGrid(scheme),
                Actions(scheme)
            ).Spacing(12)
        )
        .BackgroundColor(scheme.SurfaceContainer)
        .StrokeThickness(0)
        .StrokeShape(new RoundRectangle().CornerRadius(28)) // M3 date-picker container: extra-large corners
        .Shadow(Elevation.Level2)
        .Padding(16);
    }

    // "July 2026" centred, flanked by prev/next month arrows. The prev arrow disables at the edit-lock month.
    private VisualNode Header(MaterialScheme scheme)
    {
        var title = new DateOnly(State.Year, State.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var canPrev = CalendarGrid.HasPreviousMonth(State.Year, State.Month, _minDate);

        return Grid("Auto", "Auto,*,Auto",
            ArrowButton(MaterialSymbols.ChevronLeft, scheme, canPrev, PreviousMonth).GridColumn(0),
            Label(title).FontSize(14).FontAttributes(MauiControls.FontAttributes.Bold)
                .TextColor(scheme.OnSurface).HCenter().VCenter().GridColumn(1),
            ArrowButton(MaterialSymbols.ChevronRight, scheme, true, NextMonth).GridColumn(2)
        );
    }

    private VisualNode WeekdayHeader(MaterialScheme scheme)
    {
        var cols = string.Join(",", Enumerable.Repeat($"{Cell}", 7));
        var labels = WeekdayLetters.Select((letter, i) =>
            Label(letter).FontSize(12).TextColor(scheme.OnSurfaceVariant).HCenter().VCenter().GridColumn(i));
        return Grid("Auto", cols, [.. labels]);
    }

    private VisualNode DayGrid(MaterialScheme scheme)
    {
        var cells = CalendarGrid.ForMonth(State.Year, State.Month, _minDate);
        // Fixed six-row height (some months need only four or five) so the card doesn't resize when paging months.
        var rows = string.Join(",", Enumerable.Repeat($"{Cell}", Weeks));
        var cols = string.Join(",", Enumerable.Repeat($"{Cell}", 7));

        var nodes = new List<VisualNode>();
        for (var i = 0; i < cells.Count; i++)
        {
            if (cells[i].Date is null)
            {
                continue; // leading blank — nothing rendered
            }

            nodes.Add(DayCell(cells[i], scheme).GridRow(i / 7).GridColumn(i % 7));
        }

        return Grid(rows, cols, [.. nodes]);
    }

    // A 40dp day box: filled Primary circle when it's the draft, a Primary ring when it's today, plain otherwise;
    // disabled days (before the edit lock) are dimmed and inert.
    private VisualNode DayCell(CalendarCell cell, MaterialScheme scheme)
    {
        var date = cell.Date!.Value;
        var isSelected = date == State.Draft;
        var isToday = date == _today;

        var fill = isSelected ? scheme.Primary : Colors.Transparent;
        var ringOnly = isToday && !isSelected;
        var textColor = !cell.Enabled ? scheme.OnSurfaceVariant.WithAlpha(0.38f)
            : isSelected ? scheme.OnPrimary
            : isToday ? scheme.Primary
            : scheme.OnSurface;

        var box = Border(
                Label($"{date.Day}").FontSize(14).TextColor(textColor).HCenter().VCenter()
            )
            .BackgroundColor(fill)
            .Stroke(new MauiControls.SolidColorBrush(ringOnly ? scheme.Primary : Colors.Transparent))
            .StrokeThickness(ringOnly ? 1 : 0)
            .StrokeShape(new RoundRectangle().CornerRadius(Cell / 2))
            .WidthRequest(Cell)
            .HeightRequest(Cell);

        return cell.Enabled ? box.OnTapped(() => SetState(s => s.Draft = date)) : box;
    }

    private VisualNode Actions(MaterialScheme scheme) =>
        HStack(
            TextButton("Cancel", scheme, () => _onCancel?.Invoke()),
            TextButton("OK", scheme, () => _onConfirm?.Invoke(State.Draft))
        ).Spacing(8).HEnd();

    // A transparent M3 text button (Primary label).
    private static VisualNode TextButton(string text, MaterialScheme scheme, Action onClicked) =>
        Button(text)
            .BackgroundColor(Colors.Transparent)
            .TextColor(scheme.Primary)
            .FontSize(14)
            .Padding(12, 8)
            .OnClicked(onClicked);

    // A transparent icon button; dimmed and inert when disabled.
    private static VisualNode ArrowButton(string glyph, MaterialScheme scheme, bool enabled, Action onClicked)
    {
        var button = Button(glyph)
            .FontFamily(MaterialSymbols.FontFamily)
            .FontSize(20)
            .BackgroundColor(Colors.Transparent)
            .TextColor(enabled ? scheme.OnSurfaceVariant : scheme.OnSurfaceVariant.WithAlpha(0.38f))
            .Padding(0)
            .CornerRadius((int)(Cell / 2))
            .WidthRequest(Cell)
            .HeightRequest(Cell);
        return enabled ? button.OnClicked(onClicked) : button;
    }

    private void PreviousMonth() => ShiftMonth(-1);

    private void NextMonth() => ShiftMonth(1);

    private void ShiftMonth(int delta)
    {
        var shifted = new DateOnly(State.Year, State.Month, 1).AddMonths(delta);
        SetState(s =>
        {
            s.Year = shifted.Year;
            s.Month = shifted.Month;
        });
    }
}
