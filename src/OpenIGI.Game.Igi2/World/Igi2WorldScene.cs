using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One model a level places in the world, with everything needed to draw it.</summary>
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
public readonly record struct Igi2Placement(
    string TypeName,
    string Model,
    Vector3d Position,
    Vector3f Orientation,
    int RenderGroup);

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
            if (child is DeclaredTask declared && TryPlace(declared, out Igi2Placement placement))
            {
                into.Add(placement);
            }

            Collect(child, into);
        }
    }

    private static bool TryPlace(DeclaredTask task, out Igi2Placement placement)
    {
        placement = default;

        if (!task.TryGetValue(ModelParameter, out object? rawModel)
            || rawModel is not string model
            || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        if (!task.TryGetValue(PositionParameter, out object? rawPosition) || rawPosition is not Vector3d position)
        {
            return false;
        }

        Vector3f orientation = task.TryGetValue(OrientationParameter, out object? rawOrientation)
            && rawOrientation is Vector3f angles
                ? angles
                : default;

        int group = task.TryGetValue(RenderGroupParameter, out object? rawGroup) && rawGroup is int value
            ? value
            : 0;

        placement = new Igi2Placement(task.Type.Name, model, position, orientation, group);
        return true;
    }
}
