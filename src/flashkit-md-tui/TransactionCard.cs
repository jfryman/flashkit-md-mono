using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using FlashKit.Presentation;

namespace FlashKit.Tui;

/// <summary>
/// One transaction as a bordered card, mirroring the GUI's log entries:
/// time + title in the border, then a colored outcome bubble (▶ amber
/// running, ✔ green ok, ✖ red failed, ○ gray cancelled) beside the file
/// path, the entry's own progress bar, and the full status line (result
/// size and MD5/CRC32/SHA-1, or the error — red on failure, like the GUI).
///
/// The card is only as tall as its status needs: one line for a running or
/// failed entry, four for a completed dump/write whose status carries the
/// hash lines. The host repositions cards when a height changes.
/// </summary>
internal sealed class TransactionCard : FrameView
{
    const int PathWidth = 64;

    public TransactionEntry Entry { get; }

    internal readonly Label Bubble = new() { Text = "▶", X = 0, Y = 0 };
    internal readonly Label DetailLabel = new() { Text = "", X = 2, Y = 0, Width = Dim.Fill() };
    internal readonly ProgressBar Progress = new() { X = 0, Y = 1, Width = Dim.Fill() };
    internal readonly Label StatusLabel = new() { Text = "", X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill() };

    public TransactionCard(TransactionEntry entry)
    {
        Entry = entry;
        Width = Dim.Fill();
        CanFocus = false;
        Add(Bubble, DetailLabel, Progress, StatusLabel);
        entry.PropertyChanged += (_, _) => Sync();
        Sync();
    }

    /// <summary>Border (2) + detail + progress + however many status lines.</summary>
    public int DesiredHeight => 4 + Math.Max(1, StatusLineCount);

    int StatusLineCount => Entry.Status.Length == 0 ? 1 : Entry.Status.Split('\n').Length;

    void Sync()
    {
        string icon = Entry.Running ? "▶" : Entry.Failed ? "✖" : Entry.Succeeded ? "✔" : "○";
        Title = $"{Entry.Time} {Entry.Title}";
        Bubble.Text = icon;
        IndicatorColors.Tint(Bubble,
            Entry.Running ? IndicatorColors.Running
            : Entry.Failed ? IndicatorColors.Failure
            : Entry.Succeeded ? IndicatorColors.Success
            : IndicatorColors.Neutral);
        DetailLabel.Text = PathDisplay.Ellipsize(Entry.Detail, PathWidth);
        Progress.Fraction = (float)(Entry.ProgressValue / Math.Max(1, Entry.ProgressMax));
        StatusLabel.Text = Entry.Status;
        if (Entry.Failed) IndicatorColors.Tint(StatusLabel, IndicatorColors.Failure);
        Height = DesiredHeight;
    }
}
