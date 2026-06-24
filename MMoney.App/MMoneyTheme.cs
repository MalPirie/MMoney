using Microsoft.Maui.Graphics;
using Mobiorum.Material3;

namespace MMoney.App;

/// <summary>MMoney's Material 3 theme: the orange (<c>#FFA500</c>) scheme applied through Mobiorum.Material3.</summary>
internal sealed class MMoneyTheme : MaterialTheme
{
    protected override MaterialSchemeSet Schemes { get; } = MaterialSchemeSet.FromSeed(Color.FromArgb("#FFA500"));
}
