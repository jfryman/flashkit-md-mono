using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using FlashKit.Presentation;

namespace FlashKit.Tui;

/// <summary>
/// One transaction as a bordered card, mirroring the GUI's log entries:
/// time + title in the border, then a colored outcome bubble (▶ amber
/// running, ✔ green ok, ✖ red failed, ○ gray cancelled) beside the file
/// path, the entry's own progress bar, and the full status line (result
/// size and MD5, or the error — red on failure, like the GUI).
/// </summary>
internal sealed class TransactionCard : FrameView
{
    public const int CardHeight = 5; // 3 content rows + border

    public TransactionEntry Entry { get; }

    internal readonly Label Bubble = new() { Text = "▶", X = 0, Y = 0 };
    internal readonly Label DetailLabel = new() { Text = "", X = 2, Y = 0, Width = Dim.Fill() };
    internal readonly ProgressBar Progress = new() { X = 0, Y = 1, Width = Dim.Fill() };
    internal readonly Label StatusLabel = new() { Text = "", X = 0, Y = 2, Width = Dim.Fill() };

    public TransactionCard(TransactionEntry entry)
    {
        Entry = entry;
        Width = Dim.Fill();
        Height = CardHeight;
        CanFocus = false;
        Add(Bubble, DetailLabel, Progress, StatusLabel);
        entry.PropertyChanged += (_, _) => Sync();
        Sync();
    }

    void Sync()
    {
        Title = $"{Entry.Time} {Entry.Title}";
        Bubble.Text = Entry.Running ? "▶" : Entry.Failed ? "✖" : Entry.Succeeded ? "✔" : "○";
        IndicatorColors.Tint(Bubble,
            Entry.Running ? IndicatorColors.Running
            : Entry.Failed ? IndicatorColors.Failure
            : Entry.Succeeded ? IndicatorColors.Success
            : IndicatorColors.Neutral);
        DetailLabel.Text = Entry.Detail;
        Progress.Fraction = (float)(Entry.ProgressValue / Math.Max(1, Entry.ProgressMax));
        StatusLabel.Text = Entry.Status;
        if (Entry.Failed) IndicatorColors.Tint(StatusLabel, IndicatorColors.Failure);
    }
}
