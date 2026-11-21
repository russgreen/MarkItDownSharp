namespace MarkItDownSharp.Core.Plugins;

/// <summary>
/// Decides which third-party converter assemblies may be loaded.
/// Plugin loading is off by default. An assembly is trusted only when its SHA-256 hash is pinned,
/// or when it carries a valid Authenticode signature from a pinned signer (Windows only).
/// </summary>
public sealed class PluginTrustPolicy
{
    /// <summary>A policy that refuses every plugin.</summary>
    public static PluginTrustPolicy DenyAll { get; } = new();

    public bool Enabled { get; init; }

    /// <summary>Hex SHA-256 hashes of individual plugin assemblies that are trusted.</summary>
    public IReadOnlyCollection<string> TrustedSha256 { get; init; } = [];

    /// <summary>Hex SHA-1 or SHA-256 thumbprints of code-signing certificates whose signed assemblies are trusted.</summary>
    public IReadOnlyCollection<string> TrustedSignerThumbprints { get; init; } = [];

    public PluginTrustDecision Evaluate(string assemblyPath)
    {
        if (!Enabled)
        {
            return new PluginTrustDecision(false, "Plugin loading is disabled.", null);
        }

        var hash = AssemblyHasher.Sha256Hex(assemblyPath);
        if (TrustedSha256.Contains(hash, StringComparer.OrdinalIgnoreCase))
        {
            return new PluginTrustDecision(true, "Assembly hash is pinned.", hash);
        }

        if (TrustedSignerThumbprints.Count > 0)
        {
            if (!OperatingSystem.IsWindows())
            {
                return new PluginTrustDecision(false, "Signature verification is only available on Windows.", hash);
            }

            var signature = AuthenticodeVerifier.Verify(assemblyPath);
            if (!signature.IsValid)
            {
                return new PluginTrustDecision(false, $"Authenticode signature is not valid: {signature.Reason}", hash);
            }

            if (signature.Thumbprints.Any(t => TrustedSignerThumbprints.Contains(t, StringComparer.OrdinalIgnoreCase)))
            {
                return new PluginTrustDecision(true, "Signed by a trusted publisher.", hash);
            }

            return new PluginTrustDecision(false, "Signed by a publisher that is not trusted.", hash);
        }

        return new PluginTrustDecision(false, "Assembly is neither hash-pinned nor signed by a trusted publisher.", hash);
    }
}

public sealed record PluginTrustDecision(bool IsTrusted, string Reason, string? Sha256);
