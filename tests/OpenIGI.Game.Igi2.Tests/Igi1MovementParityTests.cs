using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Tests;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Project I.G.I.'s own player, as that game builds and tunes it, against I.G.I. 2's player on its movement
/// (<see cref="Igi2PlayerMovement.Igi1"/>): the same keys on the same ground, compared tick for tick on everything that is
/// movement -- where the feet are, how far they went, the body's speed through the air, the ground, the crouch, the locomotion
/// state, the heading, the eye's height over the feet, the camera with its landing dip, the slope's slide and a round's shove.
/// </summary>
/// <remarks>
/// The first game's player here is the whole of it (<see cref="PlayerCamera"/>): <c>000.iff</c>'s clips as the game loads
/// them, <c>humanplayer.qvm</c>'s tuning run through the script VM, its input sampler, its walk/run latch and its tick. The
/// sequel's player is its own <see cref="Igi2HumanPlayer.Update(ReadOnlySpan{float})"/>, fed by its own input reader.
/// </remarks>
public sealed class Igi1MovementParityTests(ITestOutputHelper output)
{
    /// <summary>How far from the start the ground stays level; past it, it is twenty thousand units lower.</summary>
    private const double LedgeRadius = 80000d;

    /// <summary>The heading both start at, off zero, so a turned axis cannot pass by luck.</summary>
    private const float StartYaw = 0.7f;

    /// <summary>No key held.</summary>
    private const PlayerAction Still = 0;

    private static readonly (PlayerAction Igi1, Igi2PlayerAction Igi2)[] Keys =
    [
        (PlayerAction.MoveUp, Igi2PlayerAction.MoveForwards),
        (PlayerAction.MoveDown, Igi2PlayerAction.MoveBackwards),
        (PlayerAction.MoveLeft, Igi2PlayerAction.MoveLeft),
        (PlayerAction.MoveRight, Igi2PlayerAction.MoveRight),
        (PlayerAction.Jump, Igi2PlayerAction.Jump),
        (PlayerAction.Crouch, Igi2PlayerAction.Crouch),
        (PlayerAction.ToggleWalkRun, Igi2PlayerAction.ToggleWalkRun),
    ];

    /// <summary>Running and walking each way, stopping, and changing pace.</summary>
    [RequiresGameDataFact]
    public void Running_and_walking_match_tick_for_tick() => Compare(Gaits());

    /// <summary>Jumps from a stand, from a run, from a walk and one straight after landing.</summary>
    [RequiresGameDataFact]
    public void Jumps_and_their_landings_match_tick_for_tick() => Compare(Jumps());

    /// <summary>Crouching, crouch-walking each way and standing up again.</summary>
    [RequiresGameDataFact]
    public void Crouching_matches_tick_for_tick() => Compare(Crouches());

    /// <summary>Running off a ledge, the fall and the landing.</summary>
    [RequiresGameDataFact]
    public void A_fall_off_a_ledge_matches_tick_for_tick() => Compare(OffTheLedge());

