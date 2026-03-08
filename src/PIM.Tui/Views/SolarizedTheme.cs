using Terminal.Gui.Drawing;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace PIM.Tui.Views;

/// <summary>
/// Solarized Dark color palette and pre-built attributes for consistent TUI theming.
/// https://ethanschoonover.com/solarized/
/// </summary>
internal static class Sol
{
    // Solarized base colors (dark background)
    public static readonly Color Base03  = new(0, 43, 54);      // background
    public static readonly Color Base02  = new(7, 54, 66);      // background highlights
    public static readonly Color Base01  = new(88, 110, 117);   // comments / secondary content
    public static readonly Color Base00  = new(101, 123, 131);  // mid-tone
    public static readonly Color Base0   = new(131, 148, 150);  // body text
    public static readonly Color Base1   = new(147, 161, 161);  // emphasized content
    public static readonly Color Base2   = new(238, 232, 213);  // light background
    public static readonly Color Base3   = new(253, 246, 227);  // bright background

    // Solarized accent colors
    public static readonly Color Yellow  = new(181, 137, 0);
    public static readonly Color Orange  = new(203, 75, 22);
    public static readonly Color Red     = new(220, 50, 47);
    public static readonly Color Magenta = new(211, 54, 130);
    public static readonly Color Violet  = new(108, 113, 196);
    public static readonly Color Blue    = new(38, 139, 210);
    public static readonly Color Cyan    = new(42, 161, 152);
    public static readonly Color Green   = new(133, 153, 0);

    // Common attributes
    public static readonly GuiAttribute Normal       = new(Base0, Base03);
    public static readonly GuiAttribute Emphasis      = new(Base1, Base03);
    public static readonly GuiAttribute Dimmed        = new(Base01, Base03);
    public static readonly GuiAttribute Heading       = new(Yellow, Base03);
    public static readonly GuiAttribute NowLine       = new(Red, Base03);
    public static readonly GuiAttribute SunMarker     = new(Yellow, Base03);
    public static readonly GuiAttribute Forecast      = new(Cyan, Base03);
    public static readonly GuiAttribute AllDay        = new(Base1, Base02);
    public static readonly GuiAttribute Cursor        = new(Base3, Base02);
    public static readonly GuiAttribute Unread        = new(Yellow, Base03);

    // Account color rotation for calendar events, email markers, etc.
    // 8 distinct Solarized accents — assigned round-robin per account.
    public static readonly Color[] AccountPalette =
    [
        Blue, Green, Magenta, Cyan, Violet, Orange, Yellow, Red
    ];

    /// <summary>Build a normal event attribute for an account color.</summary>
    public static GuiAttribute EventAttr(Color bg) => new(Base3, bg);

    /// <summary>Build a cursor-highlighted event attribute for an account color.</summary>
    public static GuiAttribute EventCursorAttr(Color bg) => new(Yellow, bg);

    /// <summary>Parse a hex color string (#RRGGBB) into a Terminal.Gui Color.</summary>
    public static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return Blue;
        var r = Convert.ToInt32(hex[..2], 16);
        var g = Convert.ToInt32(hex[2..4], 16);
        var b = Convert.ToInt32(hex[4..6], 16);
        return new Color(r, g, b);
    }
}
