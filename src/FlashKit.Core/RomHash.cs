using System.IO.Hashing;
using System.Security.Cryptography;

namespace FlashKit.Core;

/// <summary>
/// ROM identity hashes as compact uppercase hex — the form No-Intro and
/// romhacking.net quote, so a dump can be checked against a database.
/// </summary>
public static class RomHash
{
    public static string Crc32(byte[] data)
    {
        var crc = new Crc32();
        crc.Append(data);
        // Crc32 emits the digest little-endian; reverse to the conventional
        // big-endian hex (e.g. 792DF93B).
        var bytes = crc.GetCurrentHash();
        Array.Reverse(bytes);
        return Convert.ToHexString(bytes);
    }

    public static string Md5(byte[] data) => Convert.ToHexString(MD5.HashData(data));

    public static string Sha1(byte[] data) => Convert.ToHexString(SHA1.HashData(data));
}
