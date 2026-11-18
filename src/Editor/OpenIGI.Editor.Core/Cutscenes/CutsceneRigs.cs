using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;

namespace OpenIGI.Editor.Core.Cutscenes;

/// <summary>What a camera rig is: a rail, a crane or a dolly track, as film crews and animation tools have them.</summary>
public enum CutsceneRigKind
{
    /// <summary>Along a path laid on the ground, at one speed; watching a subject when it has one, else looking along the way.</summary>
    Rail,

    /// <summary>An arm swung round a subject, rising as it swings, the camera kept on the subject.</summary>
    Crane,

    /// <summary>Round a subject at one height and distance.</summary>
    Orbit,
}

/// <summary>
/// A camera rig's settings: what makes its keys. The keys are plain <c>EditCamera</c> knots the game reads, each labelled with the
/// rig's name; the settings are the editor's own (kept beside the mission, never in it), so the rig can be changed and its keys
/// made again.
/// </summary>
/// <param name="Name">Its name, unique in its scene, which its keys carry as their label.</param>
/// <param name="Kind">What it is.</param>
/// <param name="SceneId">The scene it is in.</param>
/// <param name="SubjectId">What it watches or goes round, or -1.</param>
/// <param name="Points">A rail's path, in world metres on the ground.</param>
/// <param name="Seconds">How long it runs, in scene seconds.</param>
/// <param name="Radius">A crane's reach or an orbit's distance, in metres.</param>
/// <param name="Height">A rail's height over its path, a crane's rise, an orbit's height over its subject, in metres.</param>
/// <param name="Sweep">How far a crane or orbit swings round, in degrees.</param>
/// <param name="Heading">Where a crane or orbit starts, as a turn about the up axis from +X, in degrees.</param>
/// <param name="Fingerprint">The keys it last made, so a hand edit of them is noticed.</param>
public sealed record CutsceneRig(
    string Name,
    CutsceneRigKind Kind,
    int SceneId,
    int SubjectId,
    IReadOnlyList<Vector3d> Points,
    double Seconds,
    double Radius,
    double Height,
    double Sweep,
    double Heading,
    string Fingerprint);

/// <summary>One key a rig makes: where it stands, its turn, how long it holds.</summary>
/// <param name="PositionUnits">Where, in world units.</param>
/// <param name="Angles">Alpha, beta, gamma, in radians.</param>
/// <param name="Seconds">How long until the next, in scene seconds.</param>
public readonly record struct CutsceneRigKey(Vector3d PositionUnits, Vector3f Angles, double Seconds);

/// <summary>Camera rigs baked into a scene's keys, and their settings kept as the editor's sidecar.</summary>
public static class CutsceneRigs
{
    private const double RailSpacing = 6;
    private const double LookAhead = 8;

    /// <summary>The keys a rig makes, in order.</summary>
    /// <param name="rig">The rig.</param>
    /// <param name="subject">Where its subject stands, in metres; null for none.</param>
    /// <returns>The keys; empty where it has too little to make any (a rail of fewer than two points, a crane with no subject).</returns>
    public static IReadOnlyList<CutsceneRigKey> Bake(CutsceneRig rig, Vector3d? subject)
    {
        ArgumentNullException.ThrowIfNull(rig);
        List<(Vector3d At, Vector3d Look)> poses = rig.Kind switch
        {
            CutsceneRigKind.Rail => Rail(rig, subject),
            _ when subject is { } centre => Swing(rig, centre),
            _ => [],
        };
        if (poses.Count < 2)
        {
            return [];
        }

        Vector3f[] turns = [.. poses.Select(static p => CutsceneCameraMath.LookAt(p.At * WorldUnits.PerMetre, p.Look * WorldUnits.PerMetre).ToEngineEulerAngles())];
        double[] holds = Even([.. poses.Select(static p => p.At * WorldUnits.PerMetre)], rig.Seconds);
        return [.. poses.Select((p, i) => new CutsceneRigKey(p.At * WorldUnits.PerMetre, turns[i], holds[i]))];
    }

