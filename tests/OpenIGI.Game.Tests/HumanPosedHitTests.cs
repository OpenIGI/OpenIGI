using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Casting at a <b>posed</b> human, which is the path a soldier is shot through.
/// </summary>
/// <remarks>
/// Every case in <see cref="HumanHitTests"/> passes <c>pose: null</c>, so the bone resolution and the
/// head-proximity promotion were never exercised together. That gap is why a promotion bug could reach
/// the game: see the original.
/// </remarks>
public sealed class HumanPosedHitTests
{
    private const double Metre = 4096.0;

    private static readonly Vector3d Feet = new(0.0, 0.0, 0.0);
    private static readonly Vector3f FacingNorth = new(0f, 1f, 0f);

    /// <summary>A shot through the knee must stay a leg hit, not become the lethal head zone.</summary>
    [Fact]
    public void AShotThroughTheKneeIsALegHit()
    {
        HumanHit hit = ShootHorizontallyAt(0.5);

        Assert.NotEqual(HumanDamage.ProximityZone, hit.Zone);
        Assert.NotEqual(HumanDamage.NeckZone, hit.Zone);
    }

    /// <summary>And so must a shot through the stomach.</summary>
    [Fact]
    public void AShotThroughTheStomachIsATrunkHit()
    {
        HumanHit hit = ShootHorizontallyAt(1.1);

        Assert.NotEqual(HumanDamage.ProximityZone, hit.Zone);
        Assert.NotEqual(HumanDamage.NeckZone, hit.Zone);
    }

    /// <summary>A shot at the proximity bone still takes the lethal zone — the promotion must work.</summary>
    [Fact]
    public void AShotThroughTheHeadTakesTheProximityZone()
    {
        HumanHit hit = ShootHorizontallyAt(1.55);

        Assert.Equal(HumanDamage.ProximityZone, hit.Zone);
    }

    /// <summary>
    /// The promotion is a 15 cm sphere about bone 4, so it must not reach the trunk or the legs.
    /// </summary>
    [Fact]
    public void TheProximityPromotionDoesNotReachTheWholeBody()
    {
        int promoted = 0;
        for (double height = 0.2; height <= 1.2; height += 0.1)
        {
            if (ShootHorizontallyAt(height).Zone == HumanDamage.ProximityZone)
            {
                promoted++;
            }
        }

        Assert.Equal(0, promoted);
    }

    private static HumanHit ShootHorizontallyAt(double heightInMetres)
    {
        // Straight at the body FROM THE FRONT, level, at the given height. The direction matters: the
        // promotion is refused for a round arriving from behind (localForward >= 0), so a test that
        // shot the body in the back would pass without exercising the promotion at all.
        Vector3d origin = new(0.0, 8.0 * Metre, heightInMetres * Metre);
        Vector3d end = new(0.0, -8.0 * Metre, heightInMetres * Metre);

        Assert.True(
            HumanHitTest.TryCast(origin, end, Feet, FacingNorth, Rig(), out HumanHit hit),
            $"the cast missed the body entirely at {heightInMetres} m");

        return hit;
    }

    /// <summary>
    /// A humanoid rig laid out the way the 33-bone human rig is: spine 0–3, neck and head 4–7, the two
    /// arms, the three-bone weapon chain at 20–22, then the two legs.
    /// </summary>
    private static BonePose Rig()
    {
        int[] parents = new int[33];
        Vector3f[] rest = new Vector3f[33];

        // Heights in metres, turned into the rest translation of each bone from its parent.
        // 0 root at the hips, up through the spine to the head; the limbs hang off their own roots.
        double[] spine = [1.0, 1.15, 1.30, 1.45];      // 0..3 pelvis and spine
        double[] neckHead = [1.55, 1.65, 1.75, 1.80];  // 4..7 neck base, neck, head, crown

        parents[0] = BoneSkeleton.NoParent;
        rest[0] = new Vector3f(0f, 0f, (float)(spine[0] * Metre));
        for (int i = 1; i < 4; i++)
        {
            parents[i] = i - 1;
            rest[i] = new Vector3f(0f, 0f, (float)((spine[i] - spine[i - 1]) * Metre));
        }

        parents[4] = 3;
        rest[4] = new Vector3f(0f, 0f, (float)((neckHead[0] - spine[3]) * Metre));
        for (int i = 5; i < 8; i++)
        {
            parents[i] = i - 1;
            rest[i] = new Vector3f(0f, 0f, (float)((neckHead[i - 4] - neckHead[i - 5]) * Metre));
        }

        // Arms off the top of the spine, running sideways and down.
        BuildLimb(parents, rest, first: 8, root: 3, sideways: +0.2, from: 1.45, to: 1.05);
        BuildLimb(parents, rest, first: 14, root: 3, sideways: -0.2, from: 1.45, to: 1.05);

        // The weapon chain: three bones running more than a metre FORWARD of the chest, carrying no
        // geometry. This is what a frontal shot passes closest to, so it has to be excluded by index.
        parents[20] = 1;
        rest[20] = new Vector3f(0f, (float)(0.4 * Metre), 0f);
        parents[21] = 20;
        rest[21] = new Vector3f(0f, (float)(0.35 * Metre), 0f);
        parents[22] = 21;
        rest[22] = new Vector3f(0f, (float)(0.33 * Metre), 0f);

        // Legs off the root, running down to the floor.
        BuildLimb(parents, rest, first: 23, root: 0, sideways: +0.1, from: 1.0, to: 0.0);
        BuildLimb(parents, rest, first: 28, root: 0, sideways: -0.1, from: 1.0, to: 0.0);

        Quaternion32[] identity = new Quaternion32[33];
        for (int i = 0; i < identity.Length; i++)
        {
            identity[i] = new Quaternion32(0f, 0f, 0f, 1f);
        }

        return BonePose.Evaluate(BoneSkeleton.Create(parents, rest), identity);
    }

    private static void BuildLimb(
        int[] parents, Vector3f[] rest, int first, int root, double sideways, double from, double to)
    {
        int count = first is 8 or 14 ? 6 : 5;
        double step = (to - from) / count;

        parents[first] = root;
        rest[first] = new Vector3f((float)(sideways * Metre), 0f, (float)(step * Metre));
        for (int i = 1; i < count; i++)
        {
            parents[first + i] = first + i - 1;
            rest[first + i] = new Vector3f(0f, 0f, (float)(step * Metre));
        }
    }
}
