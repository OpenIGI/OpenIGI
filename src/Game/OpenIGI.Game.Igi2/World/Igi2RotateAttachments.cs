using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One part of a placed object, and the placement the walk made for it.</summary>
/// <param name="OwnerLevelId">The level task id of the object the part hangs off.</param>
/// <param name="Model">The part's model name, which is what an attachment is named by.</param>
/// <param name="Placement">The placement itself.</param>
/// <remarks>
/// Recorded by <see cref="Igi2DrawableScene"/> as it expands an object's attachments, because that is
/// the only moment at which a placement and the task it belongs to are both in hand. Everything
/// downstream sees a flat list of placements with no way back to the object that carried them.
/// </remarks>
public readonly record struct Igi2AttachedPlacement(int OwnerLevelId, string Model, WorldPlacement Placement);

/// <summary>The parts of I.G.I. 2's objects that turn on the spot.</summary>
/// <remarks>
/// <para>
/// <b>A <c>RotateAttachment</c> places nothing.</b> It names a part that some other task's model already
/// carries — by model name and, where an object has several of them, by which one — and gives it three
/// rotation speeds. <c>RotateAttachment_UpdateInternalDataHandler</c> (<c>igi2ed 0x671B50</c>) walks UP
/// from the task to the first object-derived ancestor, then along that object's attachment list for the
/// Nth entry whose model matches, and keeps a reference to it. That is the whole of its placement work:
/// the geometry was already there.
/// </para>
/// <para>
/// <b>What it does every tick</b> is <c>RotateAttachment_RunHandler</c> (<c>0x671730</c>): build a
/// rotation from the three speeds as Euler angles, multiply the part's LOCAL orientation by it,
/// re-orthonormalise, and re-run the attachment update so the world transform follows. So the speeds are
/// radians per tick, not per second — the handler never looks at elapsed time.
/// </para>
/// <para>
/// <b>Which means the port needs no local orientations.</b> The world orientation of a part is its
/// parent's times its local one, and only the local one turns, so the world orientation after
/// <c>t</c> ticks is simply the authored world orientation times the same power. The placement the walk
/// already produced is the starting point, and nothing has to be recomposed up the tree.
/// </para>
/// </remarks>
public sealed class Igi2RotateAttachments : IPlacementSpin
{
    /// <summary>The task type that turns a part of another object.</summary>
    public const string TaskType = "RotateAttachment";

    private readonly Dictionary<WorldPlacement, Spin> _spins;
    private float _ticks;

    private Igi2RotateAttachments(Dictionary<WorldPlacement, Spin> spins) => _spins = spins;

    /// <summary>Gets how many placements this turns.</summary>
    public int Count => _spins.Count;

    /// <summary>Binds every <c>RotateAttachment</c> in a level to the part it turns.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="attached">Every attachment placement the walk made, in the order it made them.</param>
    /// <returns>The registry, or <see langword="null"/> when the level turns nothing.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static Igi2RotateAttachments? Build(GameTask levelRoot, IReadOnlyList<Igi2AttachedPlacement> attached)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(attached);

        // The parts of each object, by object and by model name, in the order the object's attachment
        // array declares them -- which is the order the original's attach list is walked in, and so the
        // order an "Attachment model instance" counts through.
        Dictionary<(int Owner, string Model), List<WorldPlacement>> parts = [];
        foreach (Igi2AttachedPlacement part in attached)
        {
            (int, string) key = (part.OwnerLevelId, part.Model);
            if (!parts.TryGetValue(key, out List<WorldPlacement>? list))
            {
                list = [];
                parts[key] = list;
            }

            list.Add(part.Placement);
        }

