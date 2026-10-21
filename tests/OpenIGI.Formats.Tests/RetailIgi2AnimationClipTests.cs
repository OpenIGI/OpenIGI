using OpenIGI.Formats.Animation;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Every animation clip I.G.I. 2 ships, read and accounted for.</summary>
/// <remarks>
/// The clips live as <c>anims/*.iff</c> entries inside <c>COMMON/models/{common,new}.res</c>. The
/// assertions are the corpus measurements the reader's model rests on, so a reader that drifts from the
/// format fails here rather than as a soldier frozen in a bind pose.
/// </remarks>
public sealed class RetailIgi2AnimationClipTests
{
    /// <summary>Every clip reads, and every record in it belongs to exactly one place.</summary>
    /// <remarks>
    /// The header's event count is the stream records plus the tail. Each tail record is a track key, and
    /// every key is reached by following its track's links, so the count must equal the keys of every
    /// track plus the triggers, the links and the one end record. A record the model drops, or a link it
    /// follows to the wrong place, breaks the sum.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_clip_reads_and_accounts_for_every_record()
    {
        int clips = 0;
        int looping = 0;
        foreach ((string name, Igi2AnimationClip clip) in Igi2CommonModels.Clips())
        {
            clips++;
            looping += clip.Loops ? 1 : 0;

            int keys = clip.Translation.Count + clip.Triggers.Count + clip.Links.Count + clip.InertTailLinks + 1;
            for (int bone = 0; bone < clip.BoneCount; bone++)
            {
                keys += clip.Rotations(bone).Count;
            }

            foreach (Igi2AnimationAttachObject attach in clip.AttachObjects)
            {
                keys += attach.Keys.Count;
            }

            Assert.True(clip.DeclaredEventCount == keys, $"{name}: header says {clip.DeclaredEventCount} events, tracks hold {keys}.");
            Assert.Equal(clip.BoneCount, clip.Hierarchy.ChildCounts.Count);
            Assert.Equal(clip.BoneCount, clip.Hierarchy.Translations.Count);
            Assert.Equal(clip.BoneCount, Igi2BoneHierarchy.CountBones([.. clip.Hierarchy.ChildCounts]));

            // The hierarchy's root entry is never applied, and it is the root track's first key to within
            // float rounding (a few clips differ in the last bit).
            if (clip.Translation.Count > 0)
            {
                Assert.Equal(clip.Translation[0].Translation.X, clip.Hierarchy.Translations[0].X, 0.001f);
                Assert.Equal(clip.Translation[0].Translation.Y, clip.Hierarchy.Translations[0].Y, 0.001f);
                Assert.Equal(clip.Translation[0].Translation.Z, clip.Hierarchy.Translations[0].Z, 0.001f);
            }

            // Breadth-first order puts every parent before its children.
            for (int bone = 1; bone < clip.BoneCount; bone++)
            {
                Assert.InRange(clip.Hierarchy.Parents[bone], 0, bone - 1);
            }
        }

        Assert.Equal(1244, clips);
        Assert.Equal(405, looping);
    }

    /// <summary>A looping track closes on a tail key past the end record, carrying its first key's value.</summary>
    /// <remarks>
    /// <c>005_02</c> loops over 9440 units with root keys at 0 and 4800; the 4800 key links past the end
    /// record to a key at 9600 that repeats key 0, which is what the track interpolates toward.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_looping_track_closes_on_its_tail_key()
    {
        Igi2AnimationClip clip = Clip("005_02");

        Assert.True(clip.Loops);
        Assert.Equal(9440, clip.TotalTime);
        Assert.Equal([0, 4800, 9600], clip.Translation.Select(static k => k.Time));
        Assert.Equal(clip.Translation[0].Translation, clip.Translation[2].Translation);
    }

    /// <summary>One clip's header, read field by field.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_parachute_cord_clip_reads_as_its_header_says()
    {
        Igi2AnimationClip clip = Clip("preripcord");

        Assert.Equal("preripcord", clip.Name);
        Assert.False(clip.Loops);
        Assert.Equal(3200, clip.TotalTime);
        Assert.Equal(31, clip.BoneCount);
        Assert.Equal(2, clip.AttachObjects.Count);
        Assert.Equal(-1, clip.Hierarchy.Parents[0]);
        Assert.Equal([0, 0, 0], clip.Hierarchy.Parents.Skip(1).Take(3));
    }

    /// <summary>The child counts are read breadth-first.</summary>
    [Fact]
    public void Child_counts_read_breadth_first()
    {
        // Root with two children; its first child has two, its second none.
        Assert.Equal([-1, 0, 0, 1, 1], Igi2BoneHierarchy.ParentsOf([2, 2, 0, 0, 0]));
    }

    /// <summary>Counts that place the wrong number of bones are refused.</summary>
    [Fact]
    public void Counts_that_do_not_make_one_tree_are_refused()
    {
        Assert.Throws<ArgumentException>(() => Igi2BoneHierarchy.ParentsOf([1, 0, 0]));
        Assert.Throws<ArgumentException>(() => Igi2BoneHierarchy.ParentsOf([3, 0, 0]));
    }

    private static Igi2AnimationClip Clip(string name) =>
        Igi2CommonModels.Clips().Single(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).Clip;
}