    // How long each key holds so the camera flies the game's own curve through them at one speed: each span's time its share of
    // the way as the curve runs it (a bend is cut shorter than the straight between its keys), found again until it settles.
    private static double[] Even(Vector3d[] places, double seconds)
    {
        int spans = places.Length - 1;
        double[] holds = new double[places.Length];
        for (int i = 0; i < spans; i++)
        {
            holds[i] = seconds / spans;
        }

        for (int pass = 0; pass < 6; pass++)
        {
            CutsceneKey[] path = [.. places.Select((p, i) => new CutsceneKey(p, 0, 0, 0, 1, (float)holds[i], true, -1, 0, 0))];
            double[] way = new double[spans];
            double start = 0;
            for (int i = 0; i < spans; i++)
            {
                Vector3d last = places[i];
                for (int s = 1; s <= 24; s++)
                {
                    Vector3d at = CutsceneCameraMath.Evaluate(path, 1, start + (holds[i] * s / 24), -1f).Position;
                    way[i] += (at - last).Length;
                    last = at;
                }

                start += holds[i];
            }

            double total = way.Sum();
            if (total <= 0)
            {
                break;
            }

            for (int i = 0; i < spans; i++)
            {
                holds[i] = Math.Round(seconds * way[i] / total, 3);
            }
        }

        holds[spans] = 0;
        return holds;
    }

    /// <summary>A rig's keys in the scene: the run of knots carrying its name.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="name">The rig's name.</param>
    /// <returns>The keys, in order.</returns>
    public static IReadOnlyList<MissionTask> KeysOf(MissionTask scene, string name) =>
        [.. CutsceneModel.Shots(scene).Select(static s => s.Camera).Where(c => c.Label == name)];

    /// <summary>What a rig's keys are now, to tell whether they are still as it made them.</summary>
    /// <param name="keys">The keys.</param>
    /// <returns>The fingerprint.</returns>
    public static string FingerprintOf(IReadOnlyList<MissionTask> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        StringBuilder text = new();
        foreach (MissionTask key in keys)
        {
            CutsceneKey k = CutsceneMoves.KeyOf(key);
            text.Append(CultureInfo.InvariantCulture, $"{k.Position.X:0.0},{k.Position.Y:0.0},{k.Position.Z:0.0},{k.Alpha:0.0000},{k.Beta:0.0000},{k.Gamma:0.0000},{k.FieldOfView:0.000},{k.Duration:0.000};");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    /// <summary>Gets whether a rig's keys are still as it made them, so making them again loses nothing done by hand.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="rig">The rig.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    public static bool IsIntact(MissionTask scene, CutsceneRig rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        IReadOnlyList<MissionTask> keys = KeysOf(scene, rig.Name);
        return keys.Count > 0 && FingerprintOf(keys) == rig.Fingerprint;
    }

    /// <summary>
    /// The step that puts a rig's keys in the scene: its old keys taken out, the new ones where they were (or after a key, or last),
    /// each labelled with its name, a shot of its own (cut to and cutting out); and the rig as it then stands, its fingerprint the new keys'.
    /// </summary>
    /// <param name="document">The mission, which must declare <c>EditCamera</c>.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="rig">The rig.</param>
    /// <param name="subject">Where its subject stands, in metres; null for none.</param>
    /// <param name="after">For a new rig, the key it follows; null for the scene's end.</param>
    /// <returns>The step and the rig with its fingerprint; null where it makes no keys.</returns>
    public static (IEditorCommand Step, CutsceneRig Rig)? Place(MissionDocument document, MissionTask scene, CutsceneRig rig, Vector3d? subject, MissionTask? after)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rig);
        IReadOnlyList<CutsceneRigKey> made = Bake(rig, subject);
        if (made.Count == 0)
        {
            return null;
        }

        List<IEditorCommand> steps = [];
        void Apply(IEditorCommand step)
        {
            step.Apply();
            steps.Add(step);
        }

