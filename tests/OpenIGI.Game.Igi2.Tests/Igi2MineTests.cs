using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A laid mine as <c>mine.c</c> runs it, and the frame a mine is laid along, <c>GameFunctions_CreateOrientationFromDirection</c>.</summary>
public sealed class Igi2MineTests
{
    /// <summary>
    /// A mine arms once its time is up, sounding as it does; a human in front of it within its radius sets it off, and it explodes
    /// once its countdown has run and goes.
    /// </summary>
    [Fact]
    public void A_mine_arms_is_set_off_counts_down_and_goes()
    {
        FakeMineWorld world = new();
        float[] up = Igi2Pose.FromDirection(new Vector3f(0f, 0f, 1f));
        Igi2Mine mine = new(world, new Vector3d(0, 0, 0), up, "none", armTicks: 3, timeBeforeExplode: 0.1f, detectionRadius: 2f, explodeRadius: 3f, isRemote: false);
        world.Lay(mine);

        for (int tick = 0; tick < 3; tick++)
        {
            Assert.True(mine.Run());
            Assert.False(mine.IsArmed);
        }

        Assert.True(mine.Run());
        Assert.True(mine.IsArmed);
        Assert.Equal([Igi2Mine.ArmSound], world.Sounds);

        // Behind it (its Y axis is up) a human is not seen; in front, within two metres and its own radius, it is.
        mine.Notify(null!, new Vector3d(0, 0, -4096), 100f);
        Assert.False(mine.IsTriggered);
        mine.Notify(null!, new Vector3d(0, 0, 4096), 100f);
        Assert.True(mine.IsTriggered);

        int ticks = 0;
        while (mine.Run())
        {
            ticks++;
            Assert.True(ticks < 10);
        }

        // The countdown is a tenth of a second, three ticks; it explodes on the tick after, and goes that tick.
        Assert.Equal(4, ticks);
        Assert.True(mine.Explosive.IsExploded);
        Assert.Empty(world.Laid);
        Assert.Empty(world.Notified.Items);
    }

    /// <summary>A frame laid along a direction has that direction for its Y axis, and is orthonormal, near vertical or not.</summary>
    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(0f, 0f, -1f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0.6f, 0.8f, 0f)]
    [InlineData(0.1f, 0.2f, 0.9746794f)]
    public void A_frame_from_a_direction_has_it_for_its_y_axis(float x, float y, float z)
    {
        float[] o = Igi2Pose.FromDirection(new Vector3f(x, y, z));
        Assert.Equal(x, o[1], 6);
        Assert.Equal(y, o[4], 6);
        Assert.Equal(z, o[7], 6);
        for (int a = 0; a < 3; a++)
        {
            for (int b = 0; b < 3; b++)
            {
                double dot = (o[a] * o[b]) + (o[3 + a] * o[3 + b]) + (o[6 + a] * o[6 + b]);
                Assert.Equal(a == b ? 1.0 : 0.0, dot, 5);
            }
        }
    }

    /// <summary>
    /// A mine's save state is its list's twenty-five values (<c>Mine_ptSerializeParameterList</c>): a model object's thirteen, an
    /// exploding object's four, then its flags, its two counts, its time, its radius and its three booleans, the last whether it
    /// is a remote one; it is kept with its parent, and one a load has deleted is neither saved nor run.
    /// </summary>
    [Fact]
    public void A_mine_is_saved_as_its_list_has_it()
    {
        FakeMineWorld world = new();
        Igi2Mine mine = new(world, new Vector3d(1, 2, 3), Igi2Pose.FromDirection(new Vector3f(0f, 0f, 1f)), string.Empty, 60, 0.5f, 1.5f, 3f, isRemote: true);
        OpenIGI.Game.Saves.StateWriter writer = new();
        mine.WriteState(writer);
        string[] values = writer.Arguments.Split(", ", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(25, values.Length);
        Assert.Equal(["60", "15", "0.5", "1.5", "FALSE", "FALSE", "TRUE"], values[18..]);
        Assert.True(((OpenIGI.Game.Saves.ISavedState)mine).IsSavedWithParent);
        Assert.True(mine.IsSaved);

        mine.Delete();
        Assert.False(mine.IsSaved);
        Assert.False(mine.Run());
    }

    private sealed class FakeMineWorld : IIgi2MineWorld
    {
        public Igi2CollisionWorld Collision { get; } = new([], null);

        public Func<string, MefModel?> Models { get; } = static _ => null;

        public Igi2ExplosionWorld? Explosions => null;

        public Igi2NotifyList Notified { get; } = new();

        public List<Igi2Mine> Laid { get; } = [];

        public List<string> Sounds { get; } = [];

        public void Lay(Igi2Mine mine)
        {
            Laid.Add(mine);
            Notified.Add(mine);
        }

        public void Remove(Igi2Mine mine)
        {
            Laid.Remove(mine);
            Notified.Remove(mine);
        }

        public void PlaySoundAt(string sound, Vector3d position) => Sounds.Add(sound);
    }
}
