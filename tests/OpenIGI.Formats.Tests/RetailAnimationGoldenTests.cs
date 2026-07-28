using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Iff;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Walks every retail <c>common/anims/*.iff</c> and checks the whole animation format against them.
/// </summary>
/// <remarks>
/// The invariants here are the evidence the format is read correctly, not merely parsed without
/// throwing: chunk sizes must equal their declared counts, the hierarchy must be topologically sorted,
/// every rotation key must be a unit quaternion, and no rotation key may run past its clip's duration.
/// </remarks>
public sealed class RetailAnimationGoldenTests
{
    [RequiresGameDataFact]
    public void EverySetParsesWithTheExpectedShape()
    {
        (string Name, int Skeleton, int Bones, int Animations, int IdLimit)[] expected =
        [
            ("000", 0, 33, 122, 247),
            ("001", 1, 33, 95, 212),
            ("002", 2, 33, 18, 20),
            ("003", 3, 33, 47, 50),
            ("005", 5, 56, 78, 81),
            ("006", 6, 33, 16, 18),
        ];

        foreach ((string name, int skeleton, int bones, int animations, int idLimit) in expected)
        {
            BoneAnimationSet set = Load(name);

            Assert.Equal(skeleton, set.Skeleton.Id);
            Assert.Equal(bones, set.Skeleton.BoneCount);
            Assert.Equal(animations, set.Animations.Count);
            Assert.Equal(idLimit, set.IdLimit);

            // Every clip carries a rotation track per bone, which is what makes BORH/BORD pair up.
            Assert.All(set.Animations, a => Assert.Equal(bones, a.BoneCount));
        }
    }

    [RequiresGameDataFact]
    public void TheFileNameIsTheSkeletonIdAndFourIsMissing()
    {
        // The id inside BOSH matches the file's name rather than a load counter, so the gap at 004 is
        // a real hole in the numbering and nothing renumbers to close it.
        foreach (string name in Names)
        {
            Assert.Equal(int.Parse(name, CultureInfo.InvariantCulture), Load(name).Skeleton.Id);
        }

        Assert.DoesNotContain("004", Names);
    }

    [RequiresGameDataFact]
    public void EveryHierarchyIsTopologicallySorted()
    {
        foreach (string name in Names)
        {
            BoneSkeleton skeleton = Load(name).Skeleton;

            Assert.True(skeleton.IsTopologicallySorted(), name);
            Assert.Equal(BoneSkeleton.NoParent, skeleton.Parents[0]);
            Assert.Equal(skeleton.BoneCount, skeleton.RestTranslations.Count);
        }
    }

    [RequiresGameDataFact]
    public void EveryRotationKeyIsAUnitQuaternionInTimeOrder()
    {
        int keys = 0;
        foreach (string name in Names)
        {
            BoneAnimationSet set = Load(name);
            foreach (BoneAnimation animation in set.Animations)
            {
                for (int bone = 0; bone < animation.BoneCount; bone++)
                {
                    float previous = float.NegativeInfinity;
                    foreach (BoneRotationKey key in animation.Rotations(bone))
                    {
                        Quaternion32 q = key.Rotation;
                        float length = MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));
                        Assert.Equal(1f, length, 3);

                        Assert.True(key.Time >= previous, $"{name} anim {animation.Id} bone {bone}");
                        previous = key.Time;
                        keys++;
                    }
                }
            }
        }

        Assert.Equal(95947, keys);
    }

    [RequiresGameDataFact]
    public void NoRotationKeyRunsPastItsDurationButRootMotionMay()
    {
        int overrunningTranslationTracks = 0;
        int animations = 0;

        foreach (string name in Names)
        {
            foreach (BoneAnimation animation in Load(name).Animations)
            {
                animations++;

                for (int bone = 0; bone < animation.BoneCount; bone++)
                {
                    foreach (BoneRotationKey key in animation.Rotations(bone))
                    {
                        Assert.True(
                            key.Time <= animation.Duration + 0.001f,
                            $"{name} anim {animation.Id} bone {bone}: {key.Time} > {animation.Duration}");
                    }
                }

                if (animation.Translation.Count > 1)
                {
                    float last = animation.Translation[^1].Time;
                    if (last > animation.Duration + 0.001f)
                    {
                        // The only overrun that occurs is exactly one frame step — the displacement at
                        // the end of the cycle, so a loop can take the whole cycle's travel.
                        Assert.Equal(animation.Duration + BoneAnimation.FrameStep, last, 2);
                        overrunningTranslationTracks++;
                    }
                }
            }
        }

        Assert.Equal(376, animations);
        Assert.Equal(96, overrunningTranslationTracks);
    }

    [RequiresGameDataFact]
    public void EveryDurationIsAMultipleOfTheFrameStep()
    {
        foreach (string name in Names)
        {
            foreach (BoneAnimation animation in Load(name).Animations)
            {
                Assert.Equal(0f, animation.Duration % BoneAnimation.FrameStep);
            }
        }
    }

    [RequiresGameDataFact]
    public void TheIdTableIsSparseAndIdsStartAtTwo()
    {
        foreach (string name in Names)
        {
            BoneAnimationSet set = Load(name);

            // Ids 0 and 1 are never used, in any set.
            Assert.False(set.TryGet(0, out _), name);
            Assert.False(set.TryGet(1, out _), name);

            foreach (BoneAnimation animation in set.Animations)
            {
                Assert.True(animation.Id >= 2);
                Assert.True(set.TryGet(animation.Id, out BoneAnimation found));
                Assert.Same(animation, found);
            }

            // The limit really is a limit and not a count: it exceeds the number of clips.
            Assert.True(set.IdLimit > set.Animations.Count, name);
            Assert.False(set.TryGet(set.IdLimit, out _));
        }
    }

    [RequiresGameDataFact]
    public void SingleKeyTracksCarryNoTangents()
    {
        // The loader reads a one-key track short, so the tangents in the chunk are never consumed. A
        // rotation key's tangents fall back to its own value and a translation key's to zero.
        int shortRotationTracks = 0;

        foreach (string name in Names)
        {
            foreach (BoneAnimation animation in Load(name).Animations)
            {
                if (animation.Translation.Count == 1)
                {
                    BoneTranslationKey key = animation.Translation[0];
                    Assert.Equal(default, key.InTangent);
                    Assert.Equal(default, key.OutTangent);
                }

                for (int bone = 0; bone < animation.BoneCount; bone++)
                {
                    IReadOnlyList<BoneRotationKey> track = animation.Rotations(bone);
                    if (track.Count != 1)
                    {
                        continue;
                    }

                    Assert.Equal(track[0].Rotation, track[0].InTangent);
                    Assert.Equal(track[0].Rotation, track[0].OutTangent);
                    shortRotationTracks++;
                }
            }
        }

        Assert.True(shortRotationTracks > 0);
    }

    [RequiresGameDataFact]
    public void EventsCarryAnIdATimeAndAPlace()
    {
        int events = 0;
        foreach (string name in Names)
        {
            BoneAnimationSet set = Load(name);
            foreach (BoneAnimation animation in set.Animations)
            {
                foreach (BoneAnimationEvent e in animation.Events)
                {
                    // Ids are not all positive: 000.iff's animation 84 carries a single event with
                    // id 0, so "an id is always a real trigger" is not an invariant to lean on.
                    Assert.True(e.EventId >= 0);
                    Assert.True(e.Time >= 0f);
                    Assert.True(e.Time <= animation.Duration + BoneAnimation.FrameStep);

                    // The second field is only ever -1 or a valid bone index for this set's own
                    // skeleton — which is the whole basis for reading it as a bone. A value outside
                    // that range would sink the inference.
                    Assert.True(
                        e.BoneIndex == BoneAnimationEvent.NoBone
                            || (e.BoneIndex >= 0 && e.BoneIndex < set.Skeleton.BoneCount),
                        $"{name} anim {animation.Id}: bone {e.BoneIndex} of {set.Skeleton.BoneCount}");
                    events++;
                }
            }
        }

        Assert.Equal(245, events);
    }

    [RequiresGameDataFact]
    public void TheCommonestEventFavoursASymmetricPairOfBones()
    {
        // Event 1010 is the bulk of the retail events. Where it names a bone it overwhelmingly names
        // 22 or 27, and exactly evenly — 20 each — which is what a left/right pair looks like. Bones
        // 25 and 30 appear once apiece, so this is a strong tendency rather than a clean split, and
        // the test says so rather than overstating it.
        Dictionary<int, int> byBone = [];
        int occurrences = 0;

        foreach (string name in Names)
        {
            foreach (BoneAnimation animation in Load(name).Animations)
            {
                foreach (BoneAnimationEvent e in animation.Events)
                {
                    if (e.EventId != 1010)
                    {
                        continue;
                    }

                    occurrences++;
                    byBone[e.BoneIndex] = byBone.GetValueOrDefault(e.BoneIndex) + 1;
                }
            }
        }

        Assert.Equal(117, occurrences);
        Assert.Equal(20, byBone[22]);
        Assert.Equal(20, byBone[27]);
        Assert.Equal(75, byBone[0]);
    }

    [RequiresGameDataFact]
    public void AFileReadsAsExactlyOneTopLevelChunk()
    {
        // The retail sets do not back-patch the outermost FORM's size, so a reader that advances by
        // that size walks back into the animation list and yields every chunk in it a second time.
        // One root per file is what says the correction is being applied.
        foreach (string name in Names)
        {
            string path = Path.Combine(GameData.RequireRoot(), "common", "anims", $"{name}.iff");
            IReadOnlyList<IffChunk> roots = IffFile.Read(File.ReadAllBytes(path));

            Assert.Single(roots);
            Assert.Equal(BoneAnimationTags.BoneObject, roots[0].FormType);
        }
    }

    private static string[] Names => ["000", "001", "002", "003", "005", "006"];

    private static BoneAnimationSet Load(string name)
    {
        string path = Path.Combine(GameData.RequireRoot(), "common", "anims", $"{name}.iff");
        return BoneAnimationSet.Read(File.ReadAllBytes(path));
    }
}
