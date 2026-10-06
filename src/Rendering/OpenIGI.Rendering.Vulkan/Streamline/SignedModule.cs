using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OpenIGI.Rendering.Vulkan.Streamline;

/// <summary>Checks that a native module carries a valid Authenticode signature.</summary>
/// <remarks>
/// <para>
/// Streamline loads its plugins by name from a directory, which is a path this game does not own:
/// the DLLs are fetched rather than built, and they sit beside the executable where anything can
/// replace them. NVIDIA's own guidance is blunt about the consequence — a substituted plugin runs
/// inside the process with the process's privileges — and the SDK ships
/// <c>sl::security::verifyEmbeddedSignature</c> for exactly this. That helper is C++, so the same
/// check is made here through the API it wraps.
/// </para>
/// <para>
/// This verifies the signature cryptographically and walks the certificate chain, then requires the
/// signer to be NVIDIA — a merely valid signature would let anything with a code-signing certificate
/// through, which is most of the threat. Revocation is checked only against what the machine has
/// already cached, because a player launching a game offline should not be stalled on a network
/// timeout to load an upscaler.
/// </para>
/// <para>
/// The SDK's own helper goes further: it applies a strong-signature crypto policy and additionally
/// requires a second, NVIDIA-specific signature alongside the first. That is not reproduced here.
/// The launcher's hash-checked install (or the fetch script, on a development checkout) is the trusted
/// path onto disk, and this exists to catch a file that was replaced afterwards.
/// </para>
/// </remarks>
internal static partial class SignedModule
{
    /// <summary>Determines whether a file is signed by a trusted publisher.</summary>
    /// <param name="path">The absolute path to check.</param>
    /// <param name="status">
    /// The trust provider's verdict, which is what makes a refusal diagnosable: it separates "not
    /// signed" from "signed by someone not trusted here" from "the certificate has expired".
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the signature is present and valid. Always
    /// <see langword="false"/> off Windows, where Authenticode does not exist and Streamline is not
    /// available either.
    /// </returns>
    public static bool IsTrusted(string path, out int status)
    {
        status = 0;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!Path.IsPathRooted(path) || !File.Exists(path))
        {
            // WinVerifyTrust resolves a relative path against the current directory, which is not
            // where the caller means. Refuse rather than check the wrong file.
            return false;
        }

        status = Verify(path);
        if (status != 0)
        {
            return false;
        }

        string publisher = ExpectedPublisher(Path.GetFileName(path));
        if (!IsSignedBy(path, publisher))
        {
            status = publisher == NvidiaPublisher ? SignerIsNotNvidia : SignerIsNotMicrosoft;
            return false;
        }

