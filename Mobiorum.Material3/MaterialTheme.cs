using MauiReactor;
using Microsoft.Maui.Graphics;

namespace Mobiorum.Material3;

/// <summary>
/// A MauiReactor <see cref="Theme"/> driven by a Material 3 <see cref="MaterialSchemeSet"/>. It resolves the
/// light or dark <see cref="MaterialScheme"/> for the active app theme, maps the core roles onto MauiReactor
/// control styles, and publishes the active scheme as <see cref="Current"/> for components to read.
/// </summary>
/// <remarks>Subclass it and supply <see cref="Schemes"/> (e.g. from <see cref="MaterialSchemeSet.FromSeed"/>).</remarks>
public abstract class MaterialTheme : Theme
{
    /// <summary>The scheme in force for the active app theme. Read this for role colours when building components.</summary>
    public static MaterialScheme Current { get; private set; } = MaterialSchemeSet.FromSeed(Colors.Transparent).Light;

    /// <summary>The light/dark scheme pair this theme draws from.</summary>
    protected abstract MaterialSchemeSet Schemes { get; }

    protected override void OnApply()
    {
        var scheme = Schemes.For(IsLightTheme);
        Current = scheme;

        PageStyles.Default = _ => _
            .Padding(0)
            .BackgroundColor(scheme.Surface);

        LabelStyles.Default = _ => _
            .TextColor(scheme.OnSurface)
            .FontFamily("OpenSansRegular")
            .FontSize(14);

        ButtonStyles.Default = _ => _
            .BackgroundColor(scheme.Primary)
            .TextColor(scheme.OnPrimary)
            .FontFamily("OpenSansSemibold")
            .FontSize(14)
            .CornerRadius(20)
            .Padding(20, 10);

        EntryStyles.Default = _ => _
            .TextColor(scheme.OnSurface)
            .BackgroundColor(Colors.Transparent)
            .PlaceholderColor(scheme.OnSurfaceVariant)
            .FontFamily("OpenSansRegular")
            .FontSize(14);
    }
}
