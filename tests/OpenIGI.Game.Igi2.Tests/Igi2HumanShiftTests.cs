using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A body moved where it stands -- a co-op client's own player put right by its server -- stays moved.</summary>
public sealed class Igi2HumanShiftTests
{
    /// <summary>
    /// A body riding something turned (a ladder, a wire) and shifted is placed where it was shifted to on its next tick: it is
    /// placed from where it stands in the ladder's frame, and a correction made in the world alone was undone at once, so the
    /// server put a climbing client right on every snapshot.
    /// </summary>
    [Fact]
    public void A_rider_shifted_stays_where_it_was_shifted_to()
    {
        (Player.Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.OnFloor();
        Parent ladder = new(new Vector3d(10000, 20000, 0), EngineEuler.Rotation(0f, 0f, 1.2f));
        Vector3d start = new(10500, 20250, 4096);
        player.Position = start;
        player.AttachTo(ladder, start, EngineEuler.Rotation(0f, 0f, 0f));

        Vector3d offset = new(300, -200, 150);
        player.Shift(offset);
        player.FollowAttachment();

        Vector3d expected = start + offset;
        Assert.True(Vector3d.Distance(player.Position, expected) < 0.5, $"at {player.Position}, shifted to {expected}");
    }

    private sealed class Parent(Vector3d at, float[] orientation) : IAttachParent
    {
        public Vector3d AttachPosition => at;

        public IReadOnlyList<float> AttachOrientation => orientation;
    }
}
