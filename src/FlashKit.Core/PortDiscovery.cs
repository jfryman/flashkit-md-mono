namespace FlashKit.Core;

public enum HostOs { Linux, MacOs, Windows, Other }

/// <summary>
/// Narrows raw serial port names down to plausible FlashKit candidates.
/// The original client probed every port on the system; on Linux that means
/// opening dozens of phantom /dev/ttyS* stubs, and on macOS the /dev/tty.*
/// variants can block on open (the /dev/cu.* call-up devices must be used).
/// </summary>
public static class PortDiscovery
{
    public static HostOs CurrentOs()
    {
        if (OperatingSystem.IsLinux()) return HostOs.Linux;
        if (OperatingSystem.IsMacOS()) return HostOs.MacOs;
        if (OperatingSystem.IsWindows()) return HostOs.Windows;
        return HostOs.Other;
    }

    public static string[] FilterCandidates(IEnumerable<string> portNames, HostOs os)
    {
        Func<string, bool> match = os switch
        {
            HostOs.Linux => name =>
                name.StartsWith("/dev/ttyACM", StringComparison.Ordinal) ||
                name.StartsWith("/dev/ttyUSB", StringComparison.Ordinal),
            HostOs.MacOs => name =>
                name.StartsWith("/dev/cu.usbmodem", StringComparison.Ordinal) ||
                name.StartsWith("/dev/cu.usbserial", StringComparison.Ordinal),
            HostOs.Windows => name =>
                name.StartsWith("COM", StringComparison.OrdinalIgnoreCase),
            _ => _ => true,
        };
        return portNames.Where(match).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }
}
