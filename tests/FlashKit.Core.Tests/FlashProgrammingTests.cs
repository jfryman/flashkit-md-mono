namespace FlashKit.Core.Tests;

/// <summary>
/// Exercises the full flash sequence the original write-ROM flow uses:
/// reset, sector erase per 64 KB, unlock bypass, program, reset, verify.
/// The fake flash has AND-programming semantics and starts at 0x00, so a
/// skipped erase would corrupt the readback.
/// </summary>
public class FlashProgrammingTests
{
    [Fact]
    public void erase_program_readback_roundtrip()
    {
        const int romSize = 0x20000; // 128 KB
        var fake = new FakeFlashKitDevice(new byte[romSize]) { FlashWritable = true };
        var device = new Device(fake);

        var data = new byte[romSize];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 31 + 7);

        device.flashResetByPass();
        for (int i = 0; i < romSize; i += 65536) device.flashErase(i);

        device.flashUnlockBypass();
        device.setAddr(0);
        for (int i = 0; i < romSize; i += 4096) device.flashWrite(data, i, 4096);
        device.flashResetByPass();

        var readback = new byte[romSize];
        device.setAddr(0);
        device.read(readback, 0, romSize);

        Assert.Equal(data, readback);
    }

    [Fact]
    public void erase_fills_sectors_with_ff()
    {
        const int romSize = 0x20000;
        var fake = new FakeFlashKitDevice(new byte[romSize]) { FlashWritable = true };
        var device = new Device(fake);

        device.flashResetByPass();
        device.flashErase(0x10000); // second 64 KB block only

        Assert.All(fake.Rom.Take(0x10000), b => Assert.Equal(0x00, b));
        Assert.All(fake.Rom.Skip(0x10000), b => Assert.Equal(0xFF, b));
    }

    [Fact]
    public void program_without_erase_can_only_clear_bits()
    {
        const int romSize = 0x20000;
        var fake = new FakeFlashKitDevice(new byte[romSize]) { FlashWritable = true };
        var device = new Device(fake);

        // No erase: flash still holds 0x00, so programming 0xFF must not set bits.
        device.flashUnlockBypass();
        device.setAddr(0);
        device.flashWrite(new byte[] { 0xFF, 0xFF }, 0, 2);
        device.flashResetByPass();

        Assert.Equal(0x00, fake.Rom[0]);
        Assert.Equal(0x00, fake.Rom[1]);
    }
}
