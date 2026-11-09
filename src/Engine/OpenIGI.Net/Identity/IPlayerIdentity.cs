using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenIGI.Net.Identity;

/// <summary>Who a player is, across sessions: a key pair kept by its game, which signs what a server asks it to.</summary>
/// <remarks>
/// <para>
/// A name is only what a player is called, and any two can share it. The identity is a P-256 key pair made once by each
/// install and kept: a server sends a fresh challenge, the player signs it, and the server checks the signature against
/// the public key that came with it. Its <see cref="PlayerIdentities.IdOf">id</see> -- a short hash of the public key -- is
/// what bans, rejoining and a server's stats follow; nobody without the private key can answer as it.
/// </para>
/// <para>
/// Signing may take a moment (a browser's crypto answers asynchronously), so it hands back a task the session polls.
/// </para>
/// </remarks>
public interface IPlayerIdentity
{
    /// <summary>Gets whether the key pair is loaded and can sign.</summary>
    bool IsReady { get; }

    /// <summary>Gets the public key, <see cref="PlayerIdentities.PublicKeySize"/> bytes uncompressed; empty until <see cref="IsReady"/>.</summary>
    byte[] PublicKey { get; }

    /// <summary>Signs a challenge.</summary>
    /// <param name="data">What to sign.</param>
    /// <returns>The signature, <see cref="PlayerIdentities.SignatureSize"/> bytes (r then s).</returns>
    Task<byte[]> SignAsync(byte[] data);
}

/// <summary>Checks a player's signature: the server's half of <see cref="IPlayerIdentity"/>.</summary>
public interface IPlayerVerifier
{
    /// <summary>Checks a signature.</summary>
    /// <param name="publicKey">The player's public key, as <see cref="IPlayerIdentity.PublicKey"/>.</param>
    /// <param name="data">What was signed.</param>
    /// <param name="signature">The signature.</param>
    /// <returns><see langword="true"/> when the key signed exactly this.</returns>
    Task<bool> VerifyAsync(byte[] publicKey, byte[] data, byte[] signature);
}

/// <summary>What every identity shares: the sizes, the id and what a proof signs.</summary>
public static class PlayerIdentities
{
    /// <summary>The bytes of a P-256 public key, uncompressed.</summary>
    public const int PublicKeySize = 65;

    /// <summary>The bytes of a P-256 signature: r and s, 32 bytes each.</summary>
    public const int SignatureSize = 64;

    /// <summary>The bytes of a server's challenge.</summary>
    public const int ChallengeSize = 16;

    /// <summary>Gets or sets the game's own identity, set once by its composition root: the one its sessions play under and check with.</summary>
    /// <remarks>
    /// The desktop keeps its key in a file in the user's settings folder, the browser in the page's IndexedDB. Unset (tests,
    /// tools), each session plays under a key made for it.
    /// </remarks>
    public static IPlayerIdentity? Default { get; set; }

    /// <summary>Gets a player's id from its public key: the first eight bytes of its SHA-256, as sixteen hex digits.</summary>
    /// <param name="publicKey">The public key.</param>
    /// <returns>The id.</returns>
    public static string IdOf(ReadOnlySpan<byte> publicKey)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(publicKey, hash);
        return Convert.ToHexStringLower(hash[..8]);
    }

    /// <summary>Gets what a player signs to prove itself to a server: the purpose, the protocol and the server's challenge.</summary>
    /// <param name="protocolId">The protocol the session plays, so a proof for one game is no proof for another.</param>
    /// <param name="challenge">The server's challenge.</param>
    /// <returns>The bytes to sign.</returns>
    public static byte[] ProofOf(ulong protocolId, ReadOnlySpan<byte> challenge)
    {
        ReadOnlySpan<byte> purpose = "OpenIGI player proof v1"u8;
        byte[] data = new byte[purpose.Length + 8 + challenge.Length];
        purpose.CopyTo(data);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(purpose.Length), protocolId);
        challenge.CopyTo(data.AsSpan(purpose.Length + 8));
        return data;
    }
}
