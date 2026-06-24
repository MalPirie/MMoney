using Microsoft.Maui.Graphics;

namespace Mobiorum.Material3;

/// <summary>
/// A resolved set of Material 3 colour roles for one tone (light or dark), plus the app's extended semantic
/// colours. Consumers read roles from here and never need to know how the values were produced — see
/// <see cref="MaterialSchemeSet"/> for the seam.
/// </summary>
public sealed record MaterialScheme
{
    public required Color Primary { get; init; }
    public required Color OnPrimary { get; init; }
    public required Color PrimaryContainer { get; init; }
    public required Color OnPrimaryContainer { get; init; }

    public required Color Secondary { get; init; }
    public required Color OnSecondary { get; init; }
    public required Color SecondaryContainer { get; init; }
    public required Color OnSecondaryContainer { get; init; }

    public required Color Background { get; init; }
    public required Color OnBackground { get; init; }
    public required Color Surface { get; init; }
    public required Color OnSurface { get; init; }
    public required Color SurfaceContainer { get; init; }
    public required Color SurfaceVariant { get; init; }
    public required Color OnSurfaceVariant { get; init; }

    public required Color Outline { get; init; }
    public required Color OutlineVariant { get; init; }

    public required Color Error { get; init; }
    public required Color OnError { get; init; }

    /// <summary>Extended semantic colour for income (positive amounts), tuned for text on <see cref="Surface"/>.</summary>
    public required Color Income { get; init; }

    /// <summary>Extended semantic colour for expenses (negative amounts), tuned for text on <see cref="Surface"/>.</summary>
    public required Color Expense { get; init; }
}