        // Where the keys go: where the old ones were.
        IReadOnlyList<MissionTask> old = KeysOf(scene, rig.Name);
        if (old.Count > 0)
        {
            IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
            int first = shots.Select(static s => s.Camera).ToList().IndexOf(old[0]);
            after = first > 0 ? shots[first - 1].Camera : null;
            foreach (MissionTask key in old)
            {
                Apply(new RemoveTaskCommand(document, key));
            }
        }

        bool atStart = old.Count > 0 && after is null;
        MissionTask? after0 = after;
        List<MissionTask> keys = [];
        foreach (CutsceneRigKey key in made)
        {
            if (CutsceneModel.AddShot(document, scene, after, (key.PositionUnits.X, key.PositionUnits.Y, key.PositionUnits.Z), (key.Angles.X, key.Angles.Y, key.Angles.Z), key.Seconds) is not { } added)
            {
                Undo(steps);
                return null;
            }

            added.Camera.Label = rig.Name;
            IEditorCommand step = atStart && after is null
                ? new InsertTaskCommand(document, added.Camera, new TaskLocation(scene, 0))
                : added.Step;
            Apply(step);
            keys.Add(added.Camera);
            after = added.Camera;
        }

        // A rig is a shot of its own, as a camera cut is: cut to from the key before it (a new one; one made again keeps what was
        // chosen), and cutting to whatever follows.
        if (old.Count == 0 && after0 is { } before && CutsceneMoves.Glides(before))
        {
            Apply(CutsceneModel.SetSmooth(document, before, false));
        }

