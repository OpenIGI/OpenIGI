using System.Globalization;
using System.Text.RegularExpressions;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>
/// A level's switch-worked gates made fit for an arena: each switch works its gate both ways, and a gate whose switches stand
/// all on one side gets one on the other.
/// </summary>
/// <remarks>
/// <para>
/// A campaign gate opens to its switch and never shuts again (its Open door expression names the switch; its Close door
/// expression is empty), and its switch stands on the side the campaign's hero comes from. Both are wrong for two teams: the
/// first press holds a gate open for the match, and only one team can work it.
/// </para>
/// <para>
/// Done to the loaded task tree before anything reads it, on the server and on every player alike, so the switch made for
/// the far side is a task of the level like any other: drawn, solid, offered, pressed and kept in step by the same paths, and
/// in the same place in every end's list of the level's switches. A versus match only; the campaign and co-op keep the
/// level's own gates.
/// </para>
/// </remarks>
public static partial class ArenaGates
{
    /// <summary>How far from its gate a switch is the gate's own, to be matched on the far side: a remote switch is not.</summary>
    public const double SwitchReach = 6 * WorldUnits.PerMetre;

    /// <summary>A switch nearer the gate's line than this stands on neither side of it.</summary>
    public const double SideMargin = 0.1 * WorldUnits.PerMetre;

    /// <summary>A stand nearer its switch than this, across the ground, is the switch's own post, mirrored with it.</summary>
    public const double StandReach = 1.5 * WorldUnits.PerMetre;

    private const string DoorType = "Door";
    private const string SwitchType = "Switch";
    private const string OpenExpression = "Open door expression";
    private const string CloseExpression = "Close door expression";

    /// <summary>What was done to a level's gates.</summary>
    /// <param name="Toggles">The doors whose switches now shut them as well.</param>
    /// <param name="Mirrored">The switches made on the far side of their gates, by the ids of the switches they mirror.</param>
    public readonly record struct Report(int Toggles, IReadOnlyList<(int Of, int Made)> Mirrored);

    /// <summary>Makes a level's switch-worked gates fit for an arena.</summary>
    /// <param name="root">The level's root task, as its script left it.</param>
    /// <param name="mirror">Whether to make the switches a gate lacks on its far side; a level whose scene is already walked
    /// cannot take a new one.</param>
    /// <returns>What was done.</returns>
    public static Report Prepare(GameTask root, bool mirror = true)
    {
        ArgumentNullException.ThrowIfNull(root);
        TaskTree tree = root.Tree;
        List<DeclaredTask> doors = [.. root.Descendants().OfType<DeclaredTask>().Where(static t => t.Type.Name == DoorType)];

        // Each door the switches open, and the switches; doors sharing a switch are one gate.
        Dictionary<DeclaredTask, List<DeclaredTask>> switchesOf = new(ReferenceEqualityComparer.Instance);
        foreach (DeclaredTask door in doors)
        {
            List<DeclaredTask> switches = [.. SwitchIds(Text(door, OpenExpression))
                .Select(tree.FromId)
                .OfType<DeclaredTask>()
                .Where(static s => s.Type.Name == SwitchType)
                .Distinct()];
            if (switches.Count > 0)
            {
                switchesOf[door] = switches;
            }
        }

        List<(int Of, int Made)> mirrored = [];
        foreach (List<DeclaredTask> gate in Gates(switchesOf))
        {
            List<DeclaredTask> switches = [.. gate.SelectMany(door => switchesOf[door]).Distinct()];
            if (mirror && FarSideSwitches(gate, switches) is { } made)
            {
                foreach ((DeclaredTask of, DeclaredTask copy) in made)
                {
                    mirrored.Add((of.Id, copy.Id));
                    switches.Add(copy);
                }

                // The doors open to the new switches as to their own.
                foreach (DeclaredTask door in gate)
                {
                    string opens = Text(door, OpenExpression);
                    door.TrySetScriptValue(
                        OpenExpression,
                        $"({opens}) || {Pressed(made.Select(m => m.Copy))}");
                }
            }

            foreach (DeclaredTask door in gate)
            {
                ShutToSwitches(door, switches);
            }
        }

        return new Report(switchesOf.Count, mirrored);
    }

