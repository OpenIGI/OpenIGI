using OpenIGI.Core.Graphics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A bone model played in its level's clips for the editor's model viewer.</summary>
public sealed class Igi1ModelAnimatorTests
{
    /// <summary>The soldier finds the level's clips, and some clip stands him up: taller than he is wide or deep.</summary>
    [RequiresGameDataFact]
    public void The_soldier_plays_and_stands_in_a_clip()
    {
        LoadedMission mission = RetailLevelOneGround.Mission();
        Assert.True(mission.TryGetModel("005_01_1", out MefModel? model));
        Igi1ModelAnimator animator = Assert.IsType<Igi1ModelAnimator>(Igi1ModelAnimator.For(model!, mission));
        Assert.True(animator.Clips.Count > 10);

        HashSet<int> drawn = [.. model!.Triangles().SelectMany(static t => (int[])[t.A, t.B, t.C])];
        Vertex3D[] vertices = [.. model.Vertices.ToArray().Select(static v => new Vertex3D(v.Position, v.Normal, v.DiffuseU, v.DiffuseV))];
        bool stands = false;
        for (int clip = 0; clip < animator.Clips.Count && !stands; clip++)
        {
            Assert.True(animator.Seconds(clip) > 0f);
            Assert.Equal(model.BaseVertexCount, animator.Pose(clip, 0f, vertices));
            float Extent(Func<Vertex3D, float> axis) => drawn.Max(i => axis(vertices[i])) - drawn.Min(i => axis(vertices[i]));
            float height = Extent(static v => v.Position.Z);
            stands = height > Extent(static v => v.Position.X) && height > Extent(static v => v.Position.Y);
        }

        Assert.True(stands);
    }

    /// <summary>A model that is not skinned has nothing to play.</summary>
    [RequiresGameDataFact]
    public void A_rigid_model_has_no_animator()
    {
        LoadedMission mission = RetailLevelOneGround.Mission();
        MefModel rigid = mission.LevelPack.Instances.Select(i => mission.LevelPack.TryGetModel(i, out MefModel? m) ? m : null)
            .First(static m => m is { Format: not MefMeshFormat.Skinned })!;
        Assert.Null(Igi1ModelAnimator.For(rigid, mission));
    }
}
