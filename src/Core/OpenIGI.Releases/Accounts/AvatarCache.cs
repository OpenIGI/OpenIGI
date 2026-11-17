using System.Security.Cryptography;
using System.Text;

namespace OpenIGI.Releases.Accounts;

/// <summary>
/// A signed-in player's Discord avatar, downloaded once into the settings folder the game and the launcher share, as PNG:
/// the format Discord serves every avatar in, its default artwork included (an animated one is asked for as its still).
/// </summary>
public static class AvatarCache
{
    /// <summary>An avatar's address asked for as a PNG still.</summary>
    /// <param name="avatar">The avatar's address, as the account gives it.</param>
    /// <returns>The PNG's address, or <see langword="null"/> for none.</returns>
    public static string? PngUrl(string? avatar)
    {
        if (string.IsNullOrEmpty(avatar) || !Uri.TryCreate(avatar, UriKind.Absolute, out _))
        {
            return null;
        }

        int query = avatar.IndexOf('?', StringComparison.Ordinal);
        string path = query < 0 ? avatar : avatar[..query];
        int dot = path.LastIndexOf('.');
        return dot <= path.LastIndexOf('/') ? null : $"{path[..dot]}.png{(query < 0 ? string.Empty : avatar[query..])}";
    }

    /// <summary>The avatar as a PNG file in a folder, downloaded the first time; named by its own address, so a changed avatar is a new file.</summary>
    /// <param name="http">The client to download through.</param>
    /// <param name="account">The account.</param>
    /// <param name="folder">The folder it is kept in.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The file, or <see langword="null"/> for an account without an avatar's address.</returns>
    public static async Task<string?> FetchAsync(HttpClient http, PlayerAccount account, string folder, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(account);
        if (PngUrl(account.Avatar) is not { } url)
        {
            return null;
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..12];
        string path = Path.Combine(folder, $"avatar-{account.Id}-{hash}.png");
        if (!File.Exists(path))
        {
            byte[] bytes = await http.GetByteArrayAsync(url, cancel).ConfigureAwait(false);
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(path, bytes, cancel).ConfigureAwait(false);
        }

        return path;
    }
}
