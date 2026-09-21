using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The parts of I.G.I. 2's objects that turn on the spot, and what turns them.</summary>
/// <remarks>
/// <para>
/// A <c>RotateAttachment</c> is the one task type in the sequel that places nothing and yet is not
/// scenery: it reaches into an object already placed, finds one of its parts by model name, and spins
/// it. So the thing to check is the reach — that every one of the corpus's 250 finds the part it names —
/// because a task that binds to nothing fails silently and leaves a fan standing still, which is exactly
/// what the port did before.
/// </para>
/// <para>
/// The angles are the other half. The speeds are radians per TICK, not per second, because
/// <c>RotateAttachment_RunHandler</c> (<c>igi2ed 0x671730</c>) never looks at elapsed time — it builds a
/// rotation from the three numbers as they stand and multiplies the part's orientation by it once per
/// run.
/// </para>
/// </remarks>
public sealed class Igi2RotateAttachmentTests
{
    /// <summary>Every rotating attachment in the corpus finds the part it names.</summary>
    /// <remarks>
    /// Counted exactly rather than tolerated. The binding walks up from the task to the object carrying
    /// the part and then indexes that object's own attachments by model name, and a mistake in either
    /// step shows up as a task that quietly drives nothing.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_rotating_attachment_finds_the_part_it_names()
    {
        int declared = 0;
        int bound = 0;
        int levels = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            int here = 0;
            foreach (GameTask task in Walk(level.Root))
            {
                if (task is DeclaredTask rotate
                    && string.Equals(rotate.Type.Name, Igi2RotateAttachments.TaskType, StringComparison.Ordinal))
                {
                    declared++;
                    here++;
                }
            }

            if (here == 0)
            {
                continue;
            }

            levels++;
            (Igi2DrawableSceneResult built, _) = Igi2LevelScene.Build(level);
            Igi2RotateAttachments? spins = Igi2RotateAttachments.Build(level.Root, built.Attachments);
            bound += spins?.Count ?? 0;
        }

        Assert.Equal(18, levels);
        Assert.Equal(250, declared);

        // Seven fewer than declared, for two reasons, both of them the level data's.
        //
        // ONE is authored at a standstill -- all three speeds zero -- and binding it would make the
        // renderer rebuild a transform every frame to arrive back where it started.
        //
        // SIX name a part their object does not carry. `414_08_1` on an object whose model attaches
        // `414_07_1` three times and `414_10_1` once; `720_02_1` and `720_03_1` on one that attaches
        // `710_02_1` and `710_04_1`. Checked to the bottom of each tree: not one of those attachments
        // attaches anything itself, so the named part is nowhere under the object at any depth. Retail
        // walks the same list from the same ancestor and finds the same nothing, leaves its reference
        // null, and RotateAttachment_RunHandler returns on the first line. They do not turn there either.
        Assert.Equal(243, bound);
    }

    /// <summary>No part in the corpus turns about two axes at once.</summary>
    /// <remarks>
    /// Which is what lets the turn after <c>t</c> ticks be the authored angles multiplied by <c>t</c>:
    /// repeating a rotation about ONE axis is the same as turning that far in one go, and repeating a
    /// rotation about two is not. If a level ever authored one, this fails rather than drawing it
    /// wrongly, and the fix is the matrix power proper.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void No_part_turns_about_two_axes_at_once()
    {
        int turning = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (GameTask task in Walk(level.Root))
            {
                if (task is not DeclaredTask rotate
                    || !string.Equals(rotate.Type.Name, Igi2RotateAttachments.TaskType, StringComparison.Ordinal))
                {
                    continue;
                }

                int axes = 0;
                foreach (string name in new[] { "AlphaRotationSpeed", "BetaRotationSpeed", "GammaRotationSpeed" })
                {
                    if (rotate.TryGetValue(name, out object? raw) && raw is float speed && speed != 0f)
                    {
                        axes++;
                    }
                }

                Assert.InRange(axes, 0, 1);
                turning += axes;
            }
        }

        Assert.Equal(249, turning);
    }

    /// <summary>A bound part is turned from where the walk put it, and keeps turning.</summary>
    /// <remarks>
    /// The claim the whole thing rests on: the world orientation of a part is its parent's times its
    /// local one, only the local one turns, so turning the world orientation the walk already produced
    /// gives the same answer without recomposing anything up the tree.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_bound_part_turns_from_where_the_walk_put_it()
    {
        Igi2Level level = Igi2Level.All().First(l => l.Directory.EndsWith("level1", StringComparison.OrdinalIgnoreCase));
        (Igi2DrawableSceneResult built, _) = Igi2LevelScene.Build(level);
        Igi2RotateAttachments spins = Assert.IsType<Igi2RotateAttachments>(
            Igi2RotateAttachments.Build(level.Root, built.Attachments));

        WorldPlacement turning = built.Scene.Placements.First(
            p => spins.TryGetOrientation(p, 0f, out _));

        // At rest on tick zero, and the authored orientation is what it starts from.
        Assert.True(spins.TryGetOrientation(turning, 0f, out Matrix3f start));
        Assert.Equal(turning.Orientation, start);

        // Part way through the first tick it has already moved, and it is still a rotation.
        Assert.True(spins.TryGetOrientation(turning, 0.5f, out Matrix3f part));
        Assert.NotEqual(turning.Orientation, part);
        Assert.Equal(1f, part.Determinant, 3);

        // And a tick later it has moved further still.
        spins.Advance();
        Assert.True(spins.TryGetOrientation(turning, 0.5f, out Matrix3f later));
        Assert.NotEqual(part, later);
        Assert.Equal(1f, later.Determinant, 3);

        // Nothing else in the scene is driven by accident.
        Assert.False(spins.TryGetOrientation(built.Scene.Placements[0], 0f, out _));
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
