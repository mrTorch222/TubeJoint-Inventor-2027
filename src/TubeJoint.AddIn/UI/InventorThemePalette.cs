using System.Drawing;

namespace TubeJoint.AddIn.UI;

/// <summary>
/// WinForms palette derived from Inventor's current browser theme. Inventor
/// exposes the base background/text colors; related control shades are derived
/// locally so the add-in remains readable in dark and light themes.
/// </summary>
internal sealed record InventorThemePalette(
    Color PanelBack,
    Color SectionBack,
    Color EditorBack,
    Color Border,
    Color TextColor,
    Color MutedText,
    Color Accent)
{
    private static readonly InventorThemePalette Fallback = Create(
        Color.FromArgb(47, 57, 71),
        Color.FromArgb(235, 239, 244),
        Color.FromArgb(27, 159, 202));

    public static InventorThemePalette Current { get; private set; } = Fallback;

    public static void Refresh(Inventor.Application application)
    {
        try
        {
            var manager = application.ThemeManager;
            var background = DrawingColor(
                manager.GetComponentThemeColor("BrowserPane_BackgroundColor"));
            var text = DrawingColor(
                manager.GetComponentThemeColor("BrowserPane_TextColor"));
            // ColorScheme.HighlightColor is a geometry-selection color, not the
            // blue Property Panel action accent.
            Current = Create(background, text, Fallback.Accent);
        }
        catch
        {
            Current = Fallback;
        }
    }

    private static InventorThemePalette Create(Color background, Color text, Color accent)
    {
        var dark = Luminance(background) < 145;
        return new InventorThemePalette(
            background,
            Mix(background, dark ? Color.White : Color.Black, 0.09),
            Mix(background, dark ? Color.Black : Color.White, 0.20),
            Mix(background, dark ? Color.White : Color.Black, 0.22),
            text,
            Mix(text, background, 0.28),
            accent);
    }

    private static Color DrawingColor(Inventor.Color color) =>
        Color.FromArgb(color.Red, color.Green, color.Blue);

    private static double Luminance(Color color) =>
        color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;

    private static Color Mix(Color first, Color second, double amount)
    {
        var remaining = 1.0 - amount;
        return Color.FromArgb(
            (int)Math.Round(first.R * remaining + second.R * amount),
            (int)Math.Round(first.G * remaining + second.G * amount),
            (int)Math.Round(first.B * remaining + second.B * amount));
    }
}
