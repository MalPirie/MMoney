namespace Mobiorum.Material3;

/// <summary>
/// One destination in a <see cref="NavigationBar"/>. Its <see cref="Icon"/> and <see cref="Label"/> are stable
/// identity, set at construction; its selected state and tap callback vary per render and are set fluently. A
/// plain config object — the bar does the rendering and owns no selection state.
/// </summary>
public sealed class NavDestination
{
    private bool _selected;
    private Action? _onSelected;

    public NavDestination(string icon, string label)
    {
        Icon = icon;
        Label = label;
    }

    /// <summary>The Material Symbols glyph shown for this destination.</summary>
    public string Icon { get; }

    /// <summary>The text label shown beneath the icon.</summary>
    public string Label { get; }

    /// <summary>Marks this destination as the selected one (drives the selection pill and colours).</summary>
    public NavDestination Selected(bool selected)
    {
        _selected = selected;
        return this;
    }

    /// <summary>Sets the action invoked when this destination is tapped.</summary>
    public NavDestination OnSelected(Action onSelected)
    {
        _onSelected = onSelected;
        return this;
    }

    internal bool IsSelected => _selected;

    internal void Invoke() => _onSelected?.Invoke();
}
