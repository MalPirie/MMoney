using System.Linq;
using MauiReactor;
using Mobiorum.Material3;

namespace MMoney.App.Components.Sandbox;

internal sealed class TabbedPageViewSandboxState
{
    /// <summary>Host-owned selection — the <see cref="TabbedPageView{TItem}"/> reports swipes/taps here.</summary>
    public int Selected = 5;
}

/// <summary>
/// Dev-only harness for <see cref="TabbedPageView{TItem}"/> (ADR-0003). Drives it with a synthetic <see cref="int"/>
/// sequence mirroring MMoney's shape — <b>bounded back</b> at 0 (the edit-lock analog), <b>open forward</b> — with
/// a Home anchor and a scrollable placeholder page per item, to validate swipe-to-change-selection, tab↔page sync,
/// vertical scroll within a page, and forward append-on-demand. Hosted from the Paged nav destination.
/// </summary>
internal sealed partial class TabbedPageViewSandbox : Component<TabbedPageViewSandboxState>
{
    private const int Floor = 0;

    private static readonly string[] Shapes =
        { "Jan", "September", "May 2026", "Q3", "Wednesday", "7", "Mid-month", "Oct" };

    private static string TabLabel(int i) => $"{Shapes[i % Shapes.Length]} · {i}";

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;

        return Grid("Auto,*", "*",
            Label($"TabbedPageView — selected {State.Selected}")
                .FontSize(12)
                .TextColor(scheme.OnSurfaceVariant)
                .Margin(16, 8)
                .GridRow(0),

            new TabbedPageView<int>()
                .Selected(State.Selected)
                .Next(i => i + 1)                              // open-ended forward
                .Prev(i => i > Floor ? i - 1 : (int?)null)     // bounded back at the edit-lock analog
                .Label(TabLabel)
                .Home(5)
                .Page(i => Page(scheme, i))
                .OnSelectedChanged(i => SetState(s => s.Selected = i))
                .GridRow(1)
        );
    }

    // A throwaway scrollable page: enough rows to exercise vertical scroll inside a horizontally-swiped page.
    private static VisualNode Page(MaterialScheme scheme, int item)
    {
        var rows = Enumerable.Range(1, 30).Select(n =>
            (VisualNode)Border(
                Label($"{TabLabel(item)} — row {n}")
                    .FontSize(15)
                    .TextColor(scheme.OnSurface)
                    .VCenter()
                    .Padding(16, 0)
            )
            .BackgroundColor(scheme.SurfaceContainer)
            .StrokeThickness(0)
            .HeightRequest(56));

        return VStack([.. rows]).Spacing(8).Padding(16);
    }
}
