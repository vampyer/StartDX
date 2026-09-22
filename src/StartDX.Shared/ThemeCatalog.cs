namespace StartDX.Shared;

/// <summary>
/// Describes a theme to *both* processes. The dock maps <see cref="Id"/> to a ResourceDictionary;
/// the settings app only needs the metadata to render a picker. Keeping this list in the shared
/// assembly means a theme can never be selected in Settings that the dock does not know about.
/// </summary>
public sealed record ThemeDescriptor(
    string Id,
    string DisplayName,
    string Description,
    string AccentHex,
    string PreviewFromHex,
    string PreviewToHex);

public static class ThemeCatalog
{
    public const string Glassmorphism = "Glassmorphism";
    public const string PremiumDark = "PremiumDark";
    public const string RetroUpgrade = "RetroUpgrade";
    public const string NeonGreen = "NeonGreen";

    public static IReadOnlyList<ThemeDescriptor> BuiltIn { get; } =
    [
        new(Glassmorphism, "Glassmorphism",
            "Acrylic blur, translucent panels, soft shadows and glowing accent borders.",
            "#5AC8FF", "#3A5F8F", "#0B1220"),
        new(PremiumDark, "Premium Dark",
            "Obsidian surfaces, hairline borders and crisp neon accents.",
            "#00E5FF", "#1C1E25", "#0B0C0F"),
        new(RetroUpgrade, "Retro Upgrade",
            "Classic beveled chrome and solid fills, rendered crisp with smooth motion.",
            "#000080", "#D4D0C8", "#808080"),
        new(NeonGreen, "Neon Green",
            "Pure black surfaces with an electric neon-green glow.",
            "#39FF14", "#0E2A0A", "#000000"),
    ];

    public static string Default => Glassmorphism;

    public static bool IsKnown(string? id) =>
        id is not null && BuiltIn.Any(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    public static string Normalize(string? id) => IsKnown(id) ? id! : Default;
}
