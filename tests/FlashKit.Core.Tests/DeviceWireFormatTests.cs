namespace FlashKit.Core.Tests;

/// <summary>
/// Locks the exact bytes <see cref="Device"/> puts on the wire to the format
/// of the original Windows client, so protocol regressions surface without
/// hardware. Expected sequences are hardcoded from the original Device.cs.
/// </summary>
public class DeviceWireFormatTests
{
    // [CMD_ADDR, hi, CMD_ADDR, mid, CMD_ADDR, lo] for a 24-bit word address
    static byte[] Addr(int wordAddr) => new byte[]
    {
        0, (byte)(wordAddr >> 16), 0, (byte)(wordAddr >> 8), 0, (byte)wordAddr,
    };

    static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void getID_sends_dev_id_read_and_combines_reply()
    {
        var port = new ScriptedPort();
        port.EnqueueReply(0x55, 0x55);
        Assert.Equal(0x5555, new Device(port).getID());
        Assert.Equal(new byte[] { 2 | 64 | 32 }, port.Written);
    }

    [Fact]
    public void setDelay_sends_delay_command()
    {
        var port = new ScriptedPort();
        new Device(port).setDelay(1);
        Assert.Equal(new byte[] { 5, 1 }, port.Written);
    }

    [Fact]
    public void readWord_sends_word_address_and_single_read()
    {
        var port = new ScriptedPort();
        port.EnqueueReply(0xAB, 0xCD);
        // byte address 0x123456 -> word address 0x091A2B
        Assert.Equal(0xABCD, new Device(port).readWord(0x123456));
        Assert.Equal(Cat(Addr(0x091A2B), new byte[] { 2 | 64 }), port.Written);
    }

    [Fact]
    public void writeWord_sends_address_then_big_endian_data()
    {
        var port = new ScriptedPort();
        new Device(port).writeWord(0x123456, 0xBEEF);
        Assert.Equal(Cat(Addr(0x091A2B), new byte[] { 3 | 64, 0xBE, 0xEF }), port.Written);
    }

    [Fact]
    public void writeByte_sends_address_then_mode8_data()
    {
        var port = new ScriptedPort();
        new Device(port).writeByte(0x123456, 0x5A);
        Assert.Equal(Cat(Addr(0x091A2B), new byte[] { 3 | 64 | 16, 0x5A }), port.Written);
    }

    [Fact]
    public void setAddr_sends_three_address_pairs()
    {
        var port = new ScriptedPort();
        new Device(port).setAddr(0x200000);
        Assert.Equal(Addr(0x100000), port.Written);
    }

    [Fact]
    public void read_chunks_at_64K_and_sends_word_counts()
    {
        var port = new ScriptedPort();
        var expected = new byte[65538];
        for (int i = 0; i < expected.Length; i++) expected[i] = (byte)(i * 13);
        port.EnqueueReply(expected);

        var buff = new byte[65538];
        new Device(port).read(buff, 0, buff.Length);

        Assert.Equal(expected, buff);
        Assert.Equal(Cat(
            new byte[] { 1, 0x80, 1, 0x00, 2 | 128 },  // 32768 words
            new byte[] { 1, 0x00, 1, 0x01, 2 | 128 }), // 1 word
            port.Written);
    }

    [Fact]
    public void write_sends_word_count_then_raw_data()
    {
        var port = new ScriptedPort();
        new Device(port).write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, 0, 4);
        Assert.Equal(new byte[] { 1, 0, 1, 2, 3 | 128, 0xDE, 0xAD, 0xBE, 0xEF }, port.Written);
    }

    [Fact]
    public void flashUnlockBypass_sends_amd_unlock_sequence()
    {
        var port = new ScriptedPort();
        new Device(port).flashUnlockBypass();
        Assert.Equal(Cat(
            Addr(0x555), new byte[] { 3 | 64 | 16, 0xAA },
            Addr(0x2AA), new byte[] { 3 | 64 | 16, 0x55 },
            Addr(0x555), new byte[] { 3 | 64 | 16, 0x20 }), port.Written);
    }

    [Fact]
    public void flashResetByPass_sends_reset_then_bypass_exit()
    {
        var port = new ScriptedPort();
        new Device(port).flashResetByPass();
        Assert.Equal(Cat(
            Addr(0), new byte[] { 3 | 64, 0x00, 0xF0 },
            Addr(0), new byte[] { 3 | 64 | 16, 0x90 },
            Addr(0), new byte[] { 3 | 64 | 16, 0x00 }), port.Written);
    }

    [Fact]
    public void flashWrite_arms_program_per_word_with_ready_wait()
    {
        var port = new ScriptedPort();
        new Device(port).flashWrite(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, 0, 4);
        Assert.Equal(new byte[]
        {
            3 | 64 | 16, 0xA0, 3 | 64 | 128, 0xDE, 0xAD, 4,
            3 | 64 | 16, 0xA0, 3 | 64 | 128, 0xBE, 0xEF, 4,
        }, port.Written);
    }

    [Fact]
    public void flashErase_sends_unlock_then_eight_sector_erases_then_ready_check()
    {
        var port = new ScriptedPort();
        port.EnqueueReply(0xFF, 0xFF); // flashRY status word

        new Device(port).flashErase(0x10000); // word address 0x8000

        var batch = new List<byte>();
        for (int i = 0; i < 8; i++)
        {
            batch.AddRange(Addr(0x8000 + i * 4096));
            batch.Add(3 | 64 | 16);
            batch.Add(0x30);
        }

        Assert.Equal(Cat(
            Addr(0x555), new byte[] { 3 | 64, 0x00, 0xAA },
            Addr(0x2AA), new byte[] { 3 | 64, 0x00, 0x55 },
            Addr(0x555), new byte[] { 3 | 64, 0x00, 0x80 },
            Addr(0x555), new byte[] { 3 | 64, 0x00, 0xAA },
            Addr(0x2AA), new byte[] { 3 | 64, 0x00, 0x55 },
            batch.ToArray(),
            new byte[] { 4, 2 | 64 }), port.Written);
    }
}
