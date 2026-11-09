using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using OpenIGI.Net.Identity;

namespace OpenIGI.Net.Browser;

/// <summary>The player's identity in the browser build: identity.js's key pair, kept in the page's IndexedDB.</summary>
/// <remarks>
/// The page registers identity.js's imports under <see cref="ModuleName"/> before the game starts. Loading the key takes a
/// moment after that; <see cref="IsReady"/> says when it has.
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserPlayerIdentity : IPlayerIdentity, IPlayerVerifier
{
    /// <summary>The name the page registers identity.js's imports under.</summary>
    public const string ModuleName = "openigi-identity";

    private byte[]? _publicKey;

    /// <summary>Initializes a new instance of the <see cref="BrowserPlayerIdentity"/> class and starts loading the key.</summary>
    public BrowserPlayerIdentity() => Start();

    /// <inheritdoc/>
    public bool IsReady => Ready();

    /// <inheritdoc/>
    public byte[] PublicKey => _publicKey ??= IsReady ? Convert.FromBase64String(PublicKeyBase64()) : [];

    /// <inheritdoc/>
    public async Task<byte[]> SignAsync(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Convert.FromBase64String(await Sign(Convert.ToBase64String(data)).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public Task<bool> VerifyAsync(byte[] publicKey, byte[] data, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        return Verify(Convert.ToBase64String(publicKey), Convert.ToBase64String(data), Convert.ToBase64String(signature));
    }

    [JSImport("start", ModuleName)]
    private static partial void Start();

    [JSImport("ready", ModuleName)]
    private static partial bool Ready();

    [JSImport("publicKey", ModuleName)]
    private static partial string PublicKeyBase64();

    [JSImport("sign", ModuleName)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Sign(string dataBase64);

    [JSImport("verify", ModuleName)]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    private static partial Task<bool> Verify(string publicKeyBase64, string dataBase64, string signatureBase64);
}