        Dictionary<WorldPlacement, Spin> spins = new(ReferenceComparer.Instance);
        Collect(levelRoot, parts, spins);
        return spins.Count > 0 ? new Igi2RotateAttachments(spins) : null;
    }

    /// <inheritdoc/>
    public void Advance() => _ticks++;

    /// <inheritdoc/>
    public bool TryGetOrientation(WorldPlacement placement, float tickFraction, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);

        if (!_spins.TryGetValue(placement, out Spin spin))
        {
            orientation = default;
            return false;
        }

        // Between ticks as well as on them. The original steps a whole tick at a time because it has
        // nowhere else to put the part; a renderer that is told how far through the tick it is can turn
        // the same amount smoothly, which is what the security cameras' swivel already does.
        float turned = _ticks + Math.Clamp(tickFraction, 0f, 1f);
        orientation = placement.Orientation * Matrix3f.FromEngineEulerAngles(
            Wrap(spin.Alpha * turned),
            Wrap(spin.Beta * turned),
            Wrap(spin.Gamma * turned));
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"I.G.I. 2 rotating attachments, {_spins.Count} part(s)";

    /// <summary>Keeps an angle inside one turn, so a long mission does not lose precision to the count.</summary>
    /// <remarks>
    /// Exact for a part turning about ONE axis, which every one of the corpus's 250 does — a rotation
    /// about a single axis by <c>n·θ</c> is the same as by <c>n·θ</c> less whole turns. A part turning
    /// about two at once would need the matrix power proper rather than the angles scaled, and
    /// <c>Igi2RotateAttachmentTests</c> pins that none does.
    /// </remarks>
    private static float Wrap(float radians) => radians % MathF.Tau;

    private static void Collect(
        GameTask task,
        Dictionary<(int Owner, string Model), List<WorldPlacement>> parts,
        Dictionary<WorldPlacement, Spin> into)
    {
        foreach (GameTask child in task.Children)
        {
            if (string.Equals(child.Type.Name, TaskType, StringComparison.Ordinal)
                && TryBind(child, parts, out WorldPlacement? placement, out Spin spin)
                && placement is not null)
            {
                into[placement] = spin;
            }

            Collect(child, parts, into);
        }
    }

    private static bool TryBind(
        GameTask task,
        Dictionary<(int Owner, string Model), List<WorldPlacement>> parts,
        out WorldPlacement? placement,
        out Spin spin)
    {
        placement = null;
        spin = default;

        if (!task.TryGetScriptValue("Attachment model", out object? rawModel)
            || rawModel is not string model
            || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        int instance = task.TryGetScriptValue("Attachment model instance", out object? rawInstance)
            && rawInstance is int index
                ? index
                : 0;

        // Up to the object that carries the part. The original stops at the first RigidDynCubeObj-derived
        // ancestor; this stops at the first that actually placed something of this model, which is the
        // same task and needs no list of which types derive from what.
        for (GameTask? owner = task.Parent; owner is not null; owner = owner.Parent)
        {
            if (!parts.TryGetValue((owner.Id, model), out List<WorldPlacement>? candidates))
            {
                continue;
            }

            if (instance < 0 || instance >= candidates.Count)
            {
                return false;
            }

            placement = candidates[instance];
            spin = new Spin(Speed(task, "AlphaRotationSpeed"), Speed(task, "BetaRotationSpeed"), Speed(task, "GammaRotationSpeed"));

            // A part told to turn at nothing is not a spin, and binding it would make the renderer
            // rebuild a transform every frame to arrive back where it started.
            return spin.Alpha != 0f || spin.Beta != 0f || spin.Gamma != 0f;
        }

        return false;
    }

    private static float Speed(GameTask task, string name) =>
        task.TryGetScriptValue(name, out object? raw) && raw is float value ? value : 0f;

    /// <summary>How fast one part turns, in radians per tick about each of the three axes.</summary>
    private readonly record struct Spin(float Alpha, float Beta, float Gamma);

    /// <summary>Matches placements by identity rather than by value.</summary>
    /// <remarks>
    /// A <see cref="WorldPlacement"/> is a record, so two parts of one model standing in the same pose on
    /// two different objects compare equal. The walk made each of them separately and this has to keep
    /// them apart, so the exact instances the scene holds are what is looked up.
    /// </remarks>
    private sealed class ReferenceComparer : IEqualityComparer<WorldPlacement>
    {
        public static ReferenceComparer Instance { get; } = new();

        public bool Equals(WorldPlacement? left, WorldPlacement? right) => ReferenceEquals(left, right);

        public int GetHashCode(WorldPlacement value) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}
