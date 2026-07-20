namespace FlashKit.Presentation;

public static class PathDisplay
{
    /// <summary>
    /// Middle-ellipsizes a path so both the leading directory and the
    /// filename stay visible when it is too long:
    /// "/home/james/dumps/SHINING FORCE 2 (U).bin" -> "/home/…/SHINING FORCE 2 (U).bin".
    /// Weighted toward the end so the filename survives.
    /// </summary>
    public static string Ellipsize(string path, int max)
    {
        if (max < 4 || path.Length <= max) return path;
        int keepEnd = (max - 1) * 2 / 3;
        int keepStart = max - 1 - keepEnd;
        return string.Concat(path.AsSpan(0, keepStart), "…", path.AsSpan(path.Length - keepEnd));
    }
}
