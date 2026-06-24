namespace Mobiorum.Material3;

/// <summary>
/// Glyphs from the Material Symbols (Outlined) icon font, by codepoint. The consuming app registers the font
/// under <see cref="FontFamily"/>; use these as the text of a label/button with that font family.
/// </summary>
public static class MaterialSymbols
{
    /// <summary>The font-family alias the app registers the Material Symbols font under.</summary>
    public const string FontFamily = "MaterialSymbols";

    public static readonly string Add = char.ConvertFromUtf32(0xE145);       // add
    public static readonly string MoreVert = char.ConvertFromUtf32(0xE5D4);  // more_vert
    public static readonly string Repeat = char.ConvertFromUtf32(0xE040);    // repeat
    public static readonly string List = char.ConvertFromUtf32(0xE896);      // list
}