    private void Compare(IEnumerable<PlayerAction> script, [System.Runtime.CompilerServices.CallerMemberName] string scenario = "")
    {
        Ledge ground = new();
        PlayerCamera igi1 = Igi1Player(ground);
        Igi2HumanPlayer igi2 = Igi2Player(ground);
        List<string> rows = [Sample.Header];
        List<string> differences = [];
        int tick = 0;
        foreach (PlayerAction held in script)
        {
            Vector3d before1 = igi1.PositionUnits;
            Vector3d before2 = igi2.Position;
            igi1.Tick(held, ground);
            Tick(igi2, Map(held));
            tick++;

            Sample a = Sample.Of(igi1, before1);
            Sample b = Sample.Of(igi2, before2);
            rows.Add(a.Row(tick, "igi1"));
            rows.Add(b.Row(tick, "igi2"));
            foreach (string field in a.Differences(b))
            {
                if (differences.Count < 12)
                {
                    differences.Add($"tick {tick} {field}");
                }
            }
        }

        string table = string.Join('\n', rows);
        output.WriteLine(table);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, $"igi1-movement-parity-{scenario}.csv"), table);
        Assert.True(differences.Count == 0, "The two differ:\n" + string.Join('\n', differences));
    }

    // Project I.G.I.'s player as its game makes it: the clips, the script's tuning, nothing placed in the level, at rest.
    private static PlayerCamera Igi1Player(Ledge ground)
    {
        PlayerCamera rig = new();
        rig.UseAnimations(BoneAnimationSet.Read(File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff"))));
        rig.UseParameters(RetailPlayerParameters.Value);
        rig.UseWalls(ObjectCollisionIndex.Build([]));
        rig.StandAfresh(default, StartYaw, 0f);

        // A landing dips the camera as the game's rules have it do (Igi1PlayerRules.Land): the kick the fall measured.
        rig.Body.SetFallImpactSink(impact => rig.InstallLandingViewKick(impact.ViewKickUnits));
        Assert.True(rig.Body.PlaceOnGround(ground));
        return rig;
    }

    // I.G.I. 2's player on Project I.G.I.'s movement, over the same ground and the same (empty) objects, facing the same way.
    private static Igi2HumanPlayer Igi2Player(Ledge ground)
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.OnFloor();
        player.Movement = Igi2PlayerMovement.Igi1;
        player.Igi1Feet = new Igi1FootMotion(new Igi1FootWorld(ObjectCollisionIndex.Build([]).PlayerCaster(), null, ground, null), player);
        player.Gamma = -StartYaw;
        player.IdealGamma = -StartYaw;
        return player;
    }

    private static Igi2PlayerAction Map(PlayerAction held)
    {
        Igi2PlayerAction mapped = Igi2PlayerAction.None;
        foreach ((PlayerAction igi1, Igi2PlayerAction igi2) in Keys)
        {
            if ((held & igi1) != 0)
            {
                mapped |= igi2;
            }
        }

        return mapped;
    }

    private static void Tick(Igi2HumanPlayer player, Igi2PlayerAction held)
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        new Igi2PlayerInputReader().Read(new Igi2PlayerInputState(0f, 0f, held, 0), frame);
        player.Update(frame);
    }

    private static IEnumerable<PlayerAction> Gaits()
    {
        PlayerAction[] directions =
        [
            PlayerAction.MoveUp, PlayerAction.MoveUp | PlayerAction.MoveRight, PlayerAction.MoveRight,
            PlayerAction.MoveDown | PlayerAction.MoveRight, PlayerAction.MoveDown, PlayerAction.MoveDown | PlayerAction.MoveLeft,
            PlayerAction.MoveLeft, PlayerAction.MoveUp | PlayerAction.MoveLeft,
        ];
        IEnumerable<PlayerAction> run = directions.SelectMany(d => Hold(d, 30).Concat(Hold(Still, 10)));
        return Hold(Still, 5)
            .Concat(run)
            .Concat(Hold(PlayerAction.ToggleWalkRun, 1))
            .Concat(Hold(Still, 2))
            .Concat(run)
            .Concat(Hold(PlayerAction.ToggleWalkRun, 1))
            .Concat(Hold(PlayerAction.MoveUp, 20))
            .Concat(Hold(PlayerAction.MoveUp | PlayerAction.ToggleWalkRun, 1))
            .Concat(Hold(PlayerAction.MoveUp, 20));
    }

    private static IEnumerable<PlayerAction> Jumps() =>
        Hold(Still, 5)
            .Concat(Hold(PlayerAction.Jump, 1)).Concat(Hold(Still, 45))
            .Concat(Hold(PlayerAction.MoveUp, 25)).Concat(Hold(PlayerAction.MoveUp | PlayerAction.Jump, 1)).Concat(Hold(PlayerAction.MoveUp, 45))
            .Concat(Hold(PlayerAction.MoveRight, 10)).Concat(Hold(PlayerAction.MoveRight | PlayerAction.Jump, 1)).Concat(Hold(PlayerAction.MoveRight, 45))
            .Concat(Hold(PlayerAction.ToggleWalkRun, 1)).Concat(Hold(PlayerAction.MoveUp, 25))
            .Concat(Hold(PlayerAction.MoveUp | PlayerAction.Jump, 1)).Concat(Hold(PlayerAction.MoveUp, 45))
            .Concat(Hold(PlayerAction.ToggleWalkRun, 1))
            .Concat(Enumerable.Range(0, 6).SelectMany(_ => Hold(PlayerAction.MoveUp | PlayerAction.Jump, 1).Concat(Hold(PlayerAction.MoveUp, 3))))
            .Concat(Hold(Still, 40));

    private static IEnumerable<PlayerAction> Crouches()
    {
        PlayerAction[] directions =
        [
            PlayerAction.MoveUp, PlayerAction.MoveUp | PlayerAction.MoveRight, PlayerAction.MoveRight, PlayerAction.MoveDown,
            PlayerAction.MoveLeft, PlayerAction.MoveDown | PlayerAction.MoveLeft,
        ];
        return Hold(Still, 5)
            .Concat(Hold(PlayerAction.Crouch, 20))
            .Concat(directions.SelectMany(d => Hold(PlayerAction.Crouch | d, 25)))
            .Concat(Hold(Still, 20))
            .Concat(Hold(PlayerAction.MoveUp, 20))
            .Concat(Hold(PlayerAction.MoveUp | PlayerAction.Crouch, 25))
            .Concat(Hold(PlayerAction.MoveUp, 25));
    }

    private static IEnumerable<PlayerAction> OffTheLedge() =>
        Hold(Still, 5).Concat(Hold(PlayerAction.MoveUp, 260)).Concat(Hold(Still, 40));

    private static IEnumerable<PlayerAction> Hold(PlayerAction held, int ticks) => Enumerable.Repeat(held, ticks);

    /// <summary>Everything about a tick's movement, as either game's player stands after it.</summary>
    private readonly record struct Sample(
        Vector3d Feet,
        Vector3d Step,
        Vector3f Velocity,
        double HorizontalSpeed,
        bool Grounded,
        bool Crouched,
        int State,
        float Yaw,
        double EyeHeight,
        Vector3f SlopeSlide,
        Vector3f HitShove,
        Vector3d Camera)
    {
        public const string Header =
            "tick,game,x,y,z,stepX,stepY,stepZ,velX,velY,velZ,speedMps,grounded,crouched,state,yaw,eye,slideX,slideY,slideZ,cameraZ";

        public static Sample Of(PlayerCamera rig, Vector3d before) =>
            Of(rig.Body, rig.LocomotionState, rig.PositionUnits, before) with { Camera = rig.Camera.Position * WorldUnits.PerMetre };

        public static Sample Of(Igi2HumanPlayer player, Vector3d before)
        {
            Igi1FootMotion feet = player.Igi1Feet!;
            return Of(feet.Body, feet.LocomotionState, player.Position, before) with
            {
                Grounded = player.Signal.HasFlag(Igi2PlayerSignal.Grounded),
                Camera = player.View.Position,
            };
        }

        private static Sample Of(PlayerBody body, int state, Vector3d feet, Vector3d before)
        {
            Vector3d step = feet - before;
            return new Sample(
                feet,
                step,
                body.Velocity,
                Math.Sqrt((step.X * step.X) + (step.Y * step.Y)) * 30.0 / WorldUnits.PerMetre,
                body.IsGrounded,
                body.IsCrouched,
                state,
                body.Yaw,
                body.EyeHeight,
                body.SlopeSlide,
                body.HitShove,
                default);
        }

        public IEnumerable<string> Differences(Sample other)
        {
            if (Feet != other.Feet)
            {
                yield return $"feet {Feet} against {other.Feet}";
            }

            if (Step != other.Step)
            {
                yield return $"step {Step} against {other.Step}";
            }

            if (Velocity != other.Velocity)
            {
                yield return $"velocity {Velocity} against {other.Velocity}";
            }

            if (Grounded != other.Grounded)
            {
                yield return $"grounded {Grounded} against {other.Grounded}";
            }

            if (Crouched != other.Crouched)
            {
                yield return $"crouched {Crouched} against {other.Crouched}";
            }

            if (State != other.State)
            {
                yield return $"state {State} against {other.State}";
            }

            if (BitConverter.SingleToInt32Bits(Yaw) != BitConverter.SingleToInt32Bits(other.Yaw))
            {
                yield return $"yaw {Yaw} against {other.Yaw}";
            }

            if (EyeHeight != other.EyeHeight)
            {
                yield return $"eye height {EyeHeight} against {other.EyeHeight}";
            }

            // The camera: the first game's in metres and back, so to a millionth of a unit.
            Vector3d off = Camera - other.Camera;
            if (Math.Abs(off.X) > 1e-6 || Math.Abs(off.Y) > 1e-6 || Math.Abs(off.Z) > 1e-6)
            {
                yield return $"camera {Camera} against {other.Camera}";
            }

            if (SlopeSlide != other.SlopeSlide || HitShove != other.HitShove)
            {
                yield return $"slide {SlopeSlide}/{HitShove} against {other.SlopeSlide}/{other.HitShove}";
            }
        }

        public string Row(int tick, string game)
        {
            StringBuilder s = new();
            s.Append(CultureInfo.InvariantCulture, $"{tick},{game},{Feet.X:R},{Feet.Y:R},{Feet.Z:R},{Step.X:R},{Step.Y:R},{Step.Z:R},");
            s.Append(CultureInfo.InvariantCulture, $"{Velocity.X:R},{Velocity.Y:R},{Velocity.Z:R},{HorizontalSpeed:F3},{Grounded},{Crouched},{State},");
            s.Append(CultureInfo.InvariantCulture, $"{Yaw:R},{EyeHeight:R},{SlopeSlide.X:R},{SlopeSlide.Y:R},{SlopeSlide.Z:R},{Camera.Z - Feet.Z:R}");
            return s.ToString();
        }
    }

    // Level ground at zero within the ledge's ring around the start, twenty thousand units lower past it.
    private sealed class Ledge : IGroundHeightField
    {
        public bool TryGetGround(double x, double y, double z, double aboveUnits, out TerrainGround ground)
        {
            double height = Math.Sqrt((x * x) + (y * y)) < LedgeRadius ? 0d : -20000d;
            ground = new TerrainGround(height, 0.0, 0.0, 1.0);
            return true;
        }

        public bool TryTraceDescent(Vector3d from, Vector3d target, out TerrainGround ground)
        {
            ground = default;
            return false;
        }
    }
}
