using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using FlashKit.Presentation;

namespace FlashKit.Tui;

/// <summary>The shared <see cref="StatusPalette"/> as Terminal.Gui colors,
/// plus the tint helper the status dots and transaction cards use.</summary>
internal static class IndicatorColors
{
    public static readonly Color Success = Parse(StatusPalette.Success);
    public static readonly Color Neutral = Parse(StatusPalette.Neutral);
    public static readonly Color Running = Parse(StatusPalette.Running);
    public static readonly Color Failure = Parse(StatusPalette.Failure);

    static Color Parse(string hex) => new(
        Convert.ToInt32(hex.Substring(1, 2), 16),
        Convert.ToInt32(hex.Substring(3, 2), 16),
        Convert.ToInt32(hex.Substring(5, 2), 16));

    /// <summary>Recolors the view's foreground, keeping its inherited
    /// background.</summary>
    public static void Tint(View view, Color color)
    {
        var scheme = view.GetScheme();
        view.SetScheme(scheme with
        {
            Normal = new Terminal.Gui.Drawing.Attribute(color, scheme.Normal.Background),
        });
    }
}
