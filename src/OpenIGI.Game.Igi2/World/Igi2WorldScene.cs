using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One model a level places in the world, with everything needed to draw it.</summary>
/// <param name="LevelId">
/// The level task id the placement was declared with, or <see cref="GameTask.NoId"/> when it has none.
/// This is what names its baked lightmaps — see <see cref="Igi2LightmapIndex"/>.
/// </param>
/// <param name="TypeName">The task type that placed it, kept for diagnostics rather than for drawing.</param>
/// <param name="Model">The model's base name, without a level-of-detail suffix or an extension.</param>
/// <param name="Position">Where it stands, in engine units.</param>
/// <param name="Orientation">
/// Its orientation as the three angles level data stores, in radians. <c>Real32x9</c> is the declared
/// type and a nine-float matrix is what the engine keeps, but what a script writes is three angles —
/// see <c>ScriptArgumentReader.ReadReal32x9</c>.
/// </param>
/// <param name="RenderGroup">
/// Which of the frame's sixteen call queues it is drawn in. Zero for the task types that declare no
/// group, which is also the queue an object lands in when a level does not say.
/// </param>
/// <param name="MountedModel">
/// A second model this one carries, which sits on the host's first magic vertex rather than at a
/// position of its own — a security camera's body on its bracket, a gun's barrel on its mount. Empty
/// for the great majority of placements, which carry nothing.
/// </param>
/// <param name="MountedOrientation">
/// The mounted model's own facing, in the same three angles, already composed with the host's.
/// </param>
/// <param name="ModelVariant">
/// Which of the owning task's model SLOTS this is wearing, counted from one, or zero when the type has
/// a single slot or the port has not established its enumeration. It is the group letter in the name of
/// the object's baked lightmap — <c>LightmapInfo_AddLightmapVirModelToEnumeration</c>
/// (<c>igi2 0x53C920</c>) writes <c>acIndexLetters[nModelVariation]</c>, and a slot's variation is its
/// index plus one — so it is what picks the matching bake out of the level's archive. See
/// <see cref="Igi2LightmapIndex"/>.
/// </param>
public readonly record struct Igi2Placement(
    int LevelId,
    string TypeName,
    string Model,
    Vector3d Position,
    Vector3f Orientation,
    int RenderGroup,
    int ModelVariant = 0,
    string MountedModel = "",
    Vector3f MountedOrientation = default);

/// <summary>
/// Everything an I.G.I. 2 level places in the world, walked out of its task tree.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read by parameter name, not by task class.</b> Twenty-three of the sequel's task types carry a
/// <c>Model</c>, from <c>EditRigidObj</c> through <c>Door</c>, <c>Lift</c>, <c>Car</c> and <c>Fence</c>
/// to <c>Terminal</c>, and every one of them declares the model, the position and the orientation under
/// the same three names. Walking the names collects all twenty-three at once and needs no class for any
/// of them, which is what makes a world drawable long before the gameplay behind it exists.
/// </para>
/// <para>
/// Only <see cref="DeclaredTask"/> is walked, and that is the rule rather than a limitation: a type
/// with a class has a subsystem that owns it, and a forest or a terrain tile is not one model at one
/// place. When a gameplay class arrives for a type that is currently a placeholder, its objects leave
/// this scene and become that subsystem's business — which is the correct direction, because by then
/// something knows how to animate them.
/// </para>
/// <para>
/// This is geometry, not gameplay. It carries no collision, no visibility set and no lightmap binding;
/// those come later, from the level's <c>.mtp</c> packs and its object lightmaps.
/// </para>
/// </remarks>
public sealed class Igi2WorldScene
{
    /// <summary>The parameter every placeable task names its model with.</summary>
    public const string ModelParameter = "Model";

    /// <summary>The parameter every placeable task names its position with.</summary>
    public const string PositionParameter = "Position";

    /// <summary>The parameter every placeable task names its orientation with.</summary>
    public const string OrientationParameter = "Orientation";

    /// <summary>The parameter a placeable task names its draw queue with, when it has one.</summary>
    public const string RenderGroupParameter = "RenderGroup";

    /// <summary>What a type that names its parameters the usual way is read with.</summary>
    public static Igi2PlacementBinding DefaultBinding { get; } =
        new(ModelParameter, PositionParameter, OrientationParameter);

