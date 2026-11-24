namespace OpenIGI.Game.Replays;

/// <summary>
/// What a replay is of, and everything the level was started from besides the inputs: the build, the game and mission, the
/// difficulty, the values of every setting that changes how the level plays, and the files it played from.
/// </summary>
/// <param name="GameVersion">The build that recorded it; another build may simulate differently and is not asked to play it back.</param>
/// <param name="Title">The game, by title id (<c>igi1</c>, <c>igi2</c>).</param>
/// <param name="Mission">The mission.</param>
/// <param name="Difficulty">The difficulty it was played at: nought easy to two hard.</param>
/// <param name="Ruleset">The rules its settings make (<see cref="Ruleset"/>), by id.</param>
/// <param name="Settings">
/// Every setting flagged as changing the simulation (<c>CVarOptions.Simulation</c>), by name with its value as the console writes
/// it, in name order: what a replay sets before it plays and puts back after.
/// </param>
/// <param name="ContentFingerprint">The SHA-256 of the list of files the level played from (<see cref="ContentFingerprint"/>), in hex.</param>
/// <param name="Player">Who played it, as the player is known on the boards: an install key's id; empty for nobody known.</param>
/// <param name="PlayerName">The name the player plays under.</param>
/// <param name="RecordedAt">When it began, in milliseconds since 1970 (UTC).</param>
public sealed record ReplayHeader(
    string GameVersion,
    string Title,
    int Mission,
    int Difficulty,
    string Ruleset,
    IReadOnlyList<(string Name, string Value)> Settings,
    string ContentFingerprint,
    string Player,
    string PlayerName,
    long RecordedAt)
{
    /// <summary>The longest name or value a header keeps, in characters.</summary>
    public const int MaxText = 128;

    /// <summary>The most settings a header keeps.</summary>
    public const int MaxSettings = 512;

    /// <summary>Writes the header.</summary>
    /// <param name="writer">Where it goes.</param>
    public void Write(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ReplayBinary.WriteText(writer, GameVersion, MaxText);
        ReplayBinary.WriteText(writer, Title, MaxText);
        writer.Write(Mission);
        writer.Write(Difficulty);
        ReplayBinary.WriteText(writer, Ruleset, MaxText);
        writer.Write7BitEncodedInt(Math.Min(Settings.Count, MaxSettings));
        foreach ((string name, string value) in Settings.Take(MaxSettings))
        {
            ReplayBinary.WriteText(writer, name, MaxText);
            ReplayBinary.WriteText(writer, value, MaxText);
        }

        ReplayBinary.WriteText(writer, ContentFingerprint, MaxText);
        ReplayBinary.WriteText(writer, Player, MaxText);
        ReplayBinary.WriteText(writer, PlayerName, MaxText);
        writer.Write(RecordedAt);
    }

    /// <summary>Reads a header.</summary>
    /// <param name="reader">Where it comes from.</param>
    /// <returns>The header.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a header.</exception>
    public static ReplayHeader Read(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string version = ReplayBinary.ReadText(reader, MaxText);
        string title = ReplayBinary.ReadText(reader, MaxText);
        int mission = reader.ReadInt32();
        int difficulty = reader.ReadInt32();
        string ruleset = ReplayBinary.ReadText(reader, MaxText);
        int count = reader.Read7BitEncodedInt();
        if (count < 0 || count > MaxSettings)
        {
            throw new InvalidDataException($"A header of {count} settings.");
        }

        List<(string, string)> settings = new(count);
        for (int i = 0; i < count; i++)
        {
            settings.Add((ReplayBinary.ReadText(reader, MaxText), ReplayBinary.ReadText(reader, MaxText)));
        }

        string content = ReplayBinary.ReadText(reader, MaxText);
        string player = ReplayBinary.ReadText(reader, MaxText);
        string name = ReplayBinary.ReadText(reader, MaxText);
        long at = reader.ReadInt64();
        if (difficulty is < 0 or > 2 || mission < 0)
        {
            throw new InvalidDataException("A header's mission or difficulty is not a game's.");
        }

        return new ReplayHeader(version, title, mission, difficulty, ruleset, settings, content, player, name, at);
    }

    /// <summary>A setting's value as the header has it.</summary>
    /// <param name="name">The setting's name.</param>
    /// <returns>The value, or <see langword="null"/> where the header does not name it.</returns>
    public string? Setting(string name)
    {
        foreach ((string key, string value) in Settings)
        {
            if (string.Equals(key, name, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return null;
    }
}
