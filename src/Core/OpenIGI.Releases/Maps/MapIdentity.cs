using System.Security.Cryptography;
using System.Text;

namespace OpenIGI.Releases.Maps;

/// <summary>
/// Who publishes, rates and reports maps from one installation: a key of its own (ECDSA, P-256), made the first time it is
/// needed and kept in the user's settings folder. The master server knows a map's author by the key that first published it,
/// and takes a new version, a rating or a report only signed by a key.
/// </summary>
/// <remarks>
/// No account and nothing to sign up to: losing the key loses the right to publish new versions of one's maps, nothing else.
/// What is signed is the request -- its method, path, time and the hash of its body -- so a signature is good for that request
/// alone, and only for a few minutes.
/// </remarks>
public sealed class MapIdentity : IDisposable
{
    /// <summary>The header the public key goes in, as its SubjectPublicKeyInfo in base64.</summary>
    public const string KeyHeader = "X-OpenIGI-Key";

    /// <summary>The header the signature goes in, IEEE P1363 (r then s) in base64.</summary>
    public const string SignatureHeader = "X-OpenIGI-Signature";

    /// <summary>The header the signing time goes in, as Unix seconds.</summary>
    public const string TimeHeader = "X-OpenIGI-Time";

    private readonly ECDsa _key;

    private MapIdentity(ECDsa key)
    {
        _key = key;
        PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        KeyId = Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()))[..16];
    }

    /// <summary>Gets the public key, its SubjectPublicKeyInfo in base64, as the server is sent it.</summary>
    public string PublicKey { get; }

    /// <summary>Gets a short name for the key, the first sixteen hex digits of its hash: how the server names an author.</summary>
    public string KeyId { get; }

    /// <summary>Loads the installation's key, or makes and keeps one.</summary>
    /// <param name="path">The file it is kept in.</param>
    /// <returns>The identity.</returns>
    public static MapIdentity LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ECDsa key = ECDsa.Create();
        if (File.Exists(path))
        {
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(File.ReadAllText(path).Trim()), out _);
            return new MapIdentity(key);
        }

        key.GenerateKey(ECCurve.NamedCurves.nistP256);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        File.WriteAllText(path, Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
        return new MapIdentity(key);
    }

    /// <summary>Makes a key held in memory alone, for a test.</summary>
    /// <returns>The identity.</returns>
    public static MapIdentity CreateEphemeral()
    {
        ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new MapIdentity(key);
    }

    /// <summary>The text a request's signature is over: its method, path, time and body's hash, a line each.</summary>
    /// <param name="method">The method, such as <c>POST</c>.</param>
    /// <param name="path">The path, such as <c>/v1/maps</c>.</param>
    /// <param name="unixSeconds">When it is signed.</param>
    /// <param name="body">The body.</param>
    /// <returns>The text.</returns>
    public static string SignedText(string method, string path, long unixSeconds, ReadOnlySpan<byte> body) =>
        $"{method.ToUpperInvariant()}\n{path}\n{unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n{MapPackage.Hash(body)}";

    /// <summary>Signs a request.</summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="unixSeconds">When it is signed.</param>
    /// <param name="body">The body.</param>
    /// <returns>The headers to send it with.</returns>
    public IReadOnlyList<(string Name, string Value)> Sign(string method, string path, long unixSeconds, ReadOnlySpan<byte> body)
    {
        byte[] signature = _key.SignData(Encoding.UTF8.GetBytes(SignedText(method, path, unixSeconds, body)), HashAlgorithmName.SHA256);
        return
        [
            (KeyHeader, PublicKey),
            (SignatureHeader, Convert.ToBase64String(signature)),
            (TimeHeader, unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ];
    }

    /// <summary>Checks a request's signature, as the server does.</summary>
    /// <param name="publicKey">The public key it came with.</param>
    /// <param name="signature">Its signature.</param>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="unixSeconds">The time it says it was signed.</param>
    /// <param name="body">The body.</param>
    /// <returns>Whether the key signed it.</returns>
    public static bool Verify(string publicKey, string signature, string method, string path, long unixSeconds, ReadOnlySpan<byte> body)
    {
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.VerifyData(Encoding.UTF8.GetBytes(SignedText(method, path, unixSeconds, body)), Convert.FromBase64String(signature), HashAlgorithmName.SHA256);
        }
        catch (Exception error) when (error is FormatException or CryptographicException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _key.Dispose();
}