    /// <summary>The types that name their model, position or facing something other than the usual.</summary>
    /// <remarks>
    /// <para>
    /// <b>Every entry is the rest transform its create handler builds</b>, read out of the dedicated
    /// server's <c>igi2.exe</c> rather than inferred from the parameter names:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>Door</c> — <c>Door_UpdateInternalDataHandler</c> (<c>0x537E10</c>) copies the task's position
    /// into <c>tOriginalPosStart</c>, sets <c>isClosed</c>, and calls <c>Door_SetPosition</c>
    /// (<c>0x5362B0</c>), which offsets by <c>stop × slider</c> and turns by <c>maxAngle × slider</c>.
    /// A closed door has a slider of zero, so it stands exactly at <c>Position start</c> under
    /// <c>Orientation</c>. 802 of them in the corpus.
    /// </description></item>
    /// <item><description>
    /// <c>Switch</c> — <c>Switch_SetModel</c> (<c>0x4DA360</c>) indexes the five models as on,
    /// on-pressed, off, off-pressed, destroyed. Unpressed and undamaged, <c>Initial on</c> alone chooses
    /// between the first and the third.
    /// </description></item>
    /// <item><description>
    /// <c>SCamera</c> — <c>SCamera_UpdateInternalDataHandler</c> (<c>0x48FB80</c>) builds the holder's
    /// rotation from a single angle's sine and cosine, so the bracket turns about the up axis only. The
    /// camera body is a separate child task whose own handler (<c>0x48F370</c>) places it on a MAGIC
    /// VERTEX of the holder, which is why only the holder is bound here.
    /// </description></item>
    /// <item><description>
    /// <c>StationaryGun</c>, <c>AIStationaryGunHolder</c> — <c>AIStationaryGunHolder_UpdateInternalDataHandler</c>
    /// (<c>0x5158E0</c>) copies the parameter block's orientation straight into the model object. The
    /// barrel is an attachment, and is left out for the same reason the camera body is.
    /// </description></item>
    /// <item><description>
    /// <c>Lift</c> — the authored start. <c>Lift_SetPositionToFirstFloor</c> (<c>0x46DD50</c>) moves a
    /// lift onto its first <c>Floor</c> child at load, which this does not do; the eight lifts in the
    /// corpus stand where the editor left them until it does.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Not here, and why.</b> <c>SplineObjWaypoint</c> (678) carries a <c>Segment Model</c> that is
    /// extruded along the spline between waypoints by <c>SplineObjSegment_Update</c>, and a
    /// <c>Waypoint Model</c> that is the editor's own <c>ai-arrow</c>; drawing either at the waypoint
    /// would be wrong. <c>RotateAttachment</c> (250) has no position at all — it hangs off its parent.
    /// <c>Wire</c> (25) is strung between a start and a stop. <c>Train</c> (9) rides a spline.
    /// <c>ComputerHilight</c> (108) names <c>MARKER_NONE</c>, a sentinel for the computer overlay rather
    /// than a model, and <c>LevelInfo</c> (19) names a script path. Each is a subsystem, not a name.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, Igi2PlacementBinding> Bindings { get; } =
        new Dictionary<string, Igi2PlacementBinding>(StringComparer.Ordinal)
        {
            ["Door"] = new("Model", "Position start", OrientationParameter),
            ["Switch"] = new(
                "Off model",
                PositionParameter,
                OrientationParameter,
                AlternateModel: "On model",
                AlternateWhen: "Initial on",
                Enumerated: ["On model", "On pressed model", "Off model", "Off pressed model", "Destroyed model"]),
            ["SCamera"] = new(
                "Holder Model",
                "Holder Position",
                Gamma: "Holder Gamma",
                MountedModel: "Camera Model",
                MountedAlpha: "Camera Alpha",
                MountedGamma: "Camera Gamma"),
            ["StationaryGun"] = new("Holder model", PositionParameter, OrientationParameter),
            ["AIStationaryGunHolder"] = new(
                "Holder model",
                PositionParameter,
                OrientationParameter,
                MountedModel: "Barrel model"),
            ["Lift"] = new("Model", "Start Position", "Start Orientation"),
        };

    private Igi2WorldScene(IReadOnlyList<Igi2Placement> placements) => Placements = placements;

    /// <summary>Gets every placement, in the order the level declared them.</summary>
    /// <remarks>
    /// Declaration order, not draw order. What decides drawing is
    /// <see cref="Igi2Placement.RenderGroup"/>, and the frame empties those queues in ascending order;
    /// see the original.
    /// </remarks>
    public IReadOnlyList<Igi2Placement> Placements { get; }

