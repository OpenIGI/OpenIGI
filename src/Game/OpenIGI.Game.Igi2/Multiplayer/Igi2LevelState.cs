using OpenIGI.Game.Igi2.Pickups;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>
/// An I.G.I. 2 level's running state as a co-op server shares it: its script variables (<see cref="ScriptVariableMirror"/>),
/// which every script of the level reads -- its flow's verdicts and clock, its timers, alarms, doors, switches, terminals,
/// areas, objectives and the documents taken -- and which of the weapons the level lays out have been taken.
/// </summary>
/// <remarks>
/// The players' and soldiers' own variables stay each machine's: the snapshots carry the bodies, and a client's copy of a
/// player or a soldier is the server's puppet.
/// </remarks>
internal static class Igi2LevelState
{
    /// <summary>The level's ticks in a second, which a lift's speed is carried in.</summary>
    public const int TicksPerSecond = 30;

    // The task types whose variables each machine keeps for itself: a body's -- the player's, its input's, each soldier's and
    // each soldier's mind's -- which the snapshots carry; and a lift's and a car's, which their own travel and wreck write, and
    // which the snapshots keep to the server's (Igi2LiftTask.Follow, Igi2CarTask.Hold).
    private static readonly string[] OwnPrefixes = ["HumanPlayer_", "HumanPlayerInput_", "HumanSoldier_", "HumanAI_", "Lift_", "Car_"];

    /// <summary>Gets whether a variable is the level's to share.</summary>
    /// <param name="name">Its name, <c>Task_id.field</c>.</param>
    /// <returns><see langword="true"/> unless it is a body's, a lift's or a car's own.</returns>
    public static bool IsShared(string name) => !Array.Exists(OwnPrefixes, prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Makes the mirror of a level's variables, or <see langword="null"/> for a level with no scripts of its own.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The mirror.</returns>
    public static ScriptVariableMirror? MirrorOf(Igi2TitlePlayer level) =>
        level.Level is { } run ? new ScriptVariableMirror(run.Natives, IsShared) : null;

    /// <summary>Writes the state, on the server.</summary>
    /// <param name="guns">The weapons the level lays out (<see cref="LaidOutGuns"/>).</param>
    /// <param name="mirror">The level's mirror, or <see langword="null"/>.</param>
    /// <param name="tick">The tick.</param>
    /// <param name="blasts">The explosions lately set off.</param>
    /// <returns>The state: never nothing, so a client knows the server's word has come.</returns>
    public static byte[] Capture(Igi2GunPickupTask[] guns, ScriptVariableMirror? mirror, long tick, Igi2BlastLog blasts)
    {
        BitWriter writer = new(1024);
        writer.WriteBool(mirror is not null);
        if (mirror is not null)
        {
            writer.WriteBytes(mirror.Capture(tick));
        }

        writer.WriteVarUInt((uint)guns.Length);
        foreach (Igi2GunPickupTask gun in guns)
        {
            writer.WriteBool(gun.Pickup is not { IsDelete: false });
        }

        blasts.Write(writer);
        return writer.AsSpan().ToArray();
    }

    /// <summary>Takes the state, on a client.</summary>
    /// <param name="guns">The weapons the level lays out (<see cref="LaidOutGuns"/>).</param>
    /// <param name="mirror">The level's mirror, or <see langword="null"/>.</param>
    /// <param name="state">The state, as <see cref="Capture"/> wrote it.</param>
    /// <param name="blasts">The explosions shown so far.</param>
    /// <param name="ownEntity">This client's own entity, or <see langword="null"/>.</param>
    /// <param name="explosions">Where explosions go off here, or <see langword="null"/>.</param>
    /// <remarks>
    /// A weapon another player took is taken out of this level, nobody's: this player's own pickups go by its own copy's
    /// walking over them, which the server's copy does too.
    /// </remarks>
    public static void Apply(Igi2GunPickupTask[] guns, ScriptVariableMirror? mirror, byte[] state, Igi2BlastLog blasts, int? ownEntity, Weapons.Igi2ExplosionWorld? explosions)
    {
        ArgumentNullException.ThrowIfNull(state);
        BitReader reader = new(state);
        if (reader.ReadBool())
        {
            byte[] variables = reader.ReadBytes(1 << 20);
            if (mirror is not null && variables.Length > 0)
            {
                BitReader inner = new(variables);
                mirror.Apply(ref inner);
            }
        }

        uint count = reader.ReadVarUInt();
        for (int i = 0; i < count && !reader.IsOverflowed; i++)
        {
            if (reader.ReadBool() && i < guns.Length && guns[i].Pickup is { IsDelete: false } lying)
            {
                lying.TakenElsewhere();
            }
        }

        if (!reader.IsOverflowed)
        {
            blasts.Show(ref reader, ownEntity, explosions);
        }
    }

    /// <summary>Gets the weapons the level lays out, in its tree's order, which every machine playing it shares.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The weapons' tasks.</returns>
    public static Igi2GunPickupTask[] LaidOutGuns(Igi2TitlePlayer level) => [.. level.LevelTasks().OfType<Igi2GunPickupTask>()];
}
