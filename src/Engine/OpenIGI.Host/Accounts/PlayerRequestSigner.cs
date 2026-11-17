using OpenIGI.Net.Identity;
using OpenIGI.Releases;

namespace OpenIGI.Host.Accounts;

/// <summary>
/// The key a player plays under (<see cref="IPlayerIdentity"/>), signing requests to the master server's accounts: the same
/// key game servers know the player by, so an account linked with it names that player everywhere they play.
/// </summary>
/// <param name="identity">The player's identity; it must be <see cref="IPlayerIdentity.IsReady">ready</see> before a request is signed.</param>
public sealed class PlayerRequestSigner(IPlayerIdentity identity) : IRequestSigner
{
    /// <summary>Gets the key's id, as servers and the master name the player.</summary>
    public string PlayerId => PlayerIdentities.IdOf(Ready().PublicKey);

    /// <inheritdoc/>
    public string PublicKey => Convert.ToBase64String(Ready().PublicKey);

    /// <inheritdoc/>
    public Task<byte[]> SignAsync(byte[] text) => Ready().SignAsync(text);

    private IPlayerIdentity Ready() =>
        identity.IsReady ? identity : throw new InvalidOperationException("The player's key isn't loaded yet.");
}
