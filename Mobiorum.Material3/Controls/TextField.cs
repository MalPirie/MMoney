using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>The <see cref="TextField"/>'s internal presentational state — just its focus flag.</summary>
public sealed class TextFieldState
{
    /// <summary>Whether the inner entry currently holds focus (drives the label/outline accent and floats the label).</summary>
    public bool Focused { get; set; }
}

/// <summary>
/// A Material 3 <em>outlined</em> text field: a transparent box with a rounded outline, a label notched into the top
/// of that outline, an inner entry (or host-supplied content), and a supporting/error line. The outline and label
/// take the accent — <c>primary</c> while focused, <c>error</c> when <see cref="Error"/> is set, otherwise
/// <c>outline</c>/<c>onSurfaceVariant</c>; the outline thickens only on focus. The label always sits in the notch so
/// every field reads consistently whether or not it holds a value, with any placeholder/hint shown inside. It owns
/// only its focus state (presentational); the text is host-driven — the host passes <see cref="Text"/> and updates it
/// in <see cref="OnTextChanged"/> (ADR-0001). Seed-agnostic: it reads roles from <see cref="MaterialTheme.Current"/>.
/// </summary>
public sealed partial class TextField : Component<TextFieldState>
{
    /// <summary>The field label. Set via <c>.Label(...)</c>.</summary>
    [Prop] string _label = string.Empty;

    /// <summary>The current text (host-driven). Set via <c>.Text(...)</c>.</summary>
    [Prop] string _text = string.Empty;

    /// <summary>Placeholder/hint shown inside once the label has floated up. Set via <c>.Placeholder(...)</c>.</summary>
    [Prop] string _placeholder = string.Empty;

    /// <summary>Error text; when set, the field takes the error accent and shows this as supporting text. Set via <c>.Error(...)</c>.</summary>
    [Prop] string? _error;

    /// <summary>Optional supporting text shown when there is no error. Set via <c>.Supporting(...)</c>.</summary>
    [Prop] string? _supporting;

    /// <summary>The soft keyboard to use (e.g. numeric, capitalised). Set via <c>.Keyboard(...)</c>.</summary>
    [Prop] Keyboard? _keyboard;

    /// <summary>Invoked as the text changes. Set via <c>.OnTextChanged(...)</c>.</summary>
    [Prop] Action<string>? _onTextChanged;

    /// <summary>Invoked when the inner entry gains focus (lets the host track the active field). Set via <c>.OnFocused(...)</c>.</summary>
    [Prop] Action? _onFocused;

    /// <summary>Forces the focused appearance (accent outline/label) even without real focus — e.g. while a picker
    /// the field owns is open. OR-ed with the real focus state. Set via <c>.Active(...)</c>.</summary>
    [Prop] bool _active;

    /// <summary>Optional content shown inside the outline to the right of the entry (e.g. a unit toggle). Set via <c>.Trailing(...)</c>.</summary>
    [Prop] VisualNode? _trailing;

    /// <summary>
    /// Host-supplied inner content that replaces the built entry (e.g. a date picker or a static value). When set,
    /// the field always reads as populated, so the label stays floated. The content should centre itself vertically
    /// (e.g. <c>.VCenter()</c>). Set via <c>.Content(...)</c>.
    /// </summary>
    [Prop] VisualNode? _content;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var hasError = !string.IsNullOrEmpty(_error);
        // Real focus or a host-forced "active" (e.g. this field's picker is open) both read as focused.
        var focused = State.Focused || _active;

        // The outline takes the accent; the floating label follows the same focus/error signal on a resting colour.
        var borderAccent = hasError ? scheme.Error : focused ? scheme.Primary : scheme.Outline;
        var labelAccent = hasError ? scheme.Error : focused ? scheme.Primary : scheme.OnSurfaceVariant;
        var supporting = _error ?? _supporting;

        // The field's inner content: host-supplied (a picker, a static value) or the built text entry.
        var inner = _content ?? BuildEntry(scheme);

        // The entry, plus optional trailing content (e.g. a unit toggle) to its right — all inside the outline.
        var innerRow = _trailing is null
            ? inner
            : Grid("*", "*,Auto", inner.GridColumn(0), _trailing.GridColumn(1)).ColumnSpacing(8);

        // The label always straddles the top outline (notched), so every field reads the same whether or not it has
        // a value; its surface-coloured chip masks the stroke behind the text. The 12dp left margin + 4dp inner
        // padding line the text up over the box's 16dp content inset. Component.Label(...): the generated .Label prop
        // setter shadows the Label factory inside this class.
        var frame = Grid(
            Border(innerRow)
                .BackgroundColor(Colors.Transparent) // outlined M3 field: no fill, the outline carries the accent
                .Stroke(new MauiControls.SolidColorBrush(borderAccent))
                .StrokeThickness(focused ? 2 : 1) // M3: outline thickens on focus (error stays thin at rest)
                .StrokeShape(new RoundRectangle().CornerRadius(8))
                // Shrink the padding by the extra 1px of stroke on focus so the total inset (stroke + padding) — and
                // therefore the content/value position — stays put instead of nudging as the outline thickens.
                .Padding(focused ? 15 : 16, 0, _trailing is null ? (focused ? 15 : 16) : (focused ? 7 : 8), 0)
                .MinimumHeightRequest(56),
            Grid(Component.Label(_label).FontSize(12).TextColor(labelAccent))
                .BackgroundColor(scheme.Surface)
                .Padding(4, 0)
                .HStart()
                .VStart()
                .Margin(12, 0, 0, 0)
                .TranslationY(-8));

        var children = new List<VisualNode> { frame };

        if (!string.IsNullOrEmpty(supporting))
        {
            children.Add(Component.Label(supporting)
                .FontSize(12)
                .TextColor(hasError ? scheme.Error : scheme.OnSurfaceVariant)
                .Margin(16, 4, 0, 0));
        }

        return VStack([.. children]).Spacing(0);
    }

    // The inner text entry. The label always floats into the outline, so the entry just shows the placeholder/hint.
    private VisualNode BuildEntry(MaterialScheme scheme) =>
        Entry()
            .Text(_text)
            .Placeholder(_placeholder)
            .PlaceholderColor(scheme.OnSurfaceVariant)
            .TextColor(scheme.OnSurface)
            .BackgroundColor(Colors.Transparent)
            .VCenter()
            .Keyboard(_keyboard ?? Microsoft.Maui.Keyboard.Default) // qualify: the .Keyboard prop shadows the type here
            .OnTextChanged(t => _onTextChanged?.Invoke(t))
            .OnFocused(() =>
            {
                SetState(s => s.Focused = true);
                _onFocused?.Invoke();
            })
            .OnUnfocused(() => SetState(s => s.Focused = false));
}
