using System.Security.Cryptography;

namespace MarkItDownSharp.Core.Plugins;

public static class AssemblyHasher
{
    public static string Sha256Hex(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
