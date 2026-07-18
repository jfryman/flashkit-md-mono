using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Media;
using Avalonia.VisualTree;
using FlashKit.Core;
using FlashKit.Core.Tests;
using FlashKit.Gui;

namespace FlashKit.Gui.Tests;

/// <summary>
/// Headless end-to-end tests for the adapter layer the GUI adds on top of
/// FlashKitSession: status bar + info panel refresh, transaction logging,
/// busy state, and the error path. Cart behavior itself is covered by
/// FlashKit.Core.Tests; the fake device stands in for the programmer, temp
/// files for the pickers (CliTests pattern). Auto-refresh is driven by
/// calling RefreshAsync directly — the poll timer only exists in the
/// production constructor, so no timing here.
/// </summary>
public class MainWindowTests : IDisposable
{
    readonly string dir;

    public MainWindowTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "flashkit-gui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    public void Dispose() => Directory.Delete(dir, recursive: true);

    string TempFile(string name) => Path.Combine(dir, name);

    static MainWindow Window(FakeFlashKitDevice fake)
    {
        var window = new MainWindow(new DeviceConnector(
            () => new[] { "/dev/ttyUSB0" }, _ => fake, HostOs.Linux));
        window.Show();
        return window;
    }

    static string Text(MainWindow window, string name) =>
        window.FindControl<TextBlock>(name)!.Text ?? "";

    static Button Btn(MainWindow window, string name) =>
        window.FindControl<Button>(name)!;

    [AvaloniaFact]
    public async Task refresh_shows_device_and_cart_info()
    {
        var window = Window(new FakeFlashKitDevice(TestRoms.MakeRom(0x80000), sramBytes: 8192));

        await window.RefreshAsync();

        Assert.Equal("Programmer connected on FAKE", Text(window, "DeviceStatusText"));
        Assert.Equal("Cartridge inserted", Text(window, "CartStatusText"));
        Assert.Equal("TEST GAME (U)", Text(window, "InfoName"));
        Assert.Equal("512K", Text(window, "InfoRomSize"));
        Assert.Equal("8K", Text(window, "InfoRamSize"));
        Assert.Equal("512K", Text(window, "InfoHeaderSize"));
    }

    [AvaloniaFact]
    public async Task refresh_tracks_cartridge_insertion()
    {
        var fake = new FakeFlashKitDevice(TestRoms.MakeRom(0x80000)) { CartInserted = false };
        var window = Window(fake);

        await window.RefreshAsync();
        Assert.Equal("Programmer connected on FAKE", Text(window, "DeviceStatusText"));
        Assert.Equal("No cartridge", Text(window, "CartStatusText"));
        Assert.Equal("—", Text(window, "InfoName"));
        Assert.Equal("—", Text(window, "InfoRomSize"));

        fake.CartInserted = true;
        await window.RefreshAsync();
        Assert.Equal("Cartridge inserted", Text(window, "CartStatusText"));
        Assert.Equal("TEST GAME (U)", Text(window, "InfoName"));
        Assert.Equal("512K", Text(window, "InfoRomSize"));
    }

    [AvaloniaFact]
    public async Task session_is_held_across_polls_and_operations()
    {
        // Regression guard for the macOS FTDI wedge: closing the port after
        // a flash write can hang and leave the descriptor abandoned in this
        // long-lived process, so the GUI must reuse one session instead of
        // reconnecting per poll/operation.
        var fake = new FakeFlashKitDevice(TestRoms.MakeRom(0x80000));
        var window = Window(fake);
        window.PickSavePath = (_, _) => Task.FromResult<string?>(TempFile("dump.bin"));

        await window.RefreshAsync();
        await window.RefreshAsync();
        await window.ReadRomAsync();
        await window.RefreshAsync();

        Assert.Equal(1, fake.OpenCount);
    }

