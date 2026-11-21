using System.Globalization;

namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// A signed-in player's Discord avatar as a session passes it on: a short reference -- <c>&lt;user id&gt;/&lt;hash&gt;</c>, or
/// <c>embed/&lt;n&gt;</c> for Discord's own default artwork -- that names a picture on Discord's own content server and nothing else.
/// </summary>
/// <remarks>
/// Every other player downloads what a reference names, so a reference is never an address a player wrote: it is read only as a
/// user id of digits and a hash of hex, and made back into an address on <see cref="Host"/> here. A player cannot have the others
/// fetch from anywhere else, nor anything but a small PNG.
/// </remarks>
public static class DiscordAvatar
{
    /// <summary>The only server an avatar is fetched from.</summary>
    public const string Host = "cdn.discordapp.com";

    /// <summary>The longest a reference may be, in bytes: a user id of twenty digits, a slash, an animated hash of thirty-four.</summary>
    public const int MaxReferenceBytes = 64;

    // The size asked for: as large as the board ever draws one.
    private const int Size = 64;

    /// <summary>The reference to an account's avatar.</summary>
    /// <param name="avatar">The avatar's address, as the account gives it; or <see langword="null"/>.</param>
    /// <returns>The reference, or empty where the address is not a Discord avatar's.</returns>
    public static string Reference(string? avatar)
    {
        if (string.IsNullOrEmpty(avatar) || !Uri.TryCreate(avatar, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string[] parts = uri.AbsolutePath.Trim('/').Split('/');
        static string Stem(string file) => file.Contains('.', StringComparison.Ordinal) ? file[..file.LastIndexOf('.')] : file;
        string reference = parts switch
        {
            ["avatars", string user, string file] => $"{user}/{Stem(file)}",
            ["embed", "avatars", string file] => $"embed/{Stem(file)}",
            _ => string.Empty,
        };
        return Url(reference) is null ? string.Empty : reference;
    }

    /// <summary>The address a reference names: a PNG on Discord's content server, or <see langword="null"/> for a reference that is not one.</summary>
    /// <param name="reference">The reference, as a session passed it on.</param>
    /// <returns>The address, or <see langword="null"/>.</returns>
    public static string? Url(string? reference)
    {
        if (string.IsNullOrEmpty(reference) || reference.Length > MaxReferenceBytes)
        {
            return null;
        }

        string[] parts = reference.Split('/');
        if (parts is ["embed", string index] && index.Length is > 0 and <= 2 && index.All(char.IsAsciiDigit))
        {
            return string.Create(CultureInfo.InvariantCulture, $"https://{Host}/embed/avatars/{index}.png");
        }

        if (parts is [string user, string hash] && user.Length is > 0 and <= 20 && user.All(char.IsAsciiDigit) && IsHash(hash))
        {
            // An animated avatar (a_ and its hash) is asked for as its still, the format every avatar comes in.
            return string.Create(CultureInfo.InvariantCulture, $"https://{Host}/avatars/{user}/{hash}.png?size={Size}");
        }

        return null;
    }

    // A Discord avatar hash: 32 hex digits, after a_ for an animated one.
    private static bool IsHash(string hash)
    {
        string digits = hash.StartsWith("a_", StringComparison.Ordinal) ? hash[2..] : hash;
        return digits.Length == 32 && digits.All(char.IsAsciiHexDigitLower);
    }
}
