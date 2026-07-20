using FlashKit.Core;

namespace FlashKit.Core.Tests;

/// <summary>
/// Validates IpsPatch against a real IPS patch and real cart dumps, when the
/// assets are present. Drop three files into <c>dumps/ips-fixtures/</c> at the
/// repo root (or set FLASHKIT_IPS_FIXTURES to a directory holding them):
///   base.bin     — a clean cart dump
///   patch.ips    — the IPS patch
///   patched.bin  — a dump of the patched cart (or a reference patched ROM)
/// When the assets are absent the tests no-op, so CI — which never has them —
/// stays green. The data is real ROM content and is NEVER committed
/// (dumps/ is gitignored).
///
/// Validated with Ghostbusters (Genesis) + the Special Edition v2.0 patch,
/// which expands the 512 KB ROM to ~1.07 MB.
/// </summary>
public class IpsRealAssetTests
{
    [Fact]
    public void official_patch_applied_to_base_matches_the_patched_cart()
    {
        if (Fixtures() is not var (baseRom, patch, patched)) return;

        byte[] applied = IpsPatch.Apply(baseRom, patch);

        // Applying the official patch to the clean dump reproduces the
        // patched cart's content. A flash re-dump pads/mirrors the tail past
        // the patched ROM's real extent, so compare over the applied extent.
        Assert.True(patched.Length >= applied.Length,
            $"patched dump ({patched.Length} bytes) is shorter than the patched ROM ({applied.Length} bytes)");
        Assert.Equal(applied, patched[..applied.Length]);
    }

    [Fact]
    public void create_diff_of_the_two_dumps_roundtrips()
    {
        if (Fixtures() is not var (baseRom, _, patched)) return;

        // Our Create diff of the base and patched dumps re-applies to the
        // base to reproduce the patched dump exactly.
        Assert.Equal(patched, IpsPatch.Apply(baseRom, IpsPatch.Create(baseRom, patched)));
    }

    static (byte[] baseRom, byte[] patch, byte[] patched)? Fixtures()
    {
        string dir = Environment.GetEnvironmentVariable("FLASHKIT_IPS_FIXTURES")
            ?? Path.Combine(RepoRoot() ?? ".", "dumps", "ips-fixtures");
        string b = Path.Combine(dir, "base.bin");
        string p = Path.Combine(dir, "patch.ips");
        string m = Path.Combine(dir, "patched.bin");
        if (!File.Exists(b) || !File.Exists(p) || !File.Exists(m)) return null;
        return (File.ReadAllBytes(b), File.ReadAllBytes(p), File.ReadAllBytes(m));
    }

    static string? RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "flashkit-md.sln"))) return d.FullName;
        return null;
    }
}
