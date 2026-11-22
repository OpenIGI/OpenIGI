using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Project I.G.I.'s walk carried with the code (<see cref="HumanLocomotionRootMotion"/>) against the one <c>000.iff</c> gives: the
/// same numbers, and a body walking on each to the same place every tick.
/// </summary>
public sealed class HumanLocomotionRootMotionTests
{
    /// <summary>Set to 1 to have <see cref="WriteTheBakedClips"/> write the data file instead of checking it.</summary>
    private const string WriteVariable = "OPENIGI_WRITE_ROOT_MOTION";

    private const string DataFile = "src/Game/OpenIGI.Game/Player/HumanLocomotionRootMotion.Data.cs";

    /// <summary>How far from the start the ground stays level at zero; past it, it is twenty thousand units lower.</summary>
    private const double LedgeRadius = 60000d;

    /// <summary>The data file is what <c>000.iff</c> bakes to; with <see cref="WriteVariable"/> set it is written instead.</summary>
    [RequiresGameDataFact]
    public void WriteTheBakedClips()
    {
        string source = Source(HumanLocomotionRootMotion.Bake(RetailSet()));
        string path = Path.Combine(RepositoryRoot(), DataFile);
        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
        {
            File.WriteAllText(path, source);
            return;
        }

        Assert.Equal(source, File.ReadAllText(path).ReplaceLineEndings("\n"));
    }

    /// <summary>Every clip the walk plays is carried, with its root keys, length, flags and events to the bit.</summary>
    [RequiresGameDataFact]
    public void TheCarriedClipsAreTheRetailOnes()
    {
        BoneAnimationSet retail = RetailSet();
        BoneAnimationSet carried = HumanLocomotionRootMotion.Retail;
        foreach (int id in HumanLocomotionRootMotion.AnimationIds)
        {
            Assert.True(retail.TryGet(id, out BoneAnimation expected), $"000.iff has no animation {id}.");
            Assert.True(carried.TryGet(id, out BoneAnimation actual), $"The carried walk has no animation {id}.");
            Assert.Equal(Bits(expected.Duration), Bits(actual.Duration));
            Assert.Equal(expected.Flags, actual.Flags);
            Assert.Equal(expected.Translation.ToArray().Select(Bits), actual.Translation.ToArray().Select(Bits));
            Assert.Equal(expected.Events, actual.Events);
        }
    }

    /// <summary>
    /// A body walking on the carried clips stands where one walking on <c>000.iff</c> stands, every tick: runs and walks in all
    /// eight directions, a turn at a run, the walk/run latch, a crouch and a crouch-walk, jumps from a stand and from a run, and a
    /// run off a ledge.
    /// </summary>
    [RequiresGameDataFact]
    public void ABodyWalksTheSameOnTheCarriedClips()
    {
        (PlayerBody body, HumanLocomotionAnimator animator) retail = Stand(new HumanLocomotionAnimator(RetailSet()));
        (PlayerBody body, HumanLocomotionAnimator animator) carried = Stand(new HumanLocomotionAnimator(HumanLocomotionRootMotion.Retail));

        int tick = 0;
        foreach (PlayerCommand command in Script())
        {
            retail.body.TickAnimated(command, terrain: null, retail.animator);
            carried.body.TickAnimated(command, terrain: null, carried.animator);
            tick++;

            Assert.True(retail.body.Position == carried.body.Position, $"Tick {tick}: {retail.body.Position} against {carried.body.Position}.");
            Assert.Equal(retail.animator.State, carried.animator.State);
            Assert.Equal(retail.body.IsGrounded, carried.body.IsGrounded);
            Assert.Equal(retail.body.IsCrouched, carried.body.IsCrouched);
            Assert.Equal(Bits(retail.body.Yaw), Bits(carried.body.Yaw));
        }

        // The script reached the ledge and went over it.
        Assert.True(Across(retail.body.Position) > LedgeRadius && retail.body.Position.Z < -1000d, $"The run ended at {retail.body.Position}.");
    }

    private static (PlayerBody Body, HumanLocomotionAnimator Animator) Stand(HumanLocomotionAnimator animator)
    {
        PlayerBody body = new() { Position = new Vector3d(0d, 0d, 1000d), Walls = Ground };
        Assert.True(body.PlaceOnGround(terrain: null));
        return (body, animator);
    }