        Apply(CutsceneModel.SetSmooth(document, keys[^1], false));
        CutsceneRig placed = rig with { Fingerprint = FingerprintOf(keys) };
        Undo(steps);
        return (new CompositeCommand(old.Count > 0 ? "Remake " + rig.Name : "Add " + rig.Name, steps), placed);
    }

    /// <summary>A name for a new rig of a kind in a scene: the kind and the first number free.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The name.</returns>
    public static string NewName(MissionTask scene, CutsceneRigKind kind)
    {
        HashSet<string> used = [.. CutsceneModel.Shots(scene).Select(static s => s.Camera.Label)];
        return Enumerable.Range(1, 999).Select(n => string.Create(CultureInfo.InvariantCulture, $"{kind} {n}")).First(n => !used.Contains(n));
    }

    /// <summary>Writes rigs as the sidecar keeps them.</summary>
    /// <param name="rigs">The rigs.</param>
    /// <returns>The sidecar's bytes.</returns>
    public static byte[] Write(IEnumerable<CutsceneRig> rigs) =>
        JsonSerializer.SerializeToUtf8Bytes(rigs.Select(static r => new RigFile(
            r.Name, r.Kind.ToString(), r.SceneId, r.SubjectId, [.. r.Points.Select(static p => new[] { p.X, p.Y, p.Z })],
            r.Seconds, r.Radius, r.Height, r.Sweep, r.Heading, r.Fingerprint)).ToList(), RigJson.Default.ListRigFile);

    /// <summary>Reads the sidecar's rigs.</summary>
    /// <param name="bytes">The sidecar's bytes.</param>
    /// <returns>The rigs.</returns>
    /// <exception cref="JsonException">The bytes are not a rigs sidecar.</exception>
    public static IReadOnlyList<CutsceneRig> Read(ReadOnlySpan<byte> bytes) =>
        [.. (JsonSerializer.Deserialize(bytes, RigJson.Default.ListRigFile) ?? []).Select(static f => new CutsceneRig(
            f.Name, Enum.Parse<CutsceneRigKind>(f.Kind), f.SceneId, f.SubjectId, [.. f.Points.Select(static p => new Vector3d(p[0], p[1], p[2]))],
            f.Seconds, f.Radius, f.Height, f.Sweep, f.Heading, f.Fingerprint))];

    // A rail: keys spaced evenly along its path, raised over it, each watching the subject or looking along the way ahead.
    private static List<(Vector3d At, Vector3d Look)> Rail(CutsceneRig rig, Vector3d? subject)
    {
        if (rig.Points.Count < 2)
        {
            return [];
        }

        Vector3d lift = new(0, 0, rig.Height);
        List<Vector3d> track = Rounded(rig.Points);
        List<double> along = [0];
        for (int i = 1; i < track.Count; i++)
        {
            along.Add(along[^1] + (track[i] - track[i - 1]).Length);
        }

        double length = along[^1];
        if (length < 0.5)
        {
            return [];
        }

        Vector3d On(double s)
        {
            s = Math.Clamp(s, 0, length);
            int i = 1;
            while (i < along.Count - 1 && along[i] < s)
            {
                i++;
            }

            double part = along[i] > along[i - 1] ? (s - along[i - 1]) / (along[i] - along[i - 1]) : 0;
            return track[i - 1] + ((track[i] - track[i - 1]) * part) + lift;
        }

        int count = Math.Clamp((int)Math.Ceiling(length / RailSpacing) + 1, 3, 32);
        List<(Vector3d, Vector3d)> poses = [];
        for (int i = 0; i < count; i++)
        {
            double s = length * i / (count - 1);
            Vector3d at = On(s);
            Vector3d look = subject ?? (s + LookAhead <= length ? On(s + LookAhead) : at + ((On(length) - On(length - LookAhead)) * 1.0));
            poses.Add((at, look));
        }

        return poses;
    }

    // A rail's track with its bends rounded, as a dolly track is laid: each corner cut back along both sides and joined by a curve
    // through it, so keys spaced along the track meet no corner the game's curve would slow at.
    private static List<Vector3d> Rounded(IReadOnlyList<Vector3d> points)
    {
        List<Vector3d> track = [points[0]];
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3d inward = points[i] - points[i - 1], outward = points[i + 1] - points[i];
            double cut = Math.Min(RailSpacing * 1.5, Math.Min(inward.Length, outward.Length) * 0.45);
            if (cut < 0.1)
            {
                track.Add(points[i]);
                continue;
            }

            Vector3d from = points[i] - (inward * (cut / inward.Length)), to = points[i] + (outward * (cut / outward.Length));
            for (int s = 0; s <= 12; s++)
            {
                double u = s / 12.0;
                track.Add((from * ((1 - u) * (1 - u))) + (points[i] * (2 * u * (1 - u))) + (to * (u * u)));
            }
        }

        track.Add(points[^1]);
        return track;
    }

    // A crane or an orbit: round the subject from its heading through its sweep, a key every so many degrees, the crane rising.
    private static List<(Vector3d At, Vector3d Look)> Swing(CutsceneRig rig, Vector3d centre)
    {
        int count = Math.Clamp((int)Math.Ceiling(Math.Abs(rig.Sweep) / 30) + 1, 3, 25);
        List<(Vector3d, Vector3d)> poses = [];
        for (int i = 0; i < count; i++)
        {
            double share = i / (double)(count - 1);
            double angle = (rig.Heading + (rig.Sweep * share)) * Math.PI / 180;
            double height = rig.Kind == CutsceneRigKind.Crane ? 1.8 + (rig.Height * share) : rig.Height;
            double reach = rig.Kind == CutsceneRigKind.Crane ? rig.Radius * (1 - (0.25 * share)) : rig.Radius;
            poses.Add((centre + new Vector3d(Math.Cos(angle) * reach, Math.Sin(angle) * reach, height), centre));
        }

        return poses;
    }

    private static void Undo(List<IEditorCommand> steps)
    {
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            steps[i].Undo();
        }
    }

}

/// <summary>A rig as the sidecar writes it.</summary>
internal sealed record RigFile(
    string Name, string Kind, int SceneId, int SubjectId, List<double[]> Points, double Seconds, double Radius, double Height, double Sweep, double Heading, string Fingerprint);

/// <summary>The sidecar's JSON, made at build time.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<RigFile>))]
internal sealed partial class RigJson : JsonSerializerContext;
