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
    public static readonly string ArrowBack = char.ConvertFromUtf32(0xE5C4);     // arrow_back (top app bar)
    public static readonly string ChevronLeft = char.ConvertFromUtf32(0xE5CB);  // chevron_left
    public static readonly string ChevronRight = char.ConvertFromUtf32(0xE5CC); // chevron_right
    public static readonly string ArrowDropDown = char.ConvertFromUtf32(0xE5C5); // arrow_drop_down (combo box)
    public static readonly string Home = char.ConvertFromUtf32(0xE88A);         // home (TabStrip Home button default)
    public static readonly string CalendarMonth = char.ConvertFromUtf32(0xEBCC); // calendar_month
    public static readonly string Event = char.ConvertFromUtf32(0xE878);         // event (calendar with a single date)
    public static readonly string Today = char.ConvertFromUtf32(0xE8DF);         // today (calendar with the current date)
    public static readonly string Print = char.ConvertFromUtf32(0xE8AD);         // print (overflow menu)
    public static readonly string Download = char.ConvertFromUtf32(0xF090);      // download (overflow menu — Export)
    public static readonly string Settings = char.ConvertFromUtf32(0xE8B8);      // settings (overflow menu)
}
