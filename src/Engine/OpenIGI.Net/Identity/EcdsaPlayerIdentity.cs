using System.Security.Cryptography;

namespace OpenIGI.Net.Identity;

/// <summary>A player identity in a desktop game or a dedicated server: an ECDSA P-256 key pair, kept in a file.</summary>
/// <remarks>
/// The key file holds the private key (PKCS #8). On Linux and macOS it is made readable by its owner alone. A file that
/// can't be read is replaced by a new key -- the player becomes someone new to every server, which is better than not
/// playing -- and a game that can't write it plays under a key that lasts the run.
/// </remarks>
public sealed class EcdsaPlayerIdentity : IPlayerIdentity, IPlayerVerifier, IDisposable
{
    private readonly ECDsa _key;

    private EcdsaPlayerIdentity(ECDsa key)
    {
        _key = key;
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        PublicKey = new byte[PlayerIdentities.PublicKeySize];
        PublicKey[0] = 4;
        parameters.Q.X.AsSpan().CopyTo(PublicKey.AsSpan(1));
        parameters.Q.Y.AsSpan().CopyTo(PublicKey.AsSpan(33));
    }

    /// <summary>Gets whether this platform has ECDSA (a browser's .NET has not).</summary>
    public static bool IsSupported { get; } = !OperatingSystem.IsBrowser();

    /// <inheritdoc/>
    public bool IsReady => true;

    /// <inheritdoc/>
    public byte[] PublicKey { get; }

    /// <summary>Gets the identity's id.</summary>
    public string Id => PlayerIdentities.IdOf(PublicKey);

    /// <summary>Makes an identity that lasts only this run: for tests, and for a game with nowhere to keep one.</summary>
    /// <returns>The identity.</returns>
    public static EcdsaPlayerIdentity CreateEphemeral() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>Loads the identity kept in a file, making and keeping a new one when there is none.</summary>
    /// <param name="path">The key file.</param>
    /// <returns>The identity.</returns>
    public static EcdsaPlayerIdentity LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path))
        {
            try
            {
                ECDsa loaded = ECDsa.Create();
                loaded.ImportPkcs8PrivateKey(File.ReadAllBytes(path), out _);
                if (loaded.KeySize == 256)
                {
                    return new EcdsaPlayerIdentity(loaded);
                }

                loaded.Dispose();
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            {
                // Unreadable: a new key below.
            }
        }

        ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, key.ExportPkcs8PrivateKey());
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere to keep it: it lasts the run.
        }

        return new EcdsaPlayerIdentity(key);
    }

    /// <inheritdoc/>
    public Task<byte[]> SignAsync(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Task.FromResult(_key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <inheritdoc/>
    public Task<bool> VerifyAsync(byte[] publicKey, byte[] data, byte[] signature) => Task.FromResult(Verify(publicKey, data, signature));

    /// <summary>Checks a signature at once.</summary>
    /// <param name="publicKey">The player's public key.</param>
    /// <param name="data">What was signed.</param>
    /// <param name="signature">The signature.</param>
    /// <returns><see langword="true"/> when the key signed exactly this.</returns>
    public static bool Verify(byte[] publicKey, byte[] data, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        if (publicKey.Length != PlayerIdentities.PublicKeySize || publicKey[0] != 4 || signature.Length != PlayerIdentities.SignatureSize)
        {
            return false;
        }

        try
        {
            using ECDsa key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..] },
            });
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _key.Dispose();
}

/// <summary>Checks signatures with the platform's own ECDSA: every server but one in a browser.</summary>
public sealed class EcdsaPlayerVerifier : IPlayerVerifier
{
    /// <summary>The one instance.</summary>
    public static readonly EcdsaPlayerVerifier Instance = new();

    private EcdsaPlayerVerifier()
    {
    }

    /// <inheritdoc/>
    public Task<bool> VerifyAsync(byte[] publicKey, byte[] data, byte[] signature) =>
        Task.FromResult(EcdsaPlayerIdentity.Verify(publicKey, data, signature));
}