    // A door stood open shuts to a press of any of its switches, unless its level already shuts it to every one of them.
    private static void ShutToSwitches(DeclaredTask door, IReadOnlyList<DeclaredTask> switches)
    {
        string closes = Text(door, CloseExpression);
        HashSet<int> named = [.. SwitchIds(closes)];
        if (switches.All(s => named.Contains(s.Id)))
        {
            return;
        }

        // Asked only while the door stands fully open, so the press that opened it, read as it starts to move, cannot shut it.
        string pressed = Pressed(switches);
        door.TrySetScriptValue(CloseExpression, string.IsNullOrWhiteSpace(closes) ? pressed : $"({closes}) || {pressed}");
    }

    // The gates: doors joined by a switch they share.
    private static List<List<DeclaredTask>> Gates(Dictionary<DeclaredTask, List<DeclaredTask>> switchesOf)
    {
        List<List<DeclaredTask>> gates = [];
        foreach ((DeclaredTask door, List<DeclaredTask> switches) in switchesOf)
        {
            List<List<DeclaredTask>> joined = [.. gates.Where(g => g.Any(other => switchesOf[other].Intersect(switches).Any()))];
            List<DeclaredTask> gate = [door];
            foreach (List<DeclaredTask> other in joined)
            {
                gate.AddRange(other);
                gates.Remove(other);
            }

            gates.Add(gate);
        }

        return gates;
    }

    // For a gate whose own switches all stand on one side of it, a copy of each on the other, facing away from the gate there
    // as the switch does on its side; null when the gate needs none or its line cannot be told.
    private static List<(DeclaredTask Of, DeclaredTask Copy)>? FarSideSwitches(List<DeclaredTask> gate, List<DeclaredTask> switches)
    {
        // The gate's line: through its first two doors (a pair of leaves, or a telescoping run).
        List<Vector3d> leaves = [.. gate.Select(d => Position(d, "Position start")).OfType<Vector3d>()];
        if (leaves.Count < 2)
        {
            return null;
        }

        Vector3d along = Flat(leaves[1] - leaves[0]);
        if (along.Length < SideMargin)
        {
            return null;
        }

        along /= along.Length;
        Vector3d normal = new(-along.Y, along.X, 0);
        Vector3d origin = leaves[0];

        // The gate's own switches: those beside it, not a switch worked from a room across the level.
        List<(DeclaredTask Switch, double Side)> own = [];
        foreach (DeclaredTask s in switches)
        {
            if (Position(s, "Position") is not { } at || leaves.Min(l => Flat(at - l).Length) > SwitchReach)
            {
                continue;
            }

            own.Add((s, Vector3d.Dot(Flat(at - origin), normal)));
        }

        if (own.Count == 0 || own.Any(o => Math.Abs(o.Side) < SideMargin)
            || (own.Any(o => o.Side > 0) && own.Any(o => o.Side < 0)))
        {
            return null;
        }

        List<(DeclaredTask, DeclaredTask)> made = [];
        foreach ((DeclaredTask s, _) in own)
        {
            if (Mirror(s, origin, normal) is { } copy)
            {
                made.Add((s, copy));
            }
        }

        return made.Count > 0 ? made : null;
    }

