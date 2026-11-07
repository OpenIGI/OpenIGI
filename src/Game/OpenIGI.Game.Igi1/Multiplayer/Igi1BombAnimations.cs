using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>
/// The bomb's plant and defuse for the first game's humans: I.G.I. 2's clips (<see cref="BombKit"/>) retargeted onto each
/// human set as it loads (<see cref="Igi2ClipRetarget"/>), under ids past the retail sets' own.
/// </summary>
public static class Igi1BombAnimations
{
    /// <summary>The kneeling plant, played once over the time the plant takes.</summary>
    public const int Plant = 900;

    /// <summary>The defuse, a loop.</summary>
    public const int Defuse = 901;

    /// <summary>The plant on the first-person arms, played over the plant's time as the body's is.</summary>
    public const int FirstPersonPlant = 902;

    // Where in the arms' clip the plant ends: the charge leaves the hand at 17,120, and the plant's last moment shows it just
    // set down, when the planted bomb appears in the world.
    private const float FirstPersonPlantSpan = 17600f;

    /// <summary>The bomb held on the first-person arms, standing.</summary>
    public const int FirstPersonHold = 903;

    /// <summary>The bomb held on the first-person arms, moving.</summary>
    public const int FirstPersonWalk = 904;

    /// <summary>The bomb in a body's hands, for its torso.</summary>
    public const int Hold = 905;

    // Each clip's bones as the sequel numbers them, for the rig it was added to, found when it was added.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, int[]> Matches = new();

    // The retail stand: what the bones the sequel's clips do not move (the head's end, the weapon's chain) hold.
    private const int Stand = 0;

    private static int _registered;

    /// <summary>Adds the clips to the first game's sets, once a process.</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            AnimationSetExtensions.Register(AssetFormatProfile.Igi1, For);
        }
    }

    /// <summary>Gets a clip's length, in the clip's own time.</summary>
    /// <param name="animation"><see cref="Plant"/> or <see cref="Defuse"/>.</param>
    /// <returns>Its length, 160 a tick.</returns>
    public static float Duration(int animation) => Clips.Value.TryGetValue(animation, out Igi2AnimationClip? clip) ? clip.TotalTime : 0f;

    /// <summary>Gets where the arms' plant is for where the body's is.</summary>
    /// <param name="bodyTime">The body's plant clip time, <see cref="Plant"/>'s.</param>
    /// <returns>The arms' clip time, <see cref="FirstPersonPlant"/>'s.</returns>
    public static float FirstPersonTime(float bodyTime) =>
        Math.Clamp(bodyTime / Math.Max(1f, Duration(Plant)), 0f, 1f) * Math.Min(FirstPersonPlantSpan, Duration(FirstPersonPlant));

    /// <summary>The models one of the bomb's clips holds at a time: the charge, and what the clip hangs beside it.</summary>
    /// <param name="pose">The posed rig the clip plays on, in its own frame: the arms' or a body's.</param>
    /// <param name="animation">The clip, one of this class's ids; any other holds nothing.</param>
    /// <param name="time">Its time.</param>
    /// <returns>Each model and where it stands in the pose's frame; none once the plant has set the charge down.</returns>
    public static IEnumerable<Igi2AttachedPart> InHand(BonePose pose, int animation, float time)
    {
        ArgumentNullException.ThrowIfNull(pose);
        if (!Matches.TryGetValue(animation, out int[]? match) || !Clips.Value.TryGetValue(animation, out Igi2AnimationClip? clip))
        {
            return [];
        }

        return Igi2AttachedParts.Of(clip, (int)time, Igi2ClipRetarget.ToClipPose(pose, match, clip), BombKit.HeldModel);
    }

    private static readonly Lazy<Dictionary<int, Igi2AnimationClip>> Clips = new(static () =>
    {
        Dictionary<int, Igi2AnimationClip> clips = [];
        if (BombKit.Clip(BombKit.PlantClip) is { } plant)
        {
            clips[Plant] = Igi2AnimationClip.Read(plant);
        }

        if (BombKit.Clip(BombKit.DefuseClip) is { } defuse)
        {
            clips[Defuse] = Igi2AnimationClip.Read(defuse);
        }

        foreach ((int id, string name) in (ReadOnlySpan<(int, string)>)[
            (FirstPersonPlant, BombKit.FirstPersonPlantClip),
            (FirstPersonHold, BombKit.FirstPersonHoldClip),
            (FirstPersonWalk, BombKit.FirstPersonWalkClip),
            (Hold, BombKit.HoldClip)])
        {
            if (BombKit.Clip(name) is { } bytes)
            {
                clips[id] = Igi2AnimationClip.Read(bytes);
            }
        }

        return clips;
    });

    // The clips, for a set they fit: a human's, every bone of the sequel's body found in it.
    private static List<BoneAnimation> For(BoneAnimationSet set)
    {
        List<BoneAnimation> added = [];
        Quaternion32[] stand = new Quaternion32[set.Skeleton.BoneCount];
        if (set.TryGet(Stand, out BoneAnimation standing))
        {
            for (int bone = 0; bone < stand.Length; bone++)
            {
                stand[bone] = BoneRotationSampler.Sample(standing.Rotations(bone), 0f, standing.Duration, standing.IsCyclic);
            }
        }

        // The best-matched clip of each bone tree lends its match to the others of that tree.
        List<(Igi2AnimationClip Clip, int[] Match)> best = [];
        foreach (Igi2AnimationClip clip in Clips.Value.Values)
        {
            int[] own = Igi2ClipRetarget.Match(clip, set.Skeleton);
            int at = best.FindIndex(b => Igi2ClipRetarget.SameTree(b.Clip, clip));
            if (at < 0)
            {
                best.Add((clip, own));
            }
            else if (own.Count(b => b >= 0) > best[at].Match.Count(b => b >= 0))
            {
                best[at] = (clip, own);
            }
        }

        foreach ((int id, Igi2AnimationClip clip) in Clips.Value)
        {
            int[] match = best.First(b => Igi2ClipRetarget.SameTree(b.Clip, clip)).Match;
            if (match.Count(b => b >= 0) < clip.BoneCount - 3)
            {
                continue;
            }

            Matches[id] = match;

            added.Add(Igi2ClipRetarget.ToBoneAnimation(clip, set.Skeleton, id, stand, match));
        }

        return added;
    }
}
