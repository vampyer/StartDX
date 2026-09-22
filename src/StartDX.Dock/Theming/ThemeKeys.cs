namespace StartDX.Dock.Theming;

/// <summary>
/// The *code-visible* half of the theme contract. XAML refers to every token by its string key
/// (see Themes/Theme.Contract.xaml for the full list with defaults); only the tokens that C# must read
/// - backdrop and motion parameters - are named here.
/// </summary>
public static class ThemeKeys
{
    // Backdrop (applied to the HWND through DWM / window-composition APIs, not by WPF)
    public const string BackdropKind = "Theme.Backdrop.Kind";           // string: None | Blur | Acrylic | Mica
    public const string BackdropTint = "Theme.Backdrop.Tint";           // Color : ARGB tint layered on the blur
    public const string BackdropDark = "Theme.Backdrop.Dark";           // bool  : immersive dark title/backdrop
    public const string BackdropCorners = "Theme.Backdrop.Corners";     // string: Round | RoundSmall | Square (flyouts)
    public const string BackdropBorderColor = "Theme.Backdrop.BorderColor"; // Color : DWM window border (Transparent = none)

    // Motion (read at animation time so a theme switch takes effect on the very next hover / open)
    public const string HoverScale = "Theme.Motion.HoverScale";         // double
    public const string HoverMs = "Theme.Motion.HoverMs";               // double
    public const string OpenMs = "Theme.Motion.OpenMs";                 // double
    public const string SlidePx = "Theme.Motion.SlidePx";               // double

    // Tiles
    public const string TileSize = "Theme.Tile.Size";                   // double, DIPs (default 128)
}
