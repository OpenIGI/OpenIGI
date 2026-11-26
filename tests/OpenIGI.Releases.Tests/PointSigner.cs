using System.Security.Cryptography;

namespace OpenIGI.Releases.Tests;

/// <summary>A player's or server's key as IPlayerIdentity holds one: its public key an uncompressed point, its signatures r then s.</summary>
internal sealed class PointSigner : IRequestSigner, IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <inheritdoc/>
    public string PublicKey
    {
        get
        {
            ECParameters p = _key.ExportParameters(false);
            return Convert.ToBase64String([4, .. p.Q.X!, .. p.Q.Y!]);
        }
    }

    /// <inheritdoc/>
    public Task<byte[]> SignAsync(byte[] text) =>
        Task.FromResult(_key.SignData(text, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    /// <inheritdoc/>
    public void Dispose() => _key.Dispose();
}
