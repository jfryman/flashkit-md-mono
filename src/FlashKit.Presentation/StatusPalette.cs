namespace FlashKit.Presentation;

/// <summary>
/// The status indicator colors shared by the interactive front-ends
/// (hex RGB): the GUI's bubbles/dots and the TUI's glyphs use the same
/// values, so state reads identically in both.
/// </summary>
public static class StatusPalette
{
    public const string Success = "#3FB950"; // green: present / succeeded
    public const string Neutral = "#8B949E"; // gray: absent / cancelled
    public const string Running = "#D29922"; // amber: operation in flight
    public const string Failure = "#D9534F"; // red: failed
}
