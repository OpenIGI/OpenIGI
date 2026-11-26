using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace OpenIGI.Releases;

/// <summary>A key that signs requests to the master server: a map publisher's (<see cref="Maps.MapIdentity"/>) or the key a player plays under.</summary>
public interface IRequestSigner
{
    /// <summary>Gets the public key as it goes in <see cref="SignedRequest.KeyHeader"/>: base64 of a SubjectPublicKeyInfo or of an uncompressed point.</summary>
    string PublicKey { get; }

    /// <summary>Signs a request's <see cref="SignedRequest.Text">text</see>.</summary>
    /// <param name="text">What to sign.</param>
    /// <returns>The ECDSA P-256 signature over its SHA-256, IEEE P1363 (r then s).</returns>
    Task<byte[]> SignAsync(byte[] text);
}

/// <summary>
/// A request signed by an installation's key, as the master server checks it (tools/master-server/src/signed.js): an ECDSA
/// signature over the method, path, time and the body's hash, a line each, good for that request alone and for ten minutes.
/// </summary>
public static class SignedRequest
{
    /// <summary>The header the public key goes in, base64.</summary>
    public const string KeyHeader = "X-OpenIGI-Key";

    /// <summary>The header the signature goes in, IEEE P1363 (r then s) in base64.</summary>
    public const string SignatureHeader = "X-OpenIGI-Signature";

    /// <summary>The header the signing time goes in, as Unix seconds.</summary>
    public const string TimeHeader = "X-OpenIGI-Time";

    // The bytes of a P-256 public key as an uncompressed point: 0x04, then X and Y.
    private const int RawPointSize = 65;

    /// <summary>The text a request's signature is over.</summary>
    /// <param name="method">The method, such as <c>POST</c>.</param>
    /// <param name="path">The path alone, such as <c>/v1/maps</c>: no query.</param>
    /// <param name="unixSeconds">When it is signed.</param>
    /// <param name="body">The body.</param>
    /// <returns>The text.</returns>
    public static string Text(string method, string path, long unixSeconds, ReadOnlySpan<byte> body) =>
        $"{method.ToUpperInvariant()}\n{path}\n{unixSeconds.ToString(CultureInfo.InvariantCulture)}\n{Convert.ToHexStringLower(SHA256.HashData(body))}";

    /// <summary>Signs a request: its three headers.</summary>
    /// <param name="signer">The key.</param>
    /// <param name="method">The method.</param>
    /// <param name="path">The path alone.</param>
    /// <param name="unixSeconds">When it is signed.</param>
    /// <param name="body">The body.</param>
    /// <returns>The headers to send it with.</returns>
    public static async Task<IReadOnlyList<(string Name, string Value)>> HeadersAsync(IRequestSigner signer, string method, string path, long unixSeconds, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(signer);
        byte[] signature = await signer.SignAsync(Encoding.UTF8.GetBytes(Text(method, path, unixSeconds, body))).ConfigureAwait(false);
        return
        [
            (KeyHeader, signer.PublicKey),
            (SignatureHeader, Convert.ToBase64String(signature)),
            (TimeHeader, unixSeconds.ToString(CultureInfo.InvariantCulture)),
        ];
    }

    /// <summary>Makes a signed request, now.</summary>
    /// <param name="signer">The key.</param>
    /// <param name="method">The method.</param>
    /// <param name="master">The master server.</param>
    /// <param name="path">The path, which may carry a query (the query is not signed).</param>
    /// <param name="body">The body.</param>
    /// <param name="contentType">The body's type.</param>
    /// <returns>The request, for the caller to send and dispose.</returns>
    public static async Task<HttpRequestMessage> CreateAsync(IRequestSigner signer, HttpMethod method, Uri master, string path, byte[] body, string contentType = "application/json")
    {
        ArgumentNullException.ThrowIfNull(method);
        Uri uri = new(master, path);
        HttpRequestMessage request = new(method, uri);
        if (method != HttpMethod.Get)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        foreach ((string name, string value) in await HeadersAsync(signer, method.Method, uri.AbsolutePath, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), method == HttpMethod.Get ? [] : body).ConfigureAwait(false))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    /// <summary>Checks a request's signature, as the master server does.</summary>
    /// <param name="publicKey">The key it came with: base64 of a SubjectPublicKeyInfo or of a 65-byte uncompressed point.</param>
    /// <param name="signature">Its signature, base64.</param>
    /// <param name="method">The method.</param>
    /// <param name="path">The path alone.</param>
    /// <param name="unixSeconds">The time it says it was signed.</param>
    /// <param name="body">The body.</param>
    /// <returns>Whether the key signed it.</returns>
    public static bool Verify(string publicKey, string signature, string method, string path, long unixSeconds, ReadOnlySpan<byte> body)
    {
        try
        {
            byte[] keyBytes = Convert.FromBase64String(publicKey);
            using ECDsa key = ECDsa.Create();
            if (keyBytes.Length == RawPointSize && keyBytes[0] == 4)
            {
                key.ImportParameters(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = keyBytes[1..33], Y = keyBytes[33..] },
                });
            }
            else
            {
                key.ImportSubjectPublicKeyInfo(keyBytes, out _);
            }

            return key.VerifyData(Encoding.UTF8.GetBytes(Text(method, path, unixSeconds, body)), Convert.FromBase64String(signature), HashAlgorithmName.SHA256);
        }
        catch (Exception error) when (error is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
