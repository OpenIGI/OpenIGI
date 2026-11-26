using System.Security.Cryptography;

namespace OpenIGI.Releases.Maps;

/// <summary>
/// Who publishes, rates and reports maps from one installation: a key of its own (ECDSA, P-256), made the first time it is
/// needed and kept in the user's settings folder. The master server knows a map's author by the key that first published it,
/// and takes a new version, a rating or a report only signed by a key.
/// </summary>
/// <remarks>
/// No account and nothing to sign up to: losing the key loses the right to publish new versions of one's maps, nothing else.
/// What is signed is the request (<see cref="SignedRequest"/>) -- its method, path, time and the hash of its body -- so a
/// signature is good for that request alone, and only for a few minutes.
/// </remarks>
public sealed class MapIdentity : IRequestSigner, IDisposable
{
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

    /// <inheritdoc/>
    public Task<byte[]> SignAsync(byte[] text) => Task.FromResult(_key.SignData(text, HashAlgorithmName.SHA256));

    /// <inheritdoc/>
    public void Dispose() => _key.Dispose();
}