    [AvaloniaFact]
    public async Task unplugged_programmer_is_reported_and_reconnected()
    {
        var fake = new FakeFlashKitDevice(TestRoms.MakeRom(0x80000));
        var window = Window(fake);

        await window.RefreshAsync();
        Assert.Equal("Programmer connected on FAKE", Text(window, "DeviceStatusText"));

        fake.Disconnected = true;
        await window.RefreshAsync();
        Assert.Equal("No programmer detected", Text(window, "DeviceStatusText"));
        Assert.Equal("—", Text(window, "CartStatusText"));

        fake.Disconnected = false;
        await window.RefreshAsync();
        Assert.Equal("Programmer connected on FAKE", Text(window, "DeviceStatusText"));
        Assert.Equal(2, fake.OpenCount);
    }

    [AvaloniaFact]
    public async Task refresh_reports_missing_programmer()
    {
        var window = new MainWindow(new DeviceConnector(
            () => Array.Empty<string>(),
            _ => throw new InvalidOperationException("no ports to open"),
            HostOs.Linux));
        window.Show();

        await window.RefreshAsync();

        Assert.Equal("No programmer detected", Text(window, "DeviceStatusText"));
        Assert.Equal("—", Text(window, "CartStatusText"));
        Assert.Equal("—", Text(window, "InfoName"));
    }

