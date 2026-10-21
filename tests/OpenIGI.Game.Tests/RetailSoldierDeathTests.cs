using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The measurement that settles which clips a killed soldier may play, over the retail animation sets.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that would have caught the floating corpse. The port used to play the two literal
/// ids 24 and 25, taken from the soldier class's two <em>special</em> death states. Neither is a
/// stand-up-to-ground fall; the five clips the <em>general</em> state names are. Asserting that in terms
/// of the root track's own Z is what makes "the body ends up on the ground" a property that can be
/// checked without looking at the screen.
/// </para>
/// </remarks>
public sealed class RetailSoldierDeathTests
{
    /// <summary>The set every retail death clip lives in — skeleton 1, which is what level 1's guards use.</summary>
    private const string DeathSet = "001";

    /// <summary>
    /// Every clip the general death state can name exists in the death set and takes the skeleton root
    /// from standing height down to the ground.
    /// </summary>
    /// <remarks>
    /// A retail skeleton's bone 0 rest translation is zero, so the root track's Z <em>is</em> the pelvis
    /// height above the body's origin: about 3877 units (0.95 m) standing. A fall has to end near zero,
    /// and all five do — between 524 and 772 units, which is a body lying on the floor.
    /// </remarks>
    [RequiresGameDataFact]
    public void EveryDeathClipTheStateCanNameEndsOnTheGround()
    {
        BoneAnimationSet set = Load(DeathSet);

        int[] named =
        [
            SoldierDeath.ThrownBackwardsAnimation,
            SoldierDeath.ThrownForwardsAnimation,
            SoldierDeath.CrumpleAnimations[0],
            SoldierDeath.CrumpleAnimations[1],
            HumanHitZoneTable.Soldier[SoldierDeath.HeadZone].ReactionA,
            HumanHitZoneTable.Soldier[SoldierDeath.NeckZone].ReactionA,
        ];

        foreach (int id in named)
        {
            Assert.True(set.TryGet(id, out BoneAnimation clip), $"Animation {id} is missing from set {DeathSet}.");

            ReadOnlySpan<BoneTranslationKey> root = clip.Translation;
            Assert.True(root.Length > 1, $"Animation {id} has no root motion to fall with.");

            float start = root[0].Position.Z;
            float end = root[root.Length - 1].Position.Z;

            // Starts standing, ends on the floor. 2000 units is 0.49 m -- a generous halfway line that
            // separates every one of these from the two literals below.
            Assert.True(start > 2000f, $"Animation {id} starts at root Z {start}, which is not standing.");
            Assert.True(end < 2000f, $"Animation {id} ends at root Z {end}, which is not on the ground.");
        }
    }

    /// <summary>
    /// The two literals the port used to play are <b>not</b> falls, which is the measurement that
    /// identifies the bug rather than merely fixing it.
    /// </summary>
    /// <remarks>
    /// Clip 24 starts from a crouch, so it never lowers a standing body far enough; clip 25 is a fifteen
    /// tick twitch that starts and ends on the floor, so a standing body playing it never moves down at
    /// all. Either way the corpse keeps the height it was shot at.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheTwoLiteralDeathClipsAreNotFallsFromStanding()
    {
        BoneAnimationSet set = Load(DeathSet);

        foreach (int id in SoldierView.LiteralDeathAnimations)
        {
            Assert.True(set.TryGet(id, out BoneAnimation clip));
            float start = clip.Translation[0].Position.Z;
            Assert.True(
                start < 2500f,
                $"Animation {id} starts at root Z {start}; it was supposed not to start from standing.");
        }
    }

    /// <summary>
    /// The solver's correction, run against the real death clips, never leaves a body travelling further
    /// than the obstruction it found.
    /// </summary>
    /// <remarks>
    /// Run for every clip the state can name, against a wall placed at a range of distances. The property
    /// asserted is the one the arithmetic is for: the clip's own stride plus the correction summed over
    /// the clip's duration never exceeds the gap the rays measured.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheCorrectionNeverLetsABodyTravelPastWhatItHit()
    {
        BoneAnimationSet set = Load(DeathSet);

        foreach (int id in new[] { SoldierDeath.ThrownBackwardsAnimation, SoldierDeath.ThrownForwardsAnimation, 22, 23, 30 })
        {
            Assert.True(set.TryGet(id, out BoneAnimation clip));

            Vector3d stride = SoldierDeathFall.Direction(clip, facing: 0f, out double reach);
            if (reach <= 0.0)
            {
                continue;
            }

            for (double gap = 0.0; gap < reach; gap += reach / 8.0)
            {
                double distance = gap;
                Vector3d velocity = SoldierDeathFall.Solve(
                    clip,
                    new Vector3d(0.0, 0.0, 0.0),
                    facing: 0f,
                    (from, to) =>
                    {
                        double length = Math.Sqrt(
                            ((to.X - from.X) * (to.X - from.X)) + ((to.Y - from.Y) * (to.Y - from.Y)));
                        if (length <= 0.0)
                        {
                            return default;
                        }

                        double t = distance / length;
                        return new SoldierDeathRay(
                            true,
                            new Vector3d(
                                from.X + ((to.X - from.X) * t),
                                from.Y + ((to.Y - from.Y) * t),
                                from.Z),
                            distance);
                    });

                double ticks = clip.Duration * (double)SoldierDeathFall.SecondsPerDurationUnit
                    * SoldierDeathFall.TicksPerSecond;

                // Where the body ends up along its own line: the extended direction plus the summed
                // correction. It must not be past the obstruction, give or take float noise.
                double travelled = Length(stride) + (Dot(velocity, Normalise(stride)) * ticks);
                Assert.True(
                    travelled <= gap + 1.0,
                    $"Animation {id} travelled {travelled:F1} into an obstruction {gap:F1} away.");
            }
        }
    }

    /// <summary>The clip a shotgun kill names differs from the one an assault rifle names.</summary>
    /// <remarks>
    /// Joined up, so the whole path is asserted rather than each half: category to mode, mode plus zone
    /// to clip, clip to a real animation in the set.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheWholeChoiceResolvesToARealClip()
    {
        BoneAnimationSet set = Load(DeathSet);

        foreach (WeaponCategory category in new[] { WeaponCategory.Gun, WeaponCategory.Shotgun })
        {
            foreach (int zone in new[] { 0, 3, SoldierDeath.HeadZone, SoldierDeath.NeckZone })
            {
                foreach (bool front in new[] { false, true })
                {
                    foreach (int flip in new[] { 0, 1 })
                    {
                        int id = SoldierDeath.Choose(
                            SoldierDeathClips.Soldier,
                            SoldierDeath.ModeFor(category), zone, HumanHitZoneTable.Soldier, front, flip);
                        Assert.True(set.TryGet(id, out _), $"{category}/{zone}/{front}/{flip} named {id}.");
                    }
                }
            }
        }
    }

    private static double Length(Vector3d v) => Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

    private static double Dot(Vector3d a, Vector3d b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static Vector3d Normalise(Vector3d v)
    {
        double length = Length(v);
        return length == 0.0 ? default : new Vector3d(v.X / length, v.Y / length, v.Z / length);
    }

    private static BoneAnimationSet Load(string name)
    {
        string path = Path.Combine(GameData.RequireRoot(), "common", "anims", $"{name}.iff");
        return BoneAnimationSet.Read(File.ReadAllBytes(path));
    }
}
