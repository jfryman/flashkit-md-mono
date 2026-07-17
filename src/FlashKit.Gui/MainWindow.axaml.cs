using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FlashKit.Core;

namespace FlashKit.Gui;

/// <summary>
/// Main window: a status bar tracking programmer + cartridge presence, a
/// structured cart-info panel, and a per-operation transaction log with
/// inline progress bars. Cart state auto-refreshes via a 2 s poll in
/// production (tests use the connector ctor, which starts no timer, and
/// drive <see cref="RefreshAsync"/> directly — no timing in CI). The serial
/// port is exclusive, so the poller and operations serialize on
/// <see cref="deviceGate"/>; operations run on a worker thread.
/// </summary>
public partial class MainWindow : Window
{
    static readonly FilePickerFileType RomFiles = new("ROM image") { Patterns = new[] { "*.bin" } };
    static readonly FilePickerFileType SaveFiles = new("Save RAM") { Patterns = new[] { "*.srm" } };
    static readonly IBrush PresentBrush = Brush.Parse("#3FB950");
    static readonly IBrush AbsentBrush = Brush.Parse("#8B949E");

    readonly DeviceConnector? connector;
    readonly SemaphoreSlim deviceGate = new(1, 1);

    internal ObservableCollection<TransactionEntry> Log { get; } = new();

    // Test seams: headless tests inject a fake-device connector and replace
    // the pickers (the headless platform's StorageProvider never returns a
    // file). Production code uses the defaults.
    internal Func<string, FilePickerFileType, Task<string?>> PickSavePath;
    internal Func<FilePickerFileType, Task<string?>> PickOpenPath;

