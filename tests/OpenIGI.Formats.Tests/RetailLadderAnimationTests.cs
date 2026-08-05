using OpenIGI.Formats.Animation;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Pins the three authored clips used by IGI1's ladder locomotion states.</summary>
public sealed class RetailLadderAnimationTests
{
    [RequiresGameDataFact]
    public void TheThreeLadderStatesUseTheAuthoredRetailClips()
    {
        string path = Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff");
        BoneAnimationSet set = BoneAnimationSet.Read(File.ReadAllBytes(path));

        Assert.True(set.TryGet(168, out BoneAnimation climb));
        Assert.Equal(3360f, climb.Duration);
        Assert.True(climb.IsCyclic);
        Assert.Equal(7, climb.Translation.Count);
        Assert.Collection(
            climb.Events,
            static e => Assert.Equal(0f, e.Time),
            static e => Assert.Equal(1737f, e.Time));
        Assert.All(climb.Events, static e => Assert.Equal(8, e.EventId));

        Assert.True(set.TryGet(169, out BoneAnimation slide));
        Assert.Equal(640f, slide.Duration);
        Assert.Single(slide.Translation); // a fixed pose; state 40's damped velocity supplies the fall

        Assert.True(set.TryGet(170, out BoneAnimation top));
        Assert.Equal(11200f, top.Duration);
        Assert.Equal(15, top.Translation.Count);
        Assert.True(top.Translation[^1].Position.Z - top.Translation[0].Position.Z > 7000f);
        Assert.True(top.Translation[^1].Position.Y - top.Translation[0].Position.Y > 3000f);
    }

}
