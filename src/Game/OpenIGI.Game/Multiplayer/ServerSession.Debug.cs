using System.Globalization;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer;

/// <content>
/// The server's debugging commands: the soldiers listed, a player put before one, one hurt or killed. <b>Temporary: tools for
/// finding what makes the others jump on a client (<see cref="NetProbe"/>), not part of the game.</b>
/// </content>
public sealed partial class ServerSession
{
    /// <summary>The commands <see cref="Debug"/> knows, for a console's help.</summary>
    public static readonly string[] DebugHelp =
    [
        "dbg guards [player]           the soldiers: entity, where, state, health; nearest a player first",
        "dbg tp <player> <guard> [m]   puts a player a few metres from a soldier, facing it (default 8 m)",
        "dbg hurt <guard> <share>      takes a share of a soldier's own health, as a round would",
        "dbg kill <guard>              kills a soldier",
        "  <guard> is a soldier's entity, or nearest:<player> for the living one nearest that player",
    ];

    /// <summary>Runs one of the server's debugging commands, the words after <c>dbg</c>.</summary>
    /// <param name="words">The words.</param>
    /// <returns>What it did, a line each.</returns>
    public IReadOnlyList<string> Debug(IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (LastSnapshot is not { } snapshot)
        {
            return ["No level is being played."];
        }

        string verb = words.Count > 0 ? words[0].ToLowerInvariant() : string.Empty;
        switch (verb)
        {
            case "guards":
                return Guards(snapshot, words.Count > 1 ? words[1] : null);
            case "tp" when words.Count > 2:
            {
                if (PlayerNamed(words[1]) is not { } moved)
                {
                    return [$"No one is called '{words[1]}'."];
                }

                if (GuardOf(snapshot, words[2]) is not { } guard)
                {
                    return [$"No soldier '{words[2]}'."];
                }

                double metres = words.Count > 3 && double.TryParse(words[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double m) ? m : 8;
                return [_world.TeleportBeforeSoldier(moved.Slot, guard.Id, metres) is { } problem
                    ? problem
                    : $"{moved.Name} stands {metres:0.#} m from soldier {guard.Id}, at {Metres(guard.Position)}."];
            }

            case "hurt" when words.Count > 2:
            case "kill" when words.Count > 1:
            {
                if (GuardOf(snapshot, words[1]) is not { } guard)
                {
                    return [$"No soldier '{words[1]}'."];
                }

                float share = verb == "kill" ? 1f
                    : float.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? s : 0f;
                return [_world.HarmSoldier(guard.Id, share) is { } problem
                    ? problem
                    : $"Soldier {guard.Id} took {share:P0} of its health (it had {VitalityText(guard.Health)}) on tick {snapshot.Tick}."];
            }

            default:
                return DebugHelp;
        }
    }

    // The soldiers of the last snapshot, the living first, nearest a player first when one is named.
    private string[] Guards(Snapshot snapshot, string? near)
    {
        Vector3d? from = near is not null && PlayerNamed(near) is { } player && PositionOf(snapshot, EntityOfSlot(player.Slot)) is { } at ? at : null;
        IEnumerable<ReplicatedHuman> soldiers = snapshot.Humans.Where(static h => ReplicatedEntity.IsSoldier(h.Id));
        soldiers = from is { } origin
            ? soldiers.OrderBy(h => (h.Position.ToVector() - origin).Length)
            : soldiers.OrderByDescending(static h => (h.Flags & ReplicatedHumanCondition.Alive) != 0).ThenBy(static h => h.Id);
        string[] lines = [.. soldiers.Select(h =>
            FormattableString.Invariant($"soldier {h.Id}: {((h.Flags & ReplicatedHumanCondition.Alive) != 0 ? "alive" : "dead")} at {Metres(h.Position)}, state {h.State}, health {VitalityText(h.Health)}") +
            (from is { } o ? FormattableString.Invariant($", {(h.Position.ToVector() - o).Length / WorldUnits.PerMetre:0.0} m away") : string.Empty))];
        return lines.Length > 0 ? lines : ["The level has no soldiers in its snapshots."];
    }

    // A soldier by its entity, or the living one nearest a player (nearest:<name>).
    private ReplicatedHuman? GuardOf(Snapshot snapshot, string spec)
    {
        if (spec.StartsWith("nearest:", StringComparison.OrdinalIgnoreCase))
        {
            if (PlayerNamed(spec["nearest:".Length..]) is not { } player || PositionOf(snapshot, EntityOfSlot(player.Slot)) is not { } at)
            {
                return null;
            }

            ReplicatedHuman? best = null;
            double least = double.MaxValue;
            foreach (ReplicatedHuman human in snapshot.Humans)
            {
                double d = (human.Position.ToVector() - at).Length;
                if (ReplicatedEntity.IsSoldier(human.Id) && (human.Flags & ReplicatedHumanCondition.Alive) != 0 && d < least)
                {
                    least = d;
                    best = human;
                }
            }

            return best;
        }

        return int.TryParse(spec, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && snapshot.TryGet(id, out ReplicatedHuman found)
            && ReplicatedEntity.IsSoldier(id)
            ? found
            : null;
    }

    private static Vector3d? PositionOf(Snapshot snapshot, int entity) =>
        entity >= 0 && snapshot.TryGet(entity, out ReplicatedHuman human) ? human.Position.ToVector() : null;

    private static string Metres(Int3 position) => string.Create(
        CultureInfo.InvariantCulture,
        $"({position.X / WorldUnits.PerMetre:0.0}, {position.Y / WorldUnits.PerMetre:0.0}, {position.Z / WorldUnits.PerMetre:0.0}) m");

    private static string VitalityText(byte health) => health == SoldierVitality.Unforeseeable ? "unknown" : $"{health * 100 / 64}%";
}
