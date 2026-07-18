using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
///
/// One session is held for as long as the programmer stays reachable,
/// NOT one per operation like the original client: on macOS, closing the
/// port after a multi-MB flash write wedges in tcdrain and the guarded
/// close abandons it (see SystemSerialPort). In this long-lived process an
/// abandoned close keeps the descriptor held, making the port unopenable
/// until the adapter is replugged — so the close is deferred to window
/// close / device loss, where it is harmless.
/// </summary>
public partial class MainWindow : Window
{
    static readonly FilePickerFileType RomFiles = new("ROM image") { Patterns = new[] { "*.bin" } };
    static readonly FilePickerFileType SaveFiles = new("Save RAM") { Patterns = new[] { "*.srm" } };
    static readonly IBrush PresentBrush = Brush.Parse("#3FB950");
    static readonly IBrush AbsentBrush = Brush.Parse("#8B949E");

    readonly DeviceConnector? connector;
    readonly SemaphoreSlim deviceGate = new(1, 1);
    FlashKitSession? session;

    // Auto actions fire on cart insertion (the poll transition from no cart
    // to cart). Deliberately NOT keyed on cart identity: auto-write changes
    // the cart's contents, so identity-keying would see the just-written
    // cart as new and re-write it in a loop. cartProcessed clears on
    // removal, device loss, or any auto-setting change, so the seated cart
    // then counts as newly inserted; a failed action is not retried every
    // poll. Auto-dump and auto-write are mutually exclusive in the UI.
    string? autoDumpFolder;
    string? autoWriteFile;
    bool cartProcessed;

    internal ObservableCollection<TransactionEntry> Log { get; } = new();