    public MainWindow() : this(null)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => _ = RefreshAsync();
        Opened += (_, _) => { _ = RefreshAsync(); timer.Start(); };
        Closed += (_, _) => timer.Stop();
    }

    internal MainWindow(DeviceConnector? connector)
    {
        this.connector = connector;
        InitializeComponent();
        Title += " " + VersionInfo.ClientVersion;
        LogList.ItemsSource = Log;
        PickSavePath = DefaultPickSavePath;
        PickOpenPath = DefaultPickOpenPath;
    }

    async Task<string?> DefaultPickSavePath(string suggestedName, FilePickerFileType type)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[] { type, FilePickerFileTypes.All },
        });
        return file?.TryGetLocalPath();
    }

    async Task<string?> DefaultPickOpenPath(FilePickerFileType type)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            FileTypeFilter = new[] { type, FilePickerFileTypes.All },
        });
        return files.Count == 1 ? files[0].TryGetLocalPath() : null;
    }

    void OnRefresh(object? sender, RoutedEventArgs e) => _ = RefreshAsync();
    void OnReadRom(object? sender, RoutedEventArgs e) => _ = ReadRomAsync();
    void OnWriteRom(object? sender, RoutedEventArgs e) => _ = WriteRomAsync();
    void OnReadRam(object? sender, RoutedEventArgs e) => _ = ReadRamAsync();
    void OnWriteRam(object? sender, RoutedEventArgs e) => _ = WriteRamAsync();

    /// <summary>One poll: connect, read cart info, disconnect, update the
    /// status bar and info panel. Skips silently when an operation (or a
    /// previous poll) holds the device.</summary>
    internal async Task RefreshAsync()
    {
        if (!await deviceGate.WaitAsync(0)) return;
        try
        {
            var (port, info) = await Task.Run(() =>
            {
                using var session = FlashKitSession.Connect(connector);
                return (session.PortName, session.GetInfo());
            });
            ShowDeviceState(port);
            ShowCartState(info);
        }
        catch (DeviceNotFoundException)
        {
            ShowDeviceState(null);
            ShowCartState(null);
        }
        catch (Exception)
        {
            // Transient failure mid-poll (e.g. cable pulled between the
            // handshake and the info read): keep the last device state but
            // stop claiming to know what cart is in.
            ShowCartState(null);
        }
        finally
        {
            deviceGate.Release();
        }
    }

    void ShowDeviceState(string? port)
    {
        DeviceDot.Fill = port != null ? PresentBrush : AbsentBrush;
        DeviceStatusText.Text = port != null
            ? "Programmer connected on " + port
            : "No programmer detected";
    }

    void ShowCartState(CartInfo? info)
    {
        bool present = info?.CartDetected == true;
        CartDot.Fill = present ? PresentBrush : AbsentBrush;
        CartStatusText.Text = present ? DisplayName(info!.RomName)
            : info != null ? "No cartridge" : "—";
        InfoName.Text = present ? DisplayName(info!.RomName) : "—";
        InfoRomSize.Text = present ? FormatKb(info!.RomBytes) : "—";
        InfoRamSize.Text = present ? FormatSize(info!.RamBytes) : "—";
        InfoHeaderSize.Text = present && info!.HeaderRomBytes is int hdr ? FormatKb(hdr) : "—";
    }

    // Header names are space-padded ("SONIC THE          HEDGEHOG 3");
    // collapse the runs for display, like GetRomName does for filenames.
    static string DisplayName(string romName) =>
        string.Join(' ', romName.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    static string FormatKb(int bytes) => bytes / 1024 + "K";
    static string FormatSize(int bytes) => bytes < 1024 ? bytes + "B" : bytes / 1024 + "K";
    static string Md5(byte[] buff) => BitConverter.ToString(MD5.HashData(buff));

    void SetBusy(bool busy)
    {
        foreach (var btn in new[] { BtnReadRom, BtnWriteRom, BtnReadRam, BtnWriteRam, BtnRefresh })
            btn.IsEnabled = !busy;
    }

    /// <summary>Runs one cart operation as a transaction-log entry: connect,
    /// run <paramref name="body"/>, report any failure on the entry, always
    /// disconnect — the original's per-click try/catch/disconnect shape.
    /// Waits out an in-flight poll before touching the port; Connect and
    /// Dispose run off the UI thread (Dispose can stall in the guarded
    /// serial close).</summary>
    async Task RunOperation(string title, Func<FlashKitSession, TransactionEntry, Task> body)
    {
        var entry = new TransactionEntry(title);
        Log.Insert(0, entry);
        SetBusy(true);
        await deviceGate.WaitAsync();
        FlashKitSession? session = null;
        try
        {
            entry.Status = "Connecting...";
            session = await Task.Run(() => FlashKitSession.Connect(connector));
            await body(session, entry);
        }
        catch (Exception x)
        {
            entry.Fail(x.Message);
        }
        finally
        {
            if (session != null) await Task.Run(session.Dispose);
            deviceGate.Release();
            SetBusy(false);
        }
    }

    /// <summary>Progress sink that drives the entry's progress bar and sets
    /// its status when the operation enters a new phase. Constructed on the
    /// UI thread so reports from the worker marshal back automatically.</summary>
    static IProgress<OperationProgress> TrackProgress(TransactionEntry entry,
        Func<OperationPhase, string?>? phaseLabel = null)
    {
        OperationPhase? current = null;
        return new Progress<OperationProgress>(p =>
        {
            if (current != p.Phase)
            {
                current = p.Phase;
                if (phaseLabel?.Invoke(p.Phase) is string label) entry.Status = label;
            }
            entry.ProgressMax = Math.Max(1, p.Total);
            entry.ProgressValue = p.Done;
        });
    }

    internal Task ReadRomAsync() => RunOperation("Read ROM", async (session, entry) =>
    {
        var romName = await Task.Run(session.GetRomName);
        if (await PickSavePath(romName + ".bin", RomFiles) is not string path)
        {
            entry.Cancel();
            return;
        }
        entry.Detail = path;
        entry.Status = "Reading ROM...";
        var progress = TrackProgress(entry);
        var rom = await Task.Run(() => session.ReadRom(p => progress.Report(p)));
        await File.WriteAllBytesAsync(path, rom);
        entry.Succeed($"OK — {rom.Length / 1024}K, MD5 {Md5(rom)}");
    });

    internal Task WriteRomAsync() => RunOperation("Write ROM", async (session, entry) =>
    {
        if (await PickOpenPath(RomFiles) is not string path)
        {
            entry.Cancel();
            return;
        }
        entry.Detail = path;
        var rom = await File.ReadAllBytesAsync(path);
        var progress = TrackProgress(entry, phase => phase switch
        {
            OperationPhase.Erase => "Flash erase...",
            OperationPhase.Write => "Flash write...",
            OperationPhase.Verify => "Flash verify...",
            _ => null,
        });
        await Task.Run(() => session.WriteRom(rom, progress: p => progress.Report(p)));
        entry.Succeed($"OK — {rom.Length / 1024}K written, MD5 {Md5(rom)}");
    });

    internal Task ReadRamAsync() => RunOperation("Read RAM", async (session, entry) =>
    {
        var romName = await Task.Run(session.GetRomName);
        if (await PickSavePath(romName + ".srm", SaveFiles) is not string path)
        {
            entry.Cancel();
            return;
        }
        entry.Detail = path;
        entry.Status = "Reading RAM...";
        var ram = await Task.Run(session.ReadRam);
        await File.WriteAllBytesAsync(path, ram);
        entry.Succeed($"OK — {FormatSize(ram.Length / 2)}, MD5 {Md5(ram)}");
    });

    internal Task WriteRamAsync() => RunOperation("Write RAM", async (session, entry) =>
    {
        if (await PickOpenPath(SaveFiles) is not string path)
        {
            entry.Cancel();
            return;
        }
        entry.Detail = path;
        var ram = await File.ReadAllBytesAsync(path);
        var progress = TrackProgress(entry, phase => phase switch
        {
            OperationPhase.Write => "Write RAM...",
            OperationPhase.Verify => "Verify...",
            _ => null,
        });
        int words = await Task.Run(() => session.WriteRam(ram, p => progress.Report(p)));
        entry.Succeed($"OK — {words} words sent, MD5 {Md5(ram)}");
    });
}