    // The switch made again on the far side, on a mirror of its own post when it stands on one.
    private static DeclaredTask? Mirror(DeclaredTask original, Vector3d origin, Vector3d normal)
    {
        TaskTree tree = original.Tree;
        if (original.Parent is not { } parent || FreeId(tree) is not { } id)
        {
            return null;
        }

        // A post the switch is mounted on (an EditRigidObj it is the child of, standing just by it): mirrored too, its own
        // parent's child, and the switch made on it.
        GameTask under = parent;
        if (parent is DeclaredTask { Type.Name: "EditRigidObj" } stand
            && stand.Parent is { } standParent
            && Position(stand, "Position") is { } standAt
            && Position(original, "Position") is { } switchAt
            && Flat(standAt - switchAt).Length <= StandReach)
        {
            DeclaredTask post = stand.CopyUnder(standParent, -1);
            if (Place(post, stand, origin, normal))
            {
                under = post;
            }
        }

        DeclaredTask copy = original.CopyUnder(under, id);
        return Place(copy, original, origin, normal) ? copy : null;
    }

    // A copy put where the original stands mirrored across the gate's line, turned as the mirror turns its facing.
    private static bool Place(DeclaredTask copy, DeclaredTask original, Vector3d origin, Vector3d normal)
    {
        string positionName = original.Type.Name == DoorType ? "Position start" : "Position";
        if (Position(original, positionName) is not { } at
            || !original.TryGetValue("Orientation", out object? angles) || angles is not Vector3f euler
            || Math.Abs(euler.X) > 1e-4f || Math.Abs(euler.Y) > 1e-4f)
        {
            // Only an upright thing is mirrored by turning it; a tilted one is left out rather than put in wrong.
            copy.Tree.Delete(copy);
            return false;
        }

        double side = Vector3d.Dot(Flat(at - origin), normal);
        copy.TrySetScriptValue(positionName, at - (normal * (2 * side)));
        copy.TrySetScriptValue("Orientation", new Vector3f(euler.X, euler.Y, MirroredGamma(euler.Z, normal)));
        return true;
    }

    /// <summary>The turn about the vertical that faces a thing as its mirror across a vertical plane faces.</summary>
    /// <param name="gamma">Its turn, in radians.</param>
    /// <param name="normal">The plane's normal, across the ground.</param>
    /// <returns>The mirror's turn.</returns>
    public static float MirroredGamma(float gamma, Vector3d normal)
    {
        Vector3d facing = Facing(gamma);
        Vector3d mirrored = facing - (normal * (2 * Vector3d.Dot(facing, normal)));

        // Which way a larger turn faces, in the scene's own frame, so the answer is right whichever way it turns.
        Vector3d ahead = Facing(gamma + 0.5f);
        double sense = Math.Sign((facing.X * ahead.Y) - (facing.Y * ahead.X));
        double turn = Math.Atan2((facing.X * mirrored.Y) - (facing.Y * mirrored.X), Vector3d.Dot(facing, mirrored));
        double result = gamma + (sense * turn);
        return (float)(((result % Math.Tau) + Math.Tau) % Math.Tau);
    }

    // Which way a thing turned this far faces across the ground: its frame's front, as a switch offers itself from.
    private static Vector3d Facing(float gamma)
    {
        Matrix3f m = WorldScene.ToOrientation(new Vector3f(0f, 0f, gamma));
        return new Vector3d(m.M12, m.M22, 0);
    }

    // The highest id nothing is bound to: one no level uses, the same on every end for the same level.
    private static int? FreeId(TaskTree tree)
    {
        for (int id = TaskTree.IdCapacity - 1; id > 0; id--)
        {
            if (tree.FromId(id) is null)
            {
                return id;
            }
        }

        return null;
    }

    private static string Pressed(IEnumerable<DeclaredTask> switches) =>
        string.Join(" || ", switches.Select(static s => string.Create(CultureInfo.InvariantCulture, $"Switch_{s.Id}.isLastPressed")));

    private static IEnumerable<int> SwitchIds(string expression) =>
        SwitchPress().Matches(expression).Select(static m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));

    private static string Text(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;

    private static Vector3d? Position(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is Vector3d at ? at : null;

    private static Vector3d Flat(Vector3d v) => new(v.X, v.Y, 0);

    [GeneratedRegex(@"\bSwitch_(\d+)\.is(?:Last)?Pressed\b")]
    private static partial Regex SwitchPress();
}
