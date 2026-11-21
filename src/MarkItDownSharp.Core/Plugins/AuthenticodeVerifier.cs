using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace MarkItDownSharp.Core.Plugins;

public sealed record AuthenticodeResult(bool IsValid, string Reason, IReadOnlyList<string> Thumbprints);

/// <summary>
/// Verifies an embedded Authenticode signature with WinVerifyTrust and reports the signer thumbprints.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static AuthenticodeResult Verify(string path)
    {
        var fileInfo = new WinTrustFileInfo
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = path
        };
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var data = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UIChoice = 2, // WTD_UI_NONE
                RevocationChecks = 0, // WTD_REVOKE_NONE
                UnionChoice = 1, // WTD_CHOICE_FILE
                FileInfoPointer = fileInfoPointer,
                StateAction = 0, // WTD_STATEACTION_IGNORE
                ProviderFlags = 0x00000010 // WTD_CACHE_ONLY_URL_RETRIEVAL
            };

            var action = GenericVerifyV2;
            var status = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            if (status != 0)
            {
                return new AuthenticodeResult(false, $"WinVerifyTrust returned 0x{status:X8}.", []);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPointer);
        }

        try
        {
#pragma warning disable SYSLIB0057 // Reading the signer certificate; trust was established by WinVerifyTrust above.
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return new AuthenticodeResult(true, "Valid signature.", [certificate.Thumbprint, certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)]);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            return new AuthenticodeResult(false, ex.Message, []);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfoPointer;
        public uint StateAction;
        public IntPtr StateData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UrlReference;
        public uint ProviderFlags;
        public uint UIContext;
        public IntPtr SignatureSettings;
    }
}
