using System.Globalization;
using OpenIGI.Engine.Tasks;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The draw groups I.G.I. 2's levels author on their placed objects.
/// </summary>
/// <remarks>
/// <para>
/// The original has no fixed pass order. <c>World_Draw</c> (<c>igi2ed 0x417F50</c>) enqueues each visible
/// object as the cube tree finds it, and <c>QCube_CallHandlers</c> (<c>0x4171B0</c>) then empties sixteen
/// call queues in order, flushing shadow volumes between each. An object's queue is
/// <c>DynCubeObj_s::iRenderGroup</c> at offset <c>0x6C</c>, and that is a level parameter — the editor
/// registers it on <c>EditRigidObj</c> by name, so an author picks it per object.
/// </para>
/// <para>
/// This measures what the shipped levels actually pick, because the answer decides how much of that
/// mechanism the port needs.
/// </para>
/// </remarks>
public sealed class RetailIgi2DrawOrderTests
{
    /// <summary>The task type every I.G.I. 2 level places its rigid objects with.</summary>
    private const string RigidObjectType = "EditRigidObj";

    /// <summary>The parameter carrying the object's call-queue index.</summary>
    private const string RenderGroupParameter = "RenderGroup";

    /// <summary>How many call queues the frame flushes, in order.</summary>
    private const int CallQueueCount = 16;

    /// <summary>
    /// Every placed object authors a draw group, and the shipped levels use four of the sixteen queues —
    /// two of them for almost everything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 8,472 placed objects across the 24 levels fall into groups 5 (4,860), 12 (3,562), 8 (49) and 7
    /// (one single object). So the world is drawn in two main waves with several shadow-volume flushes
    /// between them, not in one.
    /// </para>
    /// <para>
    /// That is the fact that decides how much of the mechanism the port needs, and it is why a fixed
    /// pass order — which is what the host does today — is wrong for this game in a way it is not wrong
    /// for Project I.G.I., whose levels author no groups at all and so collapse to a single queue.
    /// Group 5 is also the queue the frame inserts infra-vision after, which is worth knowing before
    /// that mode is implemented.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Placed_objects_author_draw_groups_from_a_small_set()
    {
        SortedDictionary<int, int> groups = [];
        int objects = 0;
        int levels = 0;

        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            bool any = false;
            foreach (DeclaredTask task in DeclaredOfType(harness.LevelRoot, RigidObjectType))
            {
                objects++;
                any = true;

                Assert.True(
                    task.TryGetValue(RenderGroupParameter, out object? raw),
                    $"'{script}' has an {RigidObjectType} with no {RenderGroupParameter}.");

                int group = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                Assert.InRange(group, 0, CallQueueCount - 1);
                groups[group] = groups.GetValueOrDefault(group) + 1;
            }

            levels += any ? 1 : 0;
        }

        Assert.Equal(24, levels);
        Assert.True(objects > 1000, $"Only {objects} placed objects were examined.");

        // Four of the sixteen queues, and two of those carry 99 per cent of the objects between them.
        Assert.Equal<int[]>([5, 7, 8, 12], [.. groups.Keys]);
        Assert.Equal(8472, objects);
        Assert.True(
            groups[5] + groups[12] > objects * 99 / 100,
            $"Groups 5 and 12 hold {groups[5] + groups[12]} of {objects}, less than this assumes.");
    }

    private static IEnumerable<DeclaredTask> DeclaredOfType(GameTask root, string typeName)
    {
        foreach (GameTask child in root.Children)
        {
            if (child is DeclaredTask declared && child.Type.Name == typeName)
            {
                yield return declared;
            }

            foreach (DeclaredTask nested in DeclaredOfType(child, typeName))
            {
                yield return nested;
            }
        }
    }
}
