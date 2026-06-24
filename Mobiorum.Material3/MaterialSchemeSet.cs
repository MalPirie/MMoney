using Microsoft.Maui.Graphics;

namespace Mobiorum.Material3;

/// <summary>
/// A light/dark pair of <see cref="MaterialScheme"/>s, produced from a seed colour.
/// </summary>
/// <remarks>
/// <see cref="FromSeed"/> is the seam. Today it returns a precomputed orange set tuned by hand (so the
/// brand orange and the extended income/expense colours can be reviewed and adjusted directly). A future
/// HCT generator can replace the body to honour the seed for real, without changing any caller —
/// consumers only ever read roles off a <see cref="MaterialScheme"/>.
/// </remarks>
public sealed class MaterialSchemeSet
{
    public required MaterialScheme Light { get; init; }
    public required MaterialScheme Dark { get; init; }

    /// <summary>The scheme for the given tone.</summary>
    public MaterialScheme For(bool isLight) => isLight ? Light : Dark;

    /// <summary>Produces a light/dark scheme set from a seed colour (currently a precomputed orange set).</summary>
    public static MaterialSchemeSet FromSeed(Color seed) => Orange;

    private static MaterialSchemeSet Orange { get; } = new()
    {
        Light = new MaterialScheme
        {
            Primary = Color.FromArgb("#B35A00"),               // banner orange: clears 4.5:1 with white text
            OnPrimary = Color.FromArgb("#FFFFFF"),
            PrimaryContainer = Color.FromArgb("#FFDCC2"),
            OnPrimaryContainer = Color.FromArgb("#2E1500"),
            Secondary = Color.FromArgb("#755945"),
            OnSecondary = Color.FromArgb("#FFFFFF"),
            SecondaryContainer = Color.FromArgb("#FFDCC2"),
            OnSecondaryContainer = Color.FromArgb("#2B1709"),
            Background = Color.FromArgb("#FFF8F4"),
            OnBackground = Color.FromArgb("#211A14"),
            Surface = Color.FromArgb("#FFF8F4"),
            OnSurface = Color.FromArgb("#211A14"),
            SurfaceContainer = Color.FromArgb("#F6EAE0"),
            SurfaceVariant = Color.FromArgb("#F3DFD1"),
            OnSurfaceVariant = Color.FromArgb("#52443A"),
            Outline = Color.FromArgb("#85735F"),
            OutlineVariant = Color.FromArgb("#D7C3B5"),
            Error = Color.FromArgb("#BA1A1A"),
            OnError = Color.FromArgb("#FFFFFF"),
            Income = Color.FromArgb("#2E7D32"),
            Expense = Color.FromArgb("#C62828"),
        },
        Dark = new MaterialScheme
        {
            Primary = Color.FromArgb("#FFB77C"),
            OnPrimary = Color.FromArgb("#4A2800"),
            PrimaryContainer = Color.FromArgb("#7A3F00"),
            OnPrimaryContainer = Color.FromArgb("#FFDCC2"),
            Secondary = Color.FromArgb("#E5BFA8"),
            OnSecondary = Color.FromArgb("#422B1A"),
            SecondaryContainer = Color.FromArgb("#5B4030"),
            OnSecondaryContainer = Color.FromArgb("#FFDCC2"),
            Background = Color.FromArgb("#1A120C"),
            OnBackground = Color.FromArgb("#EFE0D5"),
            Surface = Color.FromArgb("#1A120C"),
            OnSurface = Color.FromArgb("#EFE0D5"),
            SurfaceContainer = Color.FromArgb("#271C15"),
            SurfaceVariant = Color.FromArgb("#52443A"),
            OnSurfaceVariant = Color.FromArgb("#D7C3B5"),
            Outline = Color.FromArgb("#9F8D7B"),
            OutlineVariant = Color.FromArgb("#52443A"),
            Error = Color.FromArgb("#FFB4AB"),
            OnError = Color.FromArgb("#690005"),
            Income = Color.FromArgb("#81C784"),
            Expense = Color.FromArgb("#FF8A80"),
        },
    };
}