    // Test seams: headless tests inject a fake-device connector and replace
    // the pickers (the headless platform's StorageProvider never returns a
    // file). Production code uses the defaults.
    internal Func<string, FilePickerFileType, Task<string?>> PickSavePath;
    internal Func<FilePickerFileType, Task<string?>> PickOpenPath;
    internal Func<Task<string?>> PickFolder;
    internal Func<Task<bool>> ConfirmAutoWrite;

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
        PickFolder = DefaultPickFolder;
        ConfirmAutoWrite = DefaultConfirmAutoWrite;
        Closed += (_, _) => DisposeSession();
    }

    FlashKitSession EnsureSession() => session ??= FlashKitSession.Connect(connector);

    void DisposeSession()
    {
        var s = session;
        session = null;
        try { s?.Dispose(); }
        catch (Exception) { }
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

    async Task<string?> DefaultPickFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Auto-dump folder",
        });
        return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>Checking a box re-arms the auto actions (so the currently
    /// seated cart counts as newly inserted on the next poll) and prompts
    /// for a folder if none is chosen yet — cancelling the prompt unchecks
    /// the box again.</summary>
    async void OnAutoDumpToggled(object? sender, RoutedEventArgs e)
    {
        cartProcessed = false;
        UpdateAutoExclusivity();
        if (sender is CheckBox { IsChecked: true } box && autoDumpFolder == null)
        {
            autoDumpFolder = await PickFolder();
            AutoDumpFolderText.Text = autoDumpFolder ?? "No folder chosen";
            if (autoDumpFolder == null) box.IsChecked = false;
        }
    }

    async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (await PickFolder() is not string folder) return;
        autoDumpFolder = folder;
        AutoDumpFolderText.Text = folder;
        cartProcessed = false;
    }

    /// <summary>Checking "Write ROM" first requires the destructive-action
    /// warning to be acknowledged (unless suppressed), then a ROM file if
    /// none is chosen — declining either unchecks the box.</summary>
    async void OnAutoWriteToggled(object? sender, RoutedEventArgs e)
    {
        cartProcessed = false;
        UpdateAutoExclusivity();
        if (sender is not CheckBox { IsChecked: true } box) return;
        if (!await ConfirmAutoWrite())
        {
            box.IsChecked = false;
            return;
        }
        if (autoWriteFile == null)
        {
            autoWriteFile = await PickOpenPath(RomFiles);
            AutoWriteFileText.Text = autoWriteFile is string f ? Path.GetFileName(f) : "No file chosen";
            if (autoWriteFile == null) box.IsChecked = false;
        }
    }

    async void OnChooseWriteFile(object? sender, RoutedEventArgs e)
    {
        if (await PickOpenPath(RomFiles) is not string file) return;
        autoWriteFile = file;
        AutoWriteFileText.Text = Path.GetFileName(file);
        cartProcessed = false;
    }

    /// <summary>Auto-dump and auto-write must not run together (dumping a
    /// cart that is about to be erased, or writing one being archived, is
    /// never what the user meant): whichever side is checked disables the
    /// other side's checkboxes.</summary>
    void UpdateAutoExclusivity()
    {
        bool dumpOn = ChkAutoRom.IsChecked == true || ChkAutoRam.IsChecked == true;
        bool writeOn = ChkAutoWrite.IsChecked == true;
        ChkAutoWrite.IsEnabled = !dumpOn;
        ChkAutoRom.IsEnabled = !writeOn;
        ChkAutoRam.IsEnabled = !writeOn;
    }

    bool AutoDumpEnabled =>
        autoDumpFolder != null && (ChkAutoRom.IsChecked == true || ChkAutoRam.IsChecked == true);

    bool AutoWriteEnabled => autoWriteFile != null && ChkAutoWrite.IsChecked == true;

    static string SuppressAutoWriteWarningPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "flashkit-md", "suppress-auto-write-warning");

    async Task<bool> DefaultConfirmAutoWrite()
    {
        if (File.Exists(SuppressAutoWriteWarningPath)) return true;

        var dontShow = new CheckBox { Content = "Don't show this warning again" };
        var enable = new Button { Content = "Enable auto-write" };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dialog = new Window
        {
            Title = "Enable auto-write?",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = "Auto-write ERASES and reprograms every flash cartridge "
                             + "inserted while it is enabled, without asking again.\n\n"
                             + "Cartridges without a writable flash chip (retail game "
                             + "carts) are detected and skipped, but the contents of any "
                             + "flash cart you insert will be destroyed and replaced "
                             + "with the chosen file.",
                    },
                    dontShow,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, enable },
                    },
                },
            },
        };
        enable.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        bool accepted = await dialog.ShowDialog<bool>(this);
        if (accepted && dontShow.IsChecked == true)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SuppressAutoWriteWarningPath)!);
                File.WriteAllText(SuppressAutoWriteWarningPath, "");
            }
            catch (Exception) { }
        }
        return accepted;
    }

    void OnRefresh(object? sender, RoutedEventArgs e) => _ = RefreshAsync();
    void OnReadRom(object? sender, RoutedEventArgs e) => _ = ReadRomAsync();
    void OnWriteRom(object? sender, RoutedEventArgs e) => _ = WriteRomAsync();
    void OnReadRam(object? sender, RoutedEventArgs e) => _ = ReadRamAsync();
    void OnWriteRam(object? sender, RoutedEventArgs e) => _ = WriteRamAsync();

    /// <summary>One poll: connect if needed, read cart info on the held
    /// session, update the status bar and info panel. Skips silently when an
    /// operation (or a previous poll) holds the device.</summary>
    internal async Task RefreshAsync()
    {
        if (!await deviceGate.WaitAsync(0)) return;
        CartInfo? insertedCart = null;
        try
        {
            var (port, info) = await Task.Run(() =>
            {
                var s = EnsureSession();
                return (s.PortName, s.GetInfo());
            });
            ShowDeviceState(port);
            ShowCartState(info);
            if (!info.CartDetected)
            {
                cartProcessed = false;
            }
            else if (!cartProcessed)
            {
                cartProcessed = true;
                if (AutoDumpEnabled || AutoWriteEnabled) insertedCart = info;
            }
        }
        catch (DeviceNotFoundException)
        {
            ShowDeviceState(null);
            ShowCartState(null);
            cartProcessed = false;
        }
        catch (Exception)
        {
            // The held session died mid-poll (cable pulled): drop it and
            // report the device gone; the next poll reconnects if it's back.
            await Task.Run(DisposeSession);
            ShowDeviceState(null);
            ShowCartState(null);
            cartProcessed = false;
        }
        finally
        {
            deviceGate.Release();
        }
        // Outside the gate: the operations take it themselves via RunOperation.
        if (insertedCart != null) await AutoProcessAsync(insertedCart);
    }

    async Task AutoProcessAsync(CartInfo info)
    {
        if (AutoDumpEnabled && autoDumpFolder is string folder)
        {
            string name = DisplayName(info.RomName);
            if (ChkAutoRom.IsChecked == true)
                await RunOperation("Auto-dump ROM", (s, entry) => DumpRomTo(s, entry, UniquePath(folder, name + ".bin")));
            if (ChkAutoRam.IsChecked == true && info.RamBytes > 0)
                await RunOperation("Auto-dump RAM", (s, entry) => DumpRamTo(s, entry, UniquePath(folder, name + ".srm")));
        }
        if (AutoWriteEnabled && autoWriteFile is string file)
        {
            await RunOperation("Auto-write ROM", (s, entry) => WriteRomFrom(s, entry, file));
        }
    }

    /// <summary>Never overwrite an earlier dump: append " (2)", " (3)", …
    /// until the name is free.</summary>
    static string UniquePath(string folder, string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        string path = Path.Combine(folder, fileName);
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{stem} ({n}){ext}");
        return path;
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
        CartStatusText.Text = present ? "Cartridge inserted"
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

    /// <summary>Runs one cart operation as a transaction-log entry on the
    /// held session (connecting first if needed) and reports any failure on
    /// the entry. The session stays open afterwards — no per-operation close
    /// (see the class comment) — unless the failure looks like a dead serial
    /// link, in which case it is dropped for the poller to re-establish.
    /// Waits out an in-flight poll before touching the port; connecting and
    /// disposing run off the UI thread (a dispose can stall in the guarded
    /// serial close).</summary>
    async Task RunOperation(string title, Func<FlashKitSession, TransactionEntry, Task> body)
    {
        var entry = new TransactionEntry(title);
        Log.Insert(0, entry);
        SetBusy(true);
        await deviceGate.WaitAsync();
        try
        {
            entry.Status = "Connecting...";
            var s = await Task.Run(EnsureSession);
            await body(s, entry);
        }
        catch (Exception x)
        {
            entry.Fail(x.Message);
            if (x is IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException)
                await Task.Run(DisposeSession);
        }
        finally
        {
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
        await DumpRomTo(session, entry, path);
    });

    async Task DumpRomTo(FlashKitSession session, TransactionEntry entry, string path)
    {
        entry.Detail = path;
        entry.Status = "Reading ROM...";
        var progress = TrackProgress(entry);
        var rom = await Task.Run(() => session.ReadRom(p => progress.Report(p)));
        await File.WriteAllBytesAsync(path, rom);
        entry.Succeed($"OK — {rom.Length / 1024}K, MD5 {Md5(rom)}");
    }

    internal Task WriteRomAsync() => RunOperation("Write ROM", async (session, entry) =>
    {
        if (await PickOpenPath(RomFiles) is not string path)
        {
            entry.Cancel();
            return;
        }
        await WriteRomFrom(session, entry, path);
    });

    async Task WriteRomFrom(FlashKitSession session, TransactionEntry entry, string path)
    {
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
    }

    internal Task ReadRamAsync() => RunOperation("Read RAM", async (session, entry) =>
    {
        var romName = await Task.Run(session.GetRomName);
        if (await PickSavePath(romName + ".srm", SaveFiles) is not string path)
        {
            entry.Cancel();
            return;
        }
        await DumpRamTo(session, entry, path);
    });

    async Task DumpRamTo(FlashKitSession session, TransactionEntry entry, string path)
    {
        entry.Detail = path;
        entry.Status = "Reading RAM...";
        var ram = await Task.Run(session.ReadRam);
        await File.WriteAllBytesAsync(path, ram);
        entry.Succeed($"OK — {FormatSize(ram.Length / 2)}, MD5 {Md5(ram)}");
    }

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
