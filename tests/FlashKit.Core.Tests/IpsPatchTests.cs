using FlashKit.Core;

namespace FlashKit.Core.Tests;

public class IpsPatchTests
{
    static byte[] Patch(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void apply_writes_a_literal_record_in_place()
    {
        var rom = new byte[] { 0, 1, 2, 3, 4, 5 };
        // PATCH, offset 0x000002, size 0x0002, data DE AD, EOF
        var patch = Patch("PATCH"u8.ToArray(),
            new byte[] { 0x00, 0x00, 0x02, 0x00, 0x02, 0xDE, 0xAD },
            "EOF"u8.ToArray());

        Assert.Equal(new byte[] { 0, 1, 0xDE, 0xAD, 4, 5 }, IpsPatch.Apply(rom, patch));
    }

    [Fact]
    public void apply_expands_an_rle_record()
    {
        var rom = new byte[4];
        // offset 0x000001, size 0 (RLE), run 0x0003, value 0xFF
        var patch = Patch("PATCH"u8.ToArray(),
            new byte[] { 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x03, 0xFF },
            "EOF"u8.ToArray());

        Assert.Equal(new byte[] { 0, 0xFF, 0xFF, 0xFF }, IpsPatch.Apply(rom, patch));
    }

    [Fact]
    public void apply_extends_the_rom_when_a_record_runs_past_the_end()
    {
        var rom = new byte[] { 1, 2 };
        var patch = Patch("PATCH"u8.ToArray(),
            new byte[] { 0x00, 0x00, 0x04, 0x00, 0x02, 0xAA, 0xBB }, // offset 4, past end
            "EOF"u8.ToArray());

        Assert.Equal(new byte[] { 1, 2, 0, 0, 0xAA, 0xBB }, IpsPatch.Apply(rom, patch));
    }

    [Fact]
    public void apply_honors_the_truncation_extension()
    {
        var rom = new byte[] { 1, 2, 3, 4, 5 };
        var patch = Patch("PATCH"u8.ToArray(), "EOF"u8.ToArray(),
            new byte[] { 0x00, 0x00, 0x03 }); // truncate to 3 bytes

        Assert.Equal(new byte[] { 1, 2, 3 }, IpsPatch.Apply(rom, patch));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]                 // no PATCH header
    [InlineData(new byte[] { (byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H', 0x00 })] // truncated record
    public void apply_rejects_malformed_patches(byte[] patch)
    {
        Assert.Throws<IpsFormatException>(() => IpsPatch.Apply(new byte[4], patch));
    }

    [Fact]
    public void create_of_identical_data_is_header_and_eof_only()
    {
        var data = new byte[] { 1, 2, 3, 4 };
        var patch = IpsPatch.Create(data, (byte[])data.Clone());

        Assert.Equal(Patch("PATCH"u8.ToArray(), "EOF"u8.ToArray()), patch);
        Assert.Equal(data, IpsPatch.Apply(data, patch)); // no-op
    }

    [Theory]
    [InlineData("in-place single byte")]
    [InlineData("scattered edits")]
    [InlineData("extend longer")]
    [InlineData("truncate shorter")]
    [InlineData("long rle run")]
    [InlineData("empty target")]
    public void roundtrip_apply_of_create_reproduces_modified(string shape)
    {
        var rng = new Random(1234);
        var original = new byte[5000];
        rng.NextBytes(original);

        byte[] modified = shape switch
        {
            "in-place single byte" => Edit(original, m => m[2500] ^= 0xFF),
            "scattered edits" => Edit(original, m => { for (int k = 0; k < m.Length; k += 137) m[k] ^= 0x5A; }),
            "extend longer" => Grow(original, 3000, 0x00), // blank tail -> exercises RLE
            "truncate shorter" => original[..2048],
            "long rle run" => Edit(original, m => Array.Fill(m, (byte)0xFF, 100, 2000)),
            "empty target" => Array.Empty<byte>(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var patch = IpsPatch.Create(original, modified);
        Assert.Equal(modified, IpsPatch.Apply(original, patch));
    }

    [Fact]
    public void create_uses_rle_to_keep_a_blank_extension_tiny()
    {
        var original = new byte[1024];
        var modified = new byte[0x40000];             // +252 KB of 0x00 tail
        Array.Copy(original, modified, original.Length);

        var patch = IpsPatch.Create(original, modified);

        Assert.Equal(modified, IpsPatch.Apply(original, patch));
        Assert.True(patch.Length < 200, $"expected a tiny RLE patch, got {patch.Length} bytes");
    }

    [Fact]
    public void create_and_apply_roundtrip_over_random_fuzzing()
    {
        var rng = new Random(9001);
        for (int trial = 0; trial < 200; trial++)
        {
            var original = new byte[rng.Next(0, 400)];
            rng.NextBytes(original);
            var modified = new byte[rng.Next(0, 400)];
            rng.NextBytes(modified);
            // sprinkle equal regions and repeated runs so RLE/literal both fire
            int copy = Math.Min(original.Length, modified.Length);
            for (int k = 0; k < copy; k++) if (rng.Next(3) == 0) modified[k] = original[k];
            if (modified.Length > 20) Array.Fill(modified, (byte)rng.Next(256), 5, 10);

            var patch = IpsPatch.Create(original, modified);
            Assert.Equal(modified, IpsPatch.Apply(original, patch));
        }
    }

    static byte[] Edit(byte[] src, Action<byte[]> edit)
    {
        var m = (byte[])src.Clone();
        edit(m);
        return m;
    }

    static byte[] Grow(byte[] src, int extra, byte fill)
    {
        var m = new byte[src.Length + extra];
        Array.Copy(src, m, src.Length);
        Array.Fill(m, fill, src.Length, extra);
        return m;
    }
}