    private static IEnumerable<PlayerCommand> Script()
    {
        PlayerCommand still = new(0f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: false);
        (float Forward, float Strafe)[] directions = [(1f, 0f), (1f, 1f), (0f, 1f), (-1f, 1f), (-1f, 0f), (-1f, -1f), (0f, -1f), (1f, -1f)];
        foreach (bool run in new[] { true, false })
        {
            foreach ((float forward, float strafe) in directions)
            {
                foreach (PlayerCommand c in Hold(still with { Forward = forward, Strafe = strafe, Run = run }, 25))
                {
                    yield return c;
                }

                foreach (PlayerCommand c in Hold(still with { Run = run }, 8))
                {
                    yield return c;
                }
            }
        }

        // A turn at a run, then the crouch and a crouch-walk, each way.
        foreach (PlayerCommand c in Hold(still with { Forward = 1f, YawDelta = 0.06f }, 30))
        {
            yield return c;
        }

        foreach (PlayerCommand c in Hold(still with { Crouch = true }, 15))
        {
            yield return c;
        }

        foreach ((float forward, float strafe) in directions)
        {
            foreach (PlayerCommand c in Hold(still with { Forward = forward, Strafe = strafe, Crouch = true }, 20))
            {
                yield return c;
            }
        }

        // Jumps: from a stand, then from a run.
        foreach (PlayerCommand c in Jump(still))
        {
            yield return c;
        }

        foreach (PlayerCommand c in Hold(still with { Forward = 1f }, 20))
        {
            yield return c;
        }

        foreach (PlayerCommand c in Jump(still with { Forward = 1f }))
        {
            yield return c;
        }

        // Run on until the ground gives out.
        foreach (PlayerCommand c in Hold(still with { Forward = 1f }, 400))
        {
            yield return c;
        }
    }

    private static IEnumerable<PlayerCommand> Jump(PlayerCommand held)
    {
        yield return held with { Jump = true };
        foreach (PlayerCommand c in Hold(held, 45))
        {
            yield return c;
        }
    }

    private static IEnumerable<PlayerCommand> Hold(PlayerCommand command, int ticks) => Enumerable.Repeat(command, ticks);

    private static double Across(Vector3d at) => Math.Sqrt((at.X * at.X) + (at.Y * at.Y));

    // Level ground at zero within the ledge's ring, twenty thousand units lower past it.
    private static bool Ground(Vector3d origin, Vector3d end, out WorldRayHit hit)
    {
        hit = default;
        double floor = Across(origin) < LedgeRadius ? 0d : -20000d;
        if (end.Z >= origin.Z || origin.Z < floor || end.Z > floor)
        {
            return false;
        }

        hit = new WorldRayHit(new Vector3d(origin.X, origin.Y, floor), new Vector3f(0f, 0f, 1f), origin.Z - floor);
        return true;
    }

    private static BoneAnimationSet RetailSet() =>
        BoneAnimationSet.Read(File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff")));

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    private static (int, int, int, int, int, int, int, int, int, int) Bits(BoneTranslationKey key) =>
        (Bits(key.Position.X), Bits(key.Position.Y), Bits(key.Position.Z), Bits(key.Time),
            Bits(key.OutTangent.X), Bits(key.OutTangent.Y), Bits(key.OutTangent.Z),
            Bits(key.InTangent.X), Bits(key.InTangent.Y), Bits(key.InTangent.Z));

    private static string RepositoryRoot()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "OpenIGI.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("OpenIGI.slnx was not found above the test binary.");
        }

        return root;
    }

    // The data file: one clip a block, one key or event a line, every float written so it reads back to the same bits.
    private static string Source(IReadOnlyList<RootMotionClip> clips)
    {
        StringBuilder s = new();
        s.Append("// <auto-generated>Written by HumanLocomotionRootMotionTests.WriteTheBakedClips from common/anims/000.iff.</auto-generated>\n");
        s.Append("using OpenIGI.Formats.Animation;\n\n");
        s.Append("namespace OpenIGI.Game.Player;\n\n");
        s.Append("public static partial class HumanLocomotionRootMotion\n{\n");
        s.Append("    private static readonly RootMotionClip[] BakedClips =\n    [\n");
        foreach (RootMotionClip clip in clips)
        {
            s.Append(CultureInfo.InvariantCulture, $"        new({clip.Id}, {F(clip.Duration)}, 0x{clip.Flags:X8}u,\n            [\n");
            foreach (BoneTranslationKey k in clip.Translation)
            {
                s.Append(CultureInfo.InvariantCulture, $"                new({V(k.Position)}, {F(k.Time)}, {V(k.OutTangent)}, {V(k.InTangent)}),\n");
            }

            s.Append("            ],\n            [\n");
            foreach (BoneAnimationEvent e in clip.Events)
            {
                s.Append(CultureInfo.InvariantCulture, $"                new({e.EventId}, {e.BoneIndex}, {F(e.Time)}, {V(e.Position)}),\n");
            }

            s.Append("            ]),\n");
        }

        s.Append("    ];\n}\n");
        return s.ToString();
    }

    private static string V(Vector3f v) => $"new({F(v.X)}, {F(v.Y)}, {F(v.Z)})";

    private static string F(float value) => float.IsFinite(value)
        ? (BitConverter.SingleToInt32Bits(value) == int.MinValue ? "-0f" : value.ToString("R", CultureInfo.InvariantCulture) + "f")
        : throw new InvalidDataException($"A clip holds {value}, which a literal cannot carry.");
}