        return true;
    }

    /// <summary>The organisation every Streamline module must be signed by.</summary>
    private const string NvidiaPublisher = "O=NVIDIA Corporation";

    /// <summary>The organisation the C++ runtime the modules link against is signed by.</summary>
    private const string MicrosoftPublisher = "O=Microsoft Corporation";

    /// <summary>
    /// The Microsoft libraries the pack may carry beside NVIDIA's: the Visual C++ runtime every Streamline
    /// module imports, carried app-locally so a machine without the redistributable still loads them, and
    /// the PIX event runtime the development plugins load. Each must be Microsoft's; nothing else may be.
    /// </summary>
    private static readonly HashSet<string> MicrosoftRuntime = new(StringComparer.OrdinalIgnoreCase)
    {
        "msvcp140.dll",
        "msvcp140_1.dll",
        "msvcp140_2.dll",
        "vcruntime140.dll",
        "vcruntime140_1.dll",
        "concrt140.dll",
        "WinPixEventRuntime.dll",
    };

    /// <summary>Names who must have signed a file the folder holds.</summary>
    /// <param name="fileName">The file name, without a folder.</param>
    /// <returns>The organisation clause of the signer's subject.</returns>
    internal static string ExpectedPublisher(string fileName) =>
        MicrosoftRuntime.Contains(fileName) ? MicrosoftPublisher : NvidiaPublisher;

    /// <summary>Checks every native module Streamline could load from a folder.</summary>
    /// <param name="directory">The absolute path of the folder.</param>
    /// <param name="refused">The first file that failed, with the reason, when one did.</param>
    /// <returns><see langword="true"/> when every module in the folder is NVIDIA-signed.</returns>
    /// <remarks>
    /// The interposer alone is not enough. It loads its plugins by name from its own folder, and the
    /// plugins load the NGX models the same way, so one replaced <c>sl.dlss.dll</c> beside a genuine
    /// interposer runs in the game's process exactly as a replaced interposer would. Nor is it enough to
    /// check only the files with Streamline's names: loading the interposer by path puts its folder on
    /// the search path for everything it imports, so any library sitting there can be pulled in. Every
    /// DLL in the folder must therefore be NVIDIA's — or, for the C++ runtime NVIDIA's modules link
    /// against, Microsoft's — and the pack carries nothing else. All or nothing:
    /// a folder with one bad file is refused whole, because there is no way to load the rest without
    /// also offering that one.
    /// </remarks>
    public static bool IsFolderTrusted(string directory, out string? refused)
    {
        refused = null;
        if (!OperatingSystem.IsWindows())
        {
            refused = "Authenticode is a Windows facility";
            return false;
        }

        if (!Path.IsPathRooted(directory) || !Directory.Exists(directory))
        {
            refused = $"'{directory}' is not a folder";
            return false;
        }

        bool any = false;
        foreach (string path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(path);
            any |= IsStreamlineModule(name);
            if (!IsTrusted(path, out int status))
            {
                refused = $"{name}: {Describe(status)}";
                return false;
            }
        }

        if (!any)
        {
            refused = "it holds none of the Streamline modules";
            return false;
        }

        return true;
    }

    /// <summary>Determines whether a file name is one Streamline or its plugins would load.</summary>
    /// <param name="fileName">The file name, without a folder.</param>
    /// <returns><see langword="true"/> for the interposer, a plugin, an NGX model or the low-latency layer.</returns>
    internal static bool IsStreamlineModule(string fileName) =>
        fileName.StartsWith("sl.", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("nvngx_", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("NvLowLatencyVk.dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>The verdict used when the file is validly signed, but not by NVIDIA.</summary>
    /// <remarks>
    /// Not a real trust-provider status. The provider has no way to express "signed by the wrong
    /// person", because from its point of view nothing is wrong.
    /// </remarks>
    public const int SignerIsNotNvidia = unchecked((int)0xE0000001);

    /// <summary>The verdict used when a runtime library is validly signed, but not by Microsoft.</summary>
    public const int SignerIsNotMicrosoft = unchecked((int)0xE0000002);

    /// <summary>Turns a trust provider verdict into something worth reading in a log.</summary>
    /// <param name="status">The value reported by <see cref="IsTrusted"/>.</param>
    /// <returns>The description.</returns>
    public static string Describe(int status) => (uint)status switch
    {
        0 => "trusted",
        0x800B0100 => "it carries no signature",
        0x800B0101 => "its certificate has expired",
        0x800B0109 => "its certificate does not chain to a trusted root",
        0x800B010C => "its certificate was revoked",
        0x80092003 => "the file could not be read",
        0xE0000001 => "it is validly signed, but not by NVIDIA",
        0xE0000002 => "it is validly signed, but not by Microsoft",
        _ => $"the trust provider reported 0x{(uint)status:X8}",
    };

    private static bool IsSignedBy(string path, string publisher)
    {
        try
        {
            // SYSLIB0057 points at X509CertificateLoader, which reads certificate *files*. There is
            // no replacement for pulling the signer out of an Authenticode signature, so this stays
            // until one exists.
#pragma warning disable SYSLIB0057
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057

            // Match on the organisation rather than the whole subject: the certificate's
            // organisational unit carries a build identifier that turns over between releases, and
            // pinning a thumbprint would mean this file expiring alongside NVIDIA's certificate.
            return certificate.Subject.Contains(publisher, StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            // The signature verified a moment ago, so this only happens if the file changed
            // underneath us. Treat it as untrusted.
            return false;
        }
    }

    private static unsafe int Verify(string path)
    {
        Guid action = WintrustActionGenericVerifyV2;

        fixed (char* pathPtr = path)
        {
            WintrustFileInfo file = new()
            {
                CbStruct = (uint)sizeof(WintrustFileInfo),
                PcwszFilePath = (nint)pathPtr,
            };

            WintrustData data = new()
            {
                CbStruct = (uint)sizeof(WintrustData),
                DwUiChoice = WtdUiNone,
                FdwRevocationChecks = WtdRevokeNone,
                DwUnionChoice = WtdChoiceFile,
                DwStateAction = WtdStateActionVerify,
                DwProvFlags = WtdCacheOnlyUrlRetrieval,
                PFile = (nint)(&file),
            };

            // INVALID_HANDLE_VALUE, not null: null asks the trust provider to parent any prompt on
            // the desktop window, which is not what a headless verification wants.
            int result = WinVerifyTrust(-1, ref action, ref data);

            // The verify action allocates state that must be released whatever the verdict was.
            data.DwStateAction = WtdStateActionClose;
            _ = WinVerifyTrust(-1, ref action, ref data);

            return result;
        }
    }

    // WINTRUST_ACTION_GENERIC_VERIFY_V2
    private static readonly Guid WintrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    // Deliberately NOT WTD_SAFER_FLAG (0x100). It asks a different question -- whether Software
    // Restriction Policy trusts the publisher -- and on an ordinary machine that answers no for a
    // certificate the OS itself considers perfectly valid. Setting it made this refuse NVIDIA's own
    // signed interposer with CERT_E_UNTRUSTEDROOT while Get-AuthenticodeSignature reported it Valid.
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(nint window, ref Guid action, ref WintrustData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint CbStruct;
        public nint PcwszFilePath;
        public nint HFile;
        public nint PgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint CbStruct;
        public nint PPolicyCallbackData;
        public nint PSipClientData;
        public uint DwUiChoice;
        public uint FdwRevocationChecks;
        public uint DwUnionChoice;
        public nint PFile;
        public uint DwStateAction;
        public nint HWvtStateData;
        public nint PwszUrlReference;
        public uint DwProvFlags;
        public uint DwUiContext;
        public nint PSignatureSettings;
    }
}
