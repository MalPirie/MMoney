using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>The <see cref="TextField"/>'s internal presentational state — just its focus flag.</summary>
public sealed class TextFieldState
{
    /// <summary>Whether the inner entry currently holds focus (drives the label/indicator accent).</summary>
    public bool Focused { get; set; }
}

/// <summary>
/// A Material 3 <em>filled</em> text field: a <c>surfaceContainer</c> container with rounded top corners, a label,
/// the entry, a bottom active indicator, and a supporting/error line. The label and indicator take the accent —
/// <c>primary</c> while focused, <c>error</c> when <see cref="Error"/> is set, otherwise <c>onSurfaceVariant</c>.
/// It owns only its focus state (presentational); the text itself is host-driven — the host passes <see cref="Text"/>
/// and updates it in <see cref="OnTextChanged"/> (ADR-0001). Seed-agnostic: it reads roles from
/// <see cref="MaterialTheme.Current"/>.
/// </summary>
public sealed partial class TextField : Component<TextFieldState>
{
    /// <summary>The field label. Set via <c>.Label(...)</c>.</summary>
    [Prop] string _label = string.Empty;

    /// <summary>The current text (host-driven). Set via <c>.Text(...)</c>.</summary>
    [Prop] string _text = string.Empty;

    /// <summary>Placeholder shown while empty. Set via <c>.Placeholder(...)</c>.</summary>
    [Prop] string _placeholder = string.Empty;

    /// <summary>Error text; when set, the field takes the error accent and shows this as supporting text. Set via <c>.Error(...)</c>.</summary>
    [Prop] string? _error;

    /// <summary>Optional supporting text shown when there is no error. Set via <c>.Supporting(...)</c>.</summary>
    [Prop] string? _supporting;

    /// <summary>The soft keyboard to use (e.g. numeric, capitalised). Set via <c>.Keyboard(...)</c>.</summary>
    [Prop] Keyboard? _keyboard;

    /// <summary>Invoked as the text changes. Set via <c>.OnTextChanged(...)</c>.</summary>
    [Prop] Action<string>? _onTextChanged;

    /// <summary>Optional content shown inside the container to the right of the entry (e.g. a unit toggle). Set via <c>.Trailing(...)</c>.</summary>
    [Prop] VisualNode? _trailing;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var hasError = !string.IsNullOrEmpty(_error);
        // The border takes the accent; the label follows the same focus/error signal on a neutral resting colour.
        var borderAccent = hasError ? scheme.Error : State.Focused ? scheme.Primary : scheme.Outline;
        var labelAccent = hasError ? scheme.Error : State.Focused ? scheme.Primary : scheme.OnSurfaceVariant;
        var supporting = _error ?? _supporting;

        // Component.Label(...): the generated .Label prop setter shadows the Label factory inside this class.
        var entry = Entry()
            .Text(_text)
            .Placeholder(_placeholder)
            .PlaceholderColor(scheme.OnSurfaceVariant)
            .TextColor(scheme.OnSurface)
            .BackgroundColor(Colors.Transparent)
            .Keyboard(_keyboard ?? Microsoft.Maui.Keyboard.Default) // qualify: the .Keyboard prop shadows the type here
            .OnTextChanged(t => _onTextChanged?.Invoke(t))
            .OnFocused(() => SetState(s => s.Focused = true))
            .OnUnfocused(() => SetState(s => s.Focused = false));

        // The entry, plus optional trailing content (e.g. a unit toggle) to its right — all inside the border.
        VisualNode entryRow = _trailing is null
            ? entry
            : Grid("*", "*,Auto", entry.GridColumn(0), _trailing.GridColumn(1)).ColumnSpacing(8);

        var children = new List<VisualNode>
        {
            Border(
                VStack(
                    Component.Label(_label).FontSize(12).TextColor(labelAccent),
                    entryRow
                ).Spacing(2)
            )
            .BackgroundColor(scheme.SurfaceContainer)
            .Stroke(new MauiControls.SolidColorBrush(borderAccent))
            .StrokeThickness(State.Focused || hasError ? 2 : 1) // outlined M3 field: the border carries the accent
            .StrokeShape(new RoundRectangle().CornerRadius(8))
            .Padding(12, 8),
        };

        if (!string.IsNullOrEmpty(supporting))
        {
            children.Add(Component.Label(supporting)
                .FontSize(12)
                .TextColor(hasError ? scheme.Error : scheme.OnSurfaceVariant)
                .Margin(16, 4, 0, 0));
        }

        return VStack([.. children]).Spacing(0);
    }
}
