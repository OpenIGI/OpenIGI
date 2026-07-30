namespace OpenIGI.Game.Player;

/// <summary>
/// What happens to the player when the last round lands — locomotion state <b>47</b>, entered on the
/// same flag the soldier's death state is.
/// </summary>
/// <remarks>
/// <para>
/// The state's entry is <c>0x40E560</c>, and the <c>"player_death_"</c> string in it is what names it.
/// It does seven things, in this order:
/// </para>
/// <code>
/// human[829] = 0;                                    // stand up out of any crouch
/// row  = sub_489E10(human + 596, human[744]);        // the hit-zone row for the LAST ZONE STRUCK
/// clip = row[8 + 4*rand(2)];                         // ReactionA or ReactionB
/// sub_4D61D0(human + 104, 0, clip, 0, 0.2);          // play it on the whole body, blend 0.2 s
/// ... stop the weapon, drop the view, release the aim ...
/// sub_4638A0(human, "player_death_", 3);             // one of three death sounds
/// human[1636..] = human[1672..];                     // velocity from the committed move
/// </code>
/// <para>
/// <b>It is the same rule the soldier's general death state uses.</b> <c>0x45C440</c> reads
/// <c>row + 4*rand(2) + 8</c> for a head or neck hit; the player reads it for <i>whatever</i> zone was
/// struck. So a player shot in the leg falls differently from one shot in the chest, and the pair of
/// clips per zone is what the hit-zone table's trailing two integers are for
/// (<see cref="HumanHitZone.ReactionA"/>).
/// </para>
/// <para>
/// The retail player table's reactions are <c>201</c>/<c>202</c> for the limbs and torso, <c>215</c>
/// for the rear, <c>112</c> for the head and <c>204</c> for the neck — so five distinct ways to go
/// down, chosen by where the round landed.
/// </para>
/// </remarks>
public static class HumanPlayerDeath
{
    /// <summary>The locomotion state the player dies into.</summary>
    /// <remarks>
    /// Forty-seven, and one of the nine the state-to-animation table marks self-driven — it names its
    /// clip itself rather than taking one from the table, which is precisely what choosing between two
    /// reactions requires.
    /// </remarks>
    public const int State = 47;

    /// <summary>How long the death clip blends in, in seconds.</summary>
    /// <remarks>The literal fifth argument of the <c>0x4D61D0</c> call — <c>0.2</c>, not the soldier's zero.</remarks>
    public const float BlendSeconds = 0.2f;

    /// <summary>The sound prefix, and how many variants there are.</summary>
    public const string SoundPrefix = "player_death_";

    /// <summary>How many <see cref="SoundPrefix"/> variants the state picks between.</summary>
    public const int SoundVariants = 3;

    /// <summary>Picks the clip the player falls with.</summary>
    /// <param name="zone">The zone the killing round struck — <c>Human+744</c>.</param>
    /// <param name="table">The player's hit-zone table.</param>
    /// <param name="flip">The engine's <c>rand(2)</c>: 0 takes the first reaction, 1 the second.</param>
    /// <returns>The animation id, or <c>-1</c> when the zone is outside the table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The zone is read from the human rather than passed, because the damage handler stores the last
    /// zone struck there — so a player who took a leg hit and then a fatal chest hit falls the chest
    /// way. Reproducing that means remembering the <i>last</i> zone, not the worst one.
    /// </remarks>
    public static int Choose(int zone, IReadOnlyList<HumanHitZone> table, int flip)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (zone < 0 || zone >= table.Count)
        {
            return -1;
        }

        return (flip & 1) == 0 ? table[zone].ReactionA : table[zone].ReactionB;
    }
}