    [AvaloniaFact]
    public async Task read_rom_dumps_cart_and_logs_transaction()
    {
        var rom = TestRoms.MakeRom(0x80000);
        var window = Window(new FakeFlashKitDevice(rom));
        string file = TempFile("dump.bin");
        string? suggested = null;
        window.PickSavePath = (name, _) => { suggested = name; return Task.FromResult<string?>(file); };

        await window.ReadRomAsync();

        Assert.Equal("TEST GAME (U).bin", suggested);
        Assert.Equal(rom, File.ReadAllBytes(file));
        var entry = Assert.Single(window.Log);
        Assert.Equal("Read ROM", entry.Title);
        Assert.Equal(file, entry.Detail);
        Assert.StartsWith("OK — 512K, MD5 ", entry.Status);
        Assert.True(entry.Succeeded);
        Assert.False(entry.Failed);
        Assert.False(entry.Running);
        Assert.Equal(entry.ProgressMax, entry.ProgressValue);

        // Force a layout pass so the log row's DataTemplate instantiates —
        // this is the only place its bindings are exercised (they fail
        // silently at runtime if a property name drifts).
        window.UpdateLayout();
        var list = window.FindControl<ItemsControl>("LogList")!;
        var bar = list.GetVisualDescendants().OfType<ProgressBar>().Single();
        Assert.Equal(entry.ProgressMax, bar.Maximum);
        Assert.Equal(entry.ProgressValue, bar.Value);
        Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == entry.Status);
        var bubble = list.GetVisualDescendants().OfType<Ellipse>().Single();
        Assert.Equal(Color.Parse("#3FB950"), ((ISolidColorBrush)bubble.Fill!).Color);
    }

    [AvaloniaFact]
    public async Task read_ram_dumps_save_and_logs_transaction()
    {
        var window = Window(new FakeFlashKitDevice(TestRoms.MakeRom(0x80000), sramBytes: 8192));
        string file = TempFile("save.srm");
        string? suggested = null;
        window.PickSavePath = (name, _) => { suggested = name; return Task.FromResult<string?>(file); };

        await window.ReadRamAsync();

        Assert.Equal("TEST GAME (U).srm", suggested);
        Assert.Equal(16384, new FileInfo(file).Length);
        var entry = Assert.Single(window.Log);
        Assert.Equal("Read RAM", entry.Title);
        Assert.StartsWith("OK — 8K, MD5 ", entry.Status);
    }

    [AvaloniaFact]
    public async Task cancelled_picker_logs_cancelled_transaction()
    {
        var window = Window(new FakeFlashKitDevice(TestRoms.MakeRom(0x80000)));
        window.PickSavePath = (_, _) => Task.FromResult<string?>(null);

        await window.ReadRomAsync();

        var entry = Assert.Single(window.Log);
        Assert.Equal("Cancelled", entry.Status);
        Assert.False(entry.Failed);
        Assert.False(entry.Succeeded);
        Assert.False(entry.Running);
        Assert.True(Btn(window, "BtnReadRom").IsEnabled);
    }

    [AvaloniaFact]
    public async Task write_rom_programs_flash_and_reports_phases()
    {
        var image = TestRoms.MakeRom(0x20000, name: "NEW GAME");
        var fake = new FakeFlashKitDevice(TestRoms.MakeRom(0x80000)) { FlashWritable = true };
        var window = Window(fake);
        string file = TempFile("image.bin");
        File.WriteAllBytes(file, image);
        var statuses = new List<string>();
        window.PickOpenPath = _ =>
        {
            // The entry exists by picker time; record every status it goes
            // through so the transient phase labels are observable.
            var entry = window.Log[0];
            entry.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TransactionEntry.Status)) statuses.Add(entry.Status);
            };
            return Task.FromResult<string?>(file);
        };

        await window.WriteRomAsync();

        Assert.Contains("Flash erase...", statuses);
        Assert.Contains("Flash write...", statuses);
        Assert.Contains("Flash verify...", statuses);
        Assert.StartsWith("OK — 128K written, MD5 ", window.Log[0].Status);
        Assert.Equal(image, fake.Rom.Take(image.Length));
    }

    [AvaloniaFact]
    public async Task write_rom_without_flash_chip_fails_entry_and_reenables_buttons()
    {
        var window = Window(new FakeFlashKitDevice(TestRoms.MakeRom(0x80000)));
        string file = TempFile("image.bin");
        File.WriteAllBytes(file, TestRoms.MakeRom(0x20000));
        window.PickOpenPath = _ => Task.FromResult<string?>(file);

        await window.WriteRomAsync();

        var entry = Assert.Single(window.Log);
        Assert.True(entry.Failed);
        Assert.False(entry.Succeeded);
        Assert.False(entry.Running);
        Assert.Contains("No flash chip detected", entry.Status);
        Assert.True(Btn(window, "BtnWriteRom").IsEnabled);
    }

    [AvaloniaFact]
    public async Task write_ram_stores_odd_bytes_and_reports_words_sent()
    {
        var fake = new FakeFlashKitDevice(TestRoms.MakeRom(0x80000), sramBytes: 8192);
        var window = Window(fake);
        var srm = new byte[16384];
        for (int i = 0; i < srm.Length; i++) srm[i] = (byte)(i * 7);
        string file = TempFile("save.srm");
        File.WriteAllBytes(file, srm);
        window.PickOpenPath = _ => Task.FromResult<string?>(file);

        await window.WriteRamAsync();

        var entry = Assert.Single(window.Log);
        Assert.StartsWith("OK — 8192 words sent, MD5 ", entry.Status);
        for (int i = 0; i < 8192; i++) Assert.Equal(srm[i * 2 + 1], fake.Sram[i]);
    }

    [AvaloniaFact]
    public async Task buttons_are_disabled_while_an_operation_runs()
    {
        var window = Window(new FakeFlashKitDevice(TestRoms.MakeRom(0x80000)));
        window.PickSavePath = (_, _) => Task.FromResult<string?>(TempFile("dump.bin"));

        var running = window.ReadRomAsync();
        foreach (var name in new[] { "BtnReadRom", "BtnWriteRom", "BtnReadRam", "BtnWriteRam", "BtnRefresh" })
            Assert.False(Btn(window, name).IsEnabled);

        await running;
        foreach (var name in new[] { "BtnReadRom", "BtnWriteRom", "BtnReadRam", "BtnWriteRam", "BtnRefresh" })
            Assert.True(Btn(window, name).IsEnabled);
    }
}
