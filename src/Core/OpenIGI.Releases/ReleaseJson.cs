using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIGI.Releases;

/// <summary>The JSON shape of every release document: camelCase, enums as lowercase words.</summary>
/// <remarks>Source-generated, so the trimmed and ahead-of-time compiled programs that read it need no reflection.</remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ReleaseManifest))]
[JsonSerializable(typeof(ChannelFeed))]
public sealed partial class ReleaseJsonContext : JsonSerializerContext;

/// <summary>Reads and writes release documents.</summary>
public static class ReleaseJson
{
    /// <summary>Serializes a manifest.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <returns>Its JSON.</returns>
    public static string Write(ReleaseManifest manifest) =>
        JsonSerializer.Serialize(manifest, ReleaseJsonContext.Default.ReleaseManifest);

    /// <summary>Deserializes a manifest.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="JsonException">The JSON is not a manifest.</exception>
    public static ReleaseManifest ReadManifest(string json) =>
        JsonSerializer.Deserialize(json, ReleaseJsonContext.Default.ReleaseManifest)
            ?? throw new JsonException("The release manifest is empty.");

    /// <summary>Serializes a channel feed.</summary>
    /// <param name="feed">The feed.</param>
    /// <returns>Its JSON.</returns>
    public static string Write(ChannelFeed feed) =>
        JsonSerializer.Serialize(feed, ReleaseJsonContext.Default.ChannelFeed);

    /// <summary>Deserializes a channel feed.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The feed.</returns>
    /// <exception cref="JsonException">The JSON is not a feed.</exception>
    public static ChannelFeed ReadFeed(string json) =>
        JsonSerializer.Deserialize(json, ReleaseJsonContext.Default.ChannelFeed)
            ?? throw new JsonException("The channel feed is empty.");

    /// <summary>The wire name of a channel: <c>stable</c> or <c>beta</c>.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>Its name.</returns>
    public static string Name(this ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Stable => "stable",
        ReleaseChannel.Beta => "beta",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
    };

    /// <summary>Parses a channel's wire name.</summary>
    /// <param name="name">The name, in any case.</param>
    /// <param name="channel">The channel, when the name is one.</param>
    /// <returns>Whether the name is a channel.</returns>
    public static bool TryParseChannel(string? name, out ReleaseChannel channel)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "stable":
                channel = ReleaseChannel.Stable;
                return true;
            case "beta":
                channel = ReleaseChannel.Beta;
                return true;
            default:
                channel = default;
                return false;
        }
    }
}