    /// <summary>Walks a loaded level and collects everything it places.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static Igi2WorldScene Build(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        List<Igi2Placement> placements = [];
        Collect(levelRoot, placements);
        return new Igi2WorldScene(placements);
    }

    /// <summary>The task types whose <c>Model</c> is not a file name.</summary>
    /// <remarks>
    /// <para>
    /// Measured, not assumed. Across the 24 shipped levels the static props resolve by file name almost
    /// perfectly — <c>Fence</c>, <c>Ladder</c>, <c>Cabinet</c>, <c>Car</c> and <c>ConveyorBelt</c> at
    /// 100%, <c>EditRigidObj</c> at 8,296 of 8,469 — while these do not: <c>HumanSoldier</c> resolves 34
    /// of 901, <c>HumanPlayer</c> none of 19, and <c>EditBoneObj</c> 90 of 301.
    /// </para>
    /// <para>
    /// So a character's <c>Model</c> is a character <em>kind</em> that something else turns into geometry,
    /// not a file: level 1 asks for <c>rsg1_1</c>, <c>rsg2_1</c> and <c>rsg3_1</c> where the archives hold
    /// only <c>rsg_1</c> through <c>rsg_4</c>, which are its levels of detail. Which table performs that
    /// lookup is the first question M2-4 has to answer, and the <c>.mtp</c> packs each level ships beside
    /// its models are the place to look.
    /// </para>
    /// <para>
    /// A world renderer skips these: a character is drawn by a character presenter, from a skinned mesh,
    /// on a schedule the world pass does not own.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> CharacterTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "HumanPlayer",
        "HumanSoldier",
        "EditBoneObj",
        "AlarmControl",
    };

    /// <summary>Gets every placement a world pass should draw.</summary>
    /// <remarks>
    /// <see cref="Placements"/> less <see cref="CharacterTypes"/>. Kept as a filter over the whole list
    /// rather than applied during the walk, because what counts as a character is a judgement the scene
    /// should be able to state and a caller should be able to disagree with.
    /// </remarks>
    public IEnumerable<Igi2Placement> StaticPlacements =>
        Placements.Where(static p => !CharacterTypes.Contains(p.TypeName));

    /// <summary>Gets the resource name a model is loaded under.</summary>
    /// <param name="model">The model as level data names it.</param>
    /// <returns>The alias-qualified resource name.</returns>
    /// <exception cref="ArgumentException"><paramref name="model"/> is null or empty.</exception>
    /// <remarks>
    /// <b>The name is used as it is written.</b> Level data already carries the level-of-detail suffix —
    /// it asks for <c>940_07_1</c>, not <c>940_07</c> — so a renderer that appended one would ask for
    /// <c>940_07_1_1</c> and find nothing. Which is exactly what this method did when it was first
    /// written: every one of the corpus's models failed to resolve, all at once, which is the good way
    /// for that mistake to show up.
    /// </remarks>
    public static string ModelResourceName(string model)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);

        return string.Create(CultureInfo.InvariantCulture, $"LOCAL:models/{model}.mef");
    }

    /// <inheritdoc/>
    public override string ToString() => $"I.G.I. 2 scene, {Placements.Count} placements";

    private static void Collect(GameTask task, List<Igi2Placement> into)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is DeclaredTask declared && TryReadPlacement(declared, out Igi2Placement placement))
            {
                into.Add(placement);
            }

            Collect(child, into);
        }
    }

    /// <summary>Reads one task's placement, if it declares the three parameters that make one.</summary>
    /// <param name="task">The placeholder task.</param>
    /// <param name="placement">Receives the placement.</param>
    /// <returns><see langword="false"/> when the task places no model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Public because a caller that needs a placement's <em>context</em> — <see cref="Igi2SectorGraph"/>
    /// needs the task it hangs under — has to walk the tree itself, and would otherwise reimplement the
    /// parameter reading this already does.
    /// </remarks>
    public static bool TryPlace(DeclaredTask task, out Igi2Placement placement)
    {
        ArgumentNullException.ThrowIfNull(task);

        return TryReadPlacement(task, out placement);
    }

    /// <summary>Which model slot of its task a chosen model is, counted from one.</summary>
    /// <remarks>
    /// <para>
    /// <c>Switch_EnumerateLightmapVirModelsHandler</c> (<c>igi2 0x4DB120</c>) walks the five model slots
    /// in order and sets <c>nModelVariation</c> to the slot's own index plus one before enumerating it.
    /// <c>LightmapInfo_AddLightmapVirModelToEnumeration</c> (<c>0x53C920</c>) then writes that through
    /// <c>acIndexLetters</c>, so slot zero's bakes are lettered <c>a</c> and slot four's <c>e</c>. The
    /// index is the RAW slot: a slot naming a model an earlier slot already named takes no entry of its
    /// own, but does not shift the slots after it either.
    /// </para>
    /// <para>
    /// Zero for a type this port has not read an enumerator for, which means "the first bake the archive
    /// holds" rather than a particular slot — see <c>Igi2DrawableScene</c>.
    /// </para>
    /// </remarks>
    private static int VariantOf(DeclaredTask task, Igi2PlacementBinding binding, string model)
    {
        if (binding.Enumerated is not { Count: > 0 } enumerated)
        {
            return 0;
        }

        for (int slot = 0; slot < enumerated.Count; slot++)
        {
            if (task.TryGetValue(enumerated[slot], out object? raw)
                && raw is string name
                && string.Equals(name, model, StringComparison.OrdinalIgnoreCase))
            {
                return slot + 1;
            }
        }

        return 0;
    }

    private static bool TryReadPlacement(DeclaredTask task, out Igi2Placement placement)
    {
        placement = default;

        Igi2PlacementBinding binding = Bindings.TryGetValue(task.Type.Name, out Igi2PlacementBinding bound)
            ? bound
            : DefaultBinding;

        // A type with two models says which it is wearing at rest with a flag of its own.
        string modelParameter = binding.AlternateWhen.Length > 0
            && task.TryGetValue(binding.AlternateWhen, out object? rawAlternate)
            && rawAlternate is bool alternate
            && alternate
                ? binding.AlternateModel
                : binding.Model;

        if (!task.TryGetValue(modelParameter, out object? rawModel)
            || rawModel is not string model
            || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        if (!task.TryGetValue(binding.Position, out object? rawPosition) || rawPosition is not Vector3d position)
        {
            return false;
        }

        // Euler angles where the type carries three, and a lone gamma -- a turn about the world's up
        // axis -- where it carries one. A type that carries neither faces down the world's X axis.
        Vector3f orientation = default;
        if (binding.Orientation.Length > 0
            && task.TryGetValue(binding.Orientation, out object? rawOrientation)
            && rawOrientation is Vector3f angles)
        {
            orientation = angles;
        }
        else if (binding.Gamma.Length > 0
            && task.TryGetValue(binding.Gamma, out object? rawGamma)
            && rawGamma is float gamma)
        {
            orientation = new Vector3f(0f, 0f, gamma);
        }

        int group = task.TryGetValue(RenderGroupParameter, out object? rawGroup) && rawGroup is int value
            ? value
            : 0;

        // A second model carried on the first. Its tilt is its own and its turn is added to the host's,
        // which is the composition SCamera_UpdateInternalDataHandler (igi2 0x48FB80) builds for the
        // camera body: one angle about the up axis summed with the bracket's, and one about the long
        // axis for the downward look. A mounted model that names neither simply faces as its host does.
        string mounted = binding.MountedModel.Length > 0
            && task.TryGetValue(binding.MountedModel, out object? rawMounted)
            && rawMounted is string mountedName
            && !string.IsNullOrWhiteSpace(mountedName)
                ? mountedName
                : string.Empty;

        Vector3f mountedOrientation = orientation;
        if (mounted.Length > 0 && (binding.MountedAlpha.Length > 0 || binding.MountedGamma.Length > 0))
        {
            float alpha = task.TryGetValue(binding.MountedAlpha, out object? rawAlpha) && rawAlpha is float a ? a : 0f;
            float gamma = task.TryGetValue(binding.MountedGamma, out object? rawGamma2) && rawGamma2 is float g ? g : 0f;
            mountedOrientation = new Vector3f(alpha, 0f, orientation.Z + gamma);
        }

        placement = new Igi2Placement(
            task.Id,
            task.Type.Name,
            model,
            position,
            orientation,
            group,
            VariantOf(task, binding, model),
            mounted,
            mountedOrientation);
        return true;
    }
}
