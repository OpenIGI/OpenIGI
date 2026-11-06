using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenIGI.Net;

/// <summary>The keys a connection's payload packets are sealed with, once both ends agreed them in the handshake.</summary>
/// <remarks>
/// <para>
/// The handshake is an elliptic-curve Diffie-Hellman exchange on P-256: the server keeps one key pair for its run and sends
/// its public half in each challenge; a client makes one for each attempt and sends its public half with its answer, the
/// join payload already sealed under the agreed secret. Neither key is ever sent in the clear in a form that opens
/// anything, so the password and everything after it are read by nobody on the line.
/// </para>
/// <para>
/// Each direction has its own AES-256-GCM key. A packet's nonce is the direction and its sequence counted on past the
/// sixteen bits the header carries, so no nonce is ever used twice under one key; the header itself is authenticated
/// with the body, and a packet changed on the way, or replayed, fails to open and is dropped.
/// </para>
/// <para>
/// A line that is encrypted already (a WebRTC data channel runs over DTLS) skips all of this, as does a game where the
/// platform has neither primitive (a browser's .NET has none).
/// </para>
/// </remarks>
internal sealed class PacketCipher : IDisposable
{
    /// <summary>The bytes of a P-256 public key, uncompressed.</summary>
    public const int PublicKeySize = 65;

    /// <summary>The bytes a sealed packet carries beyond its plaintext.</summary>
    public const int Overhead = TagSize;

    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly AesGcm _send;
    private readonly AesGcm _receive;
    private readonly uint _sendLabel;
    private readonly uint _receiveLabel;

    private PacketCipher(byte[] sendKey, byte[] receiveKey, uint sendLabel, uint receiveLabel)
    {
        _send = new AesGcm(sendKey, TagSize);
        _receive = new AesGcm(receiveKey, TagSize);
        _sendLabel = sendLabel;
        _receiveLabel = receiveLabel;
        CryptographicOperations.ZeroMemory(sendKey);
        CryptographicOperations.ZeroMemory(receiveKey);
    }

    /// <summary>Gets whether this platform can seal packets.</summary>
    public static bool IsSupported { get; } = ProbeSupport();

    /// <summary>Makes a key pair for one side of the handshake.</summary>
    /// <returns>The key pair.</returns>
    public static ECDiffieHellman CreateKey() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Writes a key pair's public half.</summary>
    /// <param name="key">The key pair.</param>
    /// <param name="destination">Where to: <see cref="PublicKeySize"/> bytes.</param>
    public static void WritePublicKey(ECDiffieHellman key, Span<byte> destination)
    {
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        destination[0] = 4;
        parameters.Q.X.AsSpan().CopyTo(destination[1..]);
        parameters.Q.Y.AsSpan().CopyTo(destination[33..]);
    }

    /// <summary>Agrees the connection's keys from this side's key pair and the other's public half.</summary>
    /// <param name="own">This side's key pair.</param>
    /// <param name="otherPublic">The other side's public key, as <see cref="WritePublicKey"/> wrote it.</param>
    /// <param name="nonce">The client's handshake nonce, which ties the keys to this one attempt.</param>
    /// <param name="isServer">Whether this side is the server.</param>
    /// <returns>The keys, or <see langword="null"/> when the other's key is not a point on the curve.</returns>
    public static PacketCipher? Agree(ECDiffieHellman own, ReadOnlySpan<byte> otherPublic, ulong nonce, bool isServer)
    {
        if (otherPublic.Length < PublicKeySize || otherPublic[0] != 4)
        {
            return null;
        }

        try
        {
            using ECDiffieHellman other = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = otherPublic.Slice(1, 32).ToArray(), Y = otherPublic.Slice(33, 32).ToArray() },
            });

            byte[] salt = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(salt, nonce);
            byte[] toServer = DeriveKey(own, other.PublicKey, "OpenIGI client to server", salt);
            byte[] toClient = DeriveKey(own, other.PublicKey, "OpenIGI server to client", salt);
            return isServer
                ? new PacketCipher(toClient, toServer, sendLabel: 2, receiveLabel: 1)
                : new PacketCipher(toServer, toClient, sendLabel: 1, receiveLabel: 2);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Seals a body under the send key.</summary>
    /// <param name="counter">The packet's full sequence, never reused.</param>
    /// <param name="header">What is authenticated but left readable.</param>
    /// <param name="plaintext">The body.</param>
    /// <param name="destination">Where the ciphertext and then its tag go: <see cref="Overhead"/> bytes more than the body.</param>
    public void Seal(ulong counter, ReadOnlySpan<byte> header, ReadOnlySpan<byte> plaintext, Span<byte> destination)
    {
        Span<byte> nonce = stackalloc byte[12];
        WriteNonce(nonce, _sendLabel, counter);
        _send.Encrypt(nonce, plaintext, destination[..plaintext.Length], destination.Slice(plaintext.Length, TagSize), header);
    }

    /// <summary>Opens a body sealed under the receive key.</summary>
    /// <param name="counter">The packet's full sequence.</param>
    /// <param name="header">What was authenticated with it.</param>
    /// <param name="sealedBody">The ciphertext and its tag.</param>
    /// <param name="destination">Where the plaintext goes: <see cref="Overhead"/> bytes fewer than the sealed body.</param>
    /// <returns><see langword="false"/> when the packet was changed, replayed under another counter, or never sealed by the other end.</returns>
    public bool TryOpen(ulong counter, ReadOnlySpan<byte> header, ReadOnlySpan<byte> sealedBody, Span<byte> destination)
    {
        if (sealedBody.Length < TagSize)
        {
            return false;
        }

        Span<byte> nonce = stackalloc byte[12];
        WriteNonce(nonce, _receiveLabel, counter);
        int length = sealedBody.Length - TagSize;
        try
        {
            _receive.Decrypt(nonce, sealedBody[..length], sealedBody.Slice(length, TagSize), destination[..length], header);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
    }

    /// <summary>Gets the full sequence a received sixteen-bit one stands for: the one nearest the newest seen.</summary>
    /// <param name="newest">The newest full sequence opened, or 0 before any.</param>
    /// <param name="sequence">The sequence the packet's header carries.</param>
    /// <returns>The full sequence.</returns>
    public static ulong Extend(ulong newest, ushort sequence)
    {
        ulong candidate = (newest & ~0xFFFFUL) | sequence;
        if (candidate + 0x8000 < newest)
        {
            candidate += 0x10000;
        }
        else if (candidate > newest + 0x8000 && candidate >= 0x10000)
        {
            candidate -= 0x10000;
        }

        return candidate;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _send.Dispose();
        _receive.Dispose();
    }

    private static void WriteNonce(Span<byte> nonce, uint label, ulong counter)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(nonce, label);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], counter);
    }

    private static byte[] DeriveKey(ECDiffieHellman own, ECDiffieHellmanPublicKey other, string label, byte[] salt)
    {
        byte[] secret = own.DeriveKeyFromHash(other, HashAlgorithmName.SHA256, System.Text.Encoding.ASCII.GetBytes(label), salt);
        return secret.Length == KeySize ? secret : throw new CryptographicException("unexpected key size");
    }

    private static bool ProbeSupport()
    {
        if (OperatingSystem.IsBrowser() || !AesGcm.IsSupported)
        {
            return false;
        }

        try
        {
            using ECDiffieHellman probe = CreateKey();
            return true;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or CryptographicException)
        {
            return false;
        }
    }
}
