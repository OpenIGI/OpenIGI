using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>The binoculars mark another game's humans in the level as they mark the level's own soldiers.</summary>
public sealed class JoinedBinocularTests
{
    /// <summary>A living human in view is marked; a dead one and one out of the world are not.</summary>
    [Fact]
    public void A_joined_human_in_view_is_marked_and_the_dead_and_the_absent_are_not()
    {
        SoldierSimulation soldiers = new(NullLogger.Instance);
        LevelJoiners joined = new();
        soldiers.Joined = joined;
        joined.JoinSeen(new Human(new Vector3d(0, 10 * 4096, 0), isAlive: true, isActive: true));
        joined.JoinSeen(new Human(new Vector3d(2 * 4096, 12 * 4096, 0), isAlive: false, isActive: true));
        joined.JoinSeen(new Human(new Vector3d(-2 * 4096, 12 * 4096, 0), isAlive: true, isActive: false));

        IReadOnlyList<EnemyOverlayBox> marked = soldiers.BinocularTargets(Camera3D.Default, 0.5f, 1280, 720, tick: 1, float.PositiveInfinity);

        Assert.Single(marked);

        // The one marked is what its own game glows through walls, in its side's colour.
        Assert.Equal(BinocularMarkerColor.Hostile, Assert.Single(soldiers.BinocularJoinedDetected).Value);
    }

    // A human standing with its feet at a point and its eyes 1.7 metres up.
    private sealed class Human(Vector3d feet, bool isAlive, bool isActive) : IAiDetectable
    {
        public Vector3d DetectablePosition => feet;

        public float DetectableRadius => 4096f;

        public float? VisibilityFactor => 1f;

        public bool IsGone => false;

        public AiDetectionInfo DetectionInfo() =>
            new(AiDetectableKind.Human, feet + new Vector3d(0, 0, 1.7 * 4096), feet, feet, 1, isAlive, isActive);
    }
}
