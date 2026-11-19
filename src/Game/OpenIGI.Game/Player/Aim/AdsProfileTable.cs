using System.Text.Json;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player.Aim;

/// <summary>The weapons a game can aim down the sights of, and how each does: read from a JSON table.</summary>
/// <remarks>
/// <para>
/// The table has two parts. <c>classes</c> holds the values a kind of weapon shares -- a pistol comes up faster than a machine
/// gun -- and <c>weapons</c> names each aimable weapon by its script id, its class and whatever it has of its own, the sights
/// above all. A weapon the table does not name cannot be aimed, which is how the knife, the scoped rifle, the throwables, the
/// binoculars and the medipack are left as they are.
/// </para>
/// <para>
/// Each game ships its own table as a resource (<see cref="Igi1"/>); a mod may lay its own over it with <see cref="Overlay"/>,
/// entry by entry.
/// </para>
/// </remarks>
public sealed class AdsProfileTable
{
    private const string ResourcePrefix = "OpenIGI.Game.Aim.";

    private static readonly Lazy<AdsProfileTable> Igi1Table = new(() => FromResource("ads.igi1.json"));

    private readonly Dictionary<string, AdsProfile> _profiles;

    private AdsProfileTable(Dictionary<string, AdsProfile> profiles)
    {
        _profiles = profiles;
    }

    /// <summary>Gets Project I.G.I.'s table.</summary>
    public static AdsProfileTable Igi1 => Igi1Table.Value;

    /// <summary>Gets an empty table: nothing can be aimed.</summary>
    public static AdsProfileTable Empty { get; } = new(new Dictionary<string, AdsProfile>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Gets every profile the table holds.</summary>
    public IReadOnlyCollection<AdsProfile> Profiles => _profiles.Values;

    /// <summary>Finds a weapon's profile.</summary>
    /// <param name="scriptId">The weapon's script id.</param>
    /// <param name="profile">The profile, when the weapon can be aimed.</param>
    /// <returns>Whether it can.</returns>
    public bool TryGet(string? scriptId, out AdsProfile profile)
    {
        if (scriptId is not null && _profiles.TryGetValue(scriptId, out AdsProfile? found))
        {
            profile = found;
            return true;
        }

        profile = null!;
        return false;
    }

    /// <summary>Reads a table.</summary>
    /// <param name="json">The table's text.</param>
    /// <returns>The table.</returns>
    /// <exception cref="JsonException">The text is not a table.</exception>
    public static AdsProfileTable Parse(string json) => Empty.Overlay(json);

    /// <summary>Lays another table's entries over this one's: a weapon it names takes its entry, the rest keep theirs.</summary>
    /// <param name="json">The other table's text, whose classes may name this one's.</param>
    /// <returns>The combined table.</returns>
    /// <exception cref="JsonException">The text is not a table.</exception>
    public AdsProfileTable Overlay(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = document.RootElement;

        Dictionary<string, JsonElement> classes = new(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("classes", out JsonElement classList))
        {
            foreach (JsonProperty entry in classList.EnumerateObject())
            {
                classes[entry.Name] = entry.Value;
            }
        }

        Dictionary<string, AdsProfile> profiles = new(_profiles, StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("weapons", out JsonElement weapons))
        {
            foreach (JsonProperty entry in weapons.EnumerateObject())
            {
                AdsProfile profile = new() { ScriptId = entry.Name };
                if (entry.Value.TryGetProperty("class", out JsonElement className))
                {
                    string name = className.GetString() ?? string.Empty;
                    if (!classes.TryGetValue(name, out JsonElement shared))
                    {
                        throw new JsonException($"The weapon '{entry.Name}' names the class '{name}', which the table does not hold.");
                    }

                    profile = Apply(profile, shared);
                }

                profiles[entry.Name] = Apply(profile, entry.Value);
            }
        }

        return new AdsProfileTable(profiles);
    }

    private static AdsProfileTable FromResource(string name)
    {
        using Stream stream = typeof(AdsProfileTable).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new InvalidOperationException($"The aim table '{name}' is not built into the game.");
        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd());
    }

    // The values one entry -- a class or a weapon -- sets, over what the profile had.
    private static AdsProfile Apply(AdsProfile profile, JsonElement entry)
    {
        foreach (JsonProperty value in entry.EnumerateObject())
        {
            profile = value.Name switch
            {
                "class" => profile,
                "kind" => profile with { Kind = Enum.Parse<AdsKind>(value.Value.GetString() ?? nameof(AdsKind.Sights), ignoreCase: true) },
                "rear" => profile with { RearSight = Vector(value.Value) },
                "front" => profile with { FrontSight = Vector(value.Value) },
                "relief" => profile with { EyeReliefMetres = value.Value.GetSingle() },
                "focus" => profile with { FocusOffsetMetres = Vector(value.Value) },
                "in" => profile with { InSeconds = value.Value.GetSingle() },
                "out" => profile with { OutSeconds = value.Value.GetSingle() },
                "zoom" => profile with { Magnification = value.Value.GetSingle() },
                "viewModelTan" => profile with { ViewModelTanX = value.Value.GetSingle() },
                "spread" => profile with { SpreadScale = value.Value.GetSingle() },
                "recoil" => profile with { RecoilScale = value.Value.GetSingle() },
                "move" => profile with { MoveScale = value.Value.GetSingle() },
                "sway" => profile with { SwayScale = value.Value.GetSingle() },
                "steady" => profile with { Steadiness = value.Value.GetSingle() },
                "kick" => profile with { Kick = Kick(value.Value) },
                "aimedKick" => profile with { AimedKickScale = value.Value.GetSingle() },
                "mount" => profile with { CanMount = value.Value.GetBoolean() },
                _ => throw new JsonException($"An aim profile has no setting '{value.Name}'."),
            };
        }

        return profile;
    }

    private static Vector3f Vector(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3)
        {
            throw new JsonException("A point in an aim profile is three numbers, in metres.");
        }

        return new Vector3f(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
    }

    private static AdsKick Kick(JsonElement value) => new(
        value.GetProperty("back").GetSingle(),
        value.GetProperty("up").GetSingle(),
        value.GetProperty("pitch").GetSingle(),
        value.GetProperty("yaw").GetSingle(),
        value.GetProperty("roll").GetSingle());
}
