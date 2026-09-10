using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One tree of a level's forest, placed in the world.</summary>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Angles">Its three orientation angles, in radians.</param>
/// <param name="Scale">Its per-axis scale.</param>
/// <param name="Light">The colour the bake gives it.</param>
public readonly record struct Igi2Tree(Vector3d Position, Vector3f Angles, Vector3f Scale, Bgra32 Light);

/// <summary>
/// A level's forests: the trees each <c>Forest</c> task scatters, placed in the world.
/// </summary>
/// <remarks>
/// <para>
/// A forest is not placed the way anything else is. The task says where the stand is, how big it is and
/// which model to use; the trees themselves live in a separate <c>forest_&lt;taskId&gt;.dat</c> beside
/// the level, keyed by the same level task id its baked lightmaps use.
/// </para>
/// <para>
/// <b>A tree's stored position is local to its task.</b> <c>Forest_DrawTrees</c>
/// (<c>igi2ed 0x64AC80</c>) culls the stand against a square of
/// <c>AreaSize * 4096</c> centred on the task, and walks its BSP over
/// <c>±AreaSize * 2048</c> — local coordinates, half the area either side. The shipped files agree:
/// X and Y run to a few hundred metres either side of nothing.
/// </para>
/// </remarks>
public sealed class Igi2Forest
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.forest";

    private Igi2Forest(IReadOnlyList<Igi2ForestStand> stands) => Stands = stands;

    /// <summary>Gets the level's stands, in the order it declared them.</summary>
    public IReadOnlyList<Igi2ForestStand> Stands { get; }

    /// <summary>Gets how many trees the level plants between them.</summary>
    public int TreeCount => Stands.Sum(static s => s.Trees.Count);

    /// <summary>Gets a value indicating whether the level has any forest.</summary>
    public bool IsEmpty => Stands.Count == 0;

    /// <summary>Builds the resource name a forest's tree list is read from.</summary>
    /// <param name="taskId">The <c>Forest</c> task's level id.</param>
    /// <returns>The alias-qualified resource name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="taskId"/> is negative.</exception>
    /// <remarks>
    /// Loose beside the level rather than inside an archive, and named by task id — the same convention
    /// the object lightmaps use, and for the same reason: the id is the only thing that identifies which
    /// object a baked file belongs to.
    /// </remarks>
    public static string ResourceNameFor(int taskId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(taskId);

        return string.Create(CultureInfo.InvariantCulture, $"MISSION:forest_{taskId}.dat");
    }

    /// <summary>Loads the forests a level declares.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="resources">The registry the tree lists are read from.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <returns>The forests, which may be empty.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// A <c>Forest</c> task whose file is absent contributes no trees rather than faulting. The task is
    /// the authority on the stand; the file is the bake, and a level may ship one without the other.
    /// </remarks>
    public static Igi2Forest Build(GameTask levelRoot, ResourceRegistry resources, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(resources);

        IIgiLogger log = logger ?? NullLogger.Instance;
        List<Igi2ForestStand> stands = [];

        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is not ForestTask forest || task.Id == GameTask.NoId)
            {
                continue;
            }

            string resource = ResourceNameFor(task.Id);
            if (!resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
            {
                log.Warning(LogCategory, $"Forest {task.Id} has no '{resource}'; it plants nothing.");
                continue;
            }

            Igi2ForestData data = Igi2ForestData.Read(bytes);
            List<Igi2Tree> trees = new(data.Count);
            foreach (Igi2ForestTree tree in data.Trees())
            {
                // Local to the stand, which is what the draw path's own bounds say.
                trees.Add(new Igi2Tree(
                    new Vector3d(
                        forest.Position.X + tree.Position.X,
                        forest.Position.Y + tree.Position.Y,
                        forest.Position.Z + tree.Position.Z),
                    tree.Angles,
                    tree.Scale,
                    tree.Light));
            }

            stands.Add(new Igi2ForestStand(task.Id, forest.Model, forest.Position, forest.AreaSize, trees));
        }

        log.Information(LogCategory, $"Forest: {stands.Count} stand(s), {stands.Sum(static s => s.Trees.Count)} trees.");
        return new Igi2Forest(stands);
    }

    /// <inheritdoc/>
    public override string ToString() => $"I.G.I. 2 forest, {Stands.Count} stand(s), {TreeCount} trees";

    private static IEnumerable<GameTask> Walk(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;

            foreach (GameTask descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}

/// <summary>One <c>Forest</c> task's stand of trees.</summary>
/// <param name="TaskId">The level task id, which is what names the tree list.</param>
/// <param name="Model">The tree model every tree in the stand draws.</param>
/// <param name="Centre">Where the stand sits, in world units.</param>
/// <param name="AreaSize">How wide the stand is, in metres.</param>
/// <param name="Trees">The trees, already placed in the world.</param>
public readonly record struct Igi2ForestStand(
    int TaskId,
    string Model,
    Vector3d Centre,
    float AreaSize,
    IReadOnlyList<Igi2Tree> Trees)
{
    /// <summary>Gets how far the stand reaches either side of its centre, in world units.</summary>
    /// <remarks>
    /// <c>AreaSize * 4096 / 2</c>, which is what <c>Forest_DrawTrees</c> culls against and what its BSP
    /// is walked over. The size is authored in metres, like a terrain tile's world extent.
    /// </remarks>
    public double HalfExtent => AreaSize * WorldScene.UnitsPerMetre * 0.5;
}
