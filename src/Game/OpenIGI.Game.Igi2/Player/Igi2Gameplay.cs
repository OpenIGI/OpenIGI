using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>I.G.I. 2's own player for the host: the ported <c>HumanPlayer</c> over the level's collision.</summary>
/// <remarks>
/// <para>
/// The level's <c>HumanPlayer</c> task says where the player stands and which way it faces. The collision world is
/// built from the same placements and ground the scene walk produced; the tuning is the one <c>humanplayer.qvm</c>
/// declared at start-up. The player is made as <c>HumanPlayer_CreateHandler</c> makes it and ticked as the original
/// ticks it: the channels read, <c>HumanPlayer_Update</c>, then the delayed half.
/// </para>
/// <para>
/// Not yet behind it: the animation layer (a still stand-in reports every clip played out), water, sounds, AI
/// events, conveyors and the weapon (the player holds nothing).
/// </para>
/// </remarks>
public sealed class Igi2Gameplay : ITitleGameplay
{
    /// <summary>What a terrain hit reports as struck.</summary>
    public const string TerrainOwner = "Terrain";

    /// <summary>Gets the shared instance.</summary>
    public static Igi2Gameplay Instance { get; } = new();

    /// <inheritdoc/>
    public ITitlePlayer? CreatePlayer(in TitlePlayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Services);
        if (!PlayerSpawn.TryFindGameplay(request.World.LevelRoot, out PlayerSpawnPoint spawn))
        {
            return null;
        }

        Igi2CollisionWorld world = BuildCollision(request.World, request.Built);
        Igi2HumanPlayerGeneral general = request.Services.TryGet(out Igi2PlayerTuning? tuning) ? tuning.General : Igi2HumanPlayerGeneral.Unset;
        return Igi2TitlePlayer.Spawn(world, general, spawn.Position, spawn.Gamma, request.Difficulty);
    }

    /// <summary>Builds the collision a level's player moves through: its solid placements and its ground.</summary>
    /// <param name="world">The request the scene was built from.</param>
    /// <param name="built">What the scene walk produced; its ground is reused.</param>
    /// <returns>The collision world.</returns>
    public static Igi2CollisionWorld BuildCollision(in WorldBuildRequest world, in WorldBuildResult built)
    {
        ArgumentNullException.ThrowIfNull(world.Mission);
        ArgumentNullException.ThrowIfNull(world.LevelRoot);

        List<Formats.World.MtpPack> mtp = [.. world.Mission.PacksInLoadOrder.Select(static pack => pack.Pack)];
        Func<string, Formats.World.MefModel?> models = Igi2DrawableScene.Models(Igi2ModelTable.Create(mtp), world.Mission.PacksInLoadOrder);
        Igi2WorldScene scene = Igi2WorldScene.Build(world.LevelRoot, models);
        Igi2TerrainSurface? ground = built.Ground is Igi2Ground level ? level.Surface : null;
        return Igi2CollisionWorld.Build(scene, models, ground, TerrainOwner);
    }
}

/// <summary>The ported player as the host steps it.</summary>
public sealed class Igi2TitlePlayer : ITitlePlayer
{
    /// <summary>DirectInput's relative counts to the analog axis, <c>Mouse_Update</c> (<c>igi2ed 0x55ED60</c>): a 255th each.</summary>
    private const double CountsToAnalog = 0.00392156862745098;

    private static readonly (PlayerAction Host, Igi2PlayerAction Title)[] Actions =
    [
        (PlayerAction.MoveLeft, Igi2PlayerAction.MoveLeft),
        (PlayerAction.MoveRight, Igi2PlayerAction.MoveRight),
        (PlayerAction.MoveUp, Igi2PlayerAction.MoveForwards),
        (PlayerAction.MoveDown, Igi2PlayerAction.MoveBackwards),
        (PlayerAction.Fire, Igi2PlayerAction.Fire),
        (PlayerAction.AlternateFire, Igi2PlayerAction.AlternateFire),
        (PlayerAction.Reload, Igi2PlayerAction.Reload),
        (PlayerAction.Activate, Igi2PlayerAction.Activate),
        (PlayerAction.Crouch, Igi2PlayerAction.Crouch),
        (PlayerAction.Jump, Igi2PlayerAction.Jump),
        (PlayerAction.Peek, Igi2PlayerAction.Peek),
        (PlayerAction.ZoomIn, Igi2PlayerAction.ZoomIn),
        (PlayerAction.ZoomOut, Igi2PlayerAction.ZoomOut),
        (PlayerAction.NextWeapon, Igi2PlayerAction.NextWeapon),
        (PlayerAction.PreviousWeapon, Igi2PlayerAction.PrevWeapon),
        (PlayerAction.ToggleWalkRun, Igi2PlayerAction.ToggleWalkRun),
        (PlayerAction.Binoculars, Igi2PlayerAction.Binoculars),
        (PlayerAction.MapComputer, Igi2PlayerAction.ComputerMap),
    ];

    private readonly Igi2PlayerInputReader _reader = new();
    private readonly float[] _frame = new float[Igi2HumanChannels.Count];

    private Igi2TitlePlayer(Igi2HumanPlayer human) => Human = human;

    /// <summary>Gets the ported player.</summary>
    public Igi2HumanPlayer Human { get; }

    /// <inheritdoc/>
    public Vector3d Position => Human.Position;

    /// <inheritdoc/>
    public Vector3d EyePosition => Human.View.Position;

    /// <inheritdoc/>
    /// <remarks>The view's columns are its right, forward and up axes; the camera wants them as rows, up before forward.</remarks>
    public Matrix3f ViewOrientation
    {
        get
        {
            Matrix3f m = Human.View.Orientation;
            return Matrix3f.FromRows(
                new Vector3f(m.M11, m.M21, m.M31),
                new Vector3f(m.M13, m.M23, m.M33),
                new Vector3f(m.M12, m.M22, m.M32));
        }
    }

    /// <summary>Makes the player at its spawn, as <c>HumanPlayer_CreateHandler</c> does, over a collision world.</summary>
    /// <param name="world">The level's collision.</param>
    /// <param name="general">The player tuning.</param>
    /// <param name="position">Where the level places the player's feet.</param>
    /// <param name="gamma">The heading the level gives it.</param>
    /// <param name="difficulty">The active difficulty.</param>
    /// <returns>The player.</returns>
    public static Igi2TitlePlayer Spawn(
        Igi2CollisionWorld world,
        Igi2HumanPlayerGeneral general,
        Vector3d position,
        float gamma,
        Ai.AiDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(general);

        Igi2PlayerSurroundings surroundings = new(world);
        Igi2HumanPlayer human = new(Igi2NoWeaponControl.Instance, new Igi2StillAnimation(), surroundings)
        {
            General = general,
            Position = position,
            Gamma = gamma,
            IdealGamma = gamma,
        };
        surroundings.Owner = human;
        human.Create(difficulty);
        human.MaintainCollisionZOffset();
        return new Igi2TitlePlayer(human);
    }

    /// <inheritdoc/>
    public void Tick(in TitlePlayerInput input)
    {
        Igi2PlayerAction held = Map(input.Held);
        Igi2PlayerAction pressed = Map(input.Pressed);
        _reader.MouseSensitivity = input.MouseSensitivity;
        _reader.InvertMouse = input.InvertMouse;
        _reader.Read(new Igi2PlayerInputState(Analog(input.MouseX), Analog(input.MouseY), held, pressed), _frame);
        Human.Update(_frame);
        Human.RunDelayed();
    }

    /// <summary>The mouse's counts as the input port's analog axis: a 255th each, held within one either way.</summary>
    private static float Analog(float counts)
    {
        float analog = ((Fpu)(double)counts * CountsToAnalog).F;
        return analog < -1f ? -1f : analog > 1f ? 1f : analog;
    }

    private static Igi2PlayerAction Map(PlayerAction actions)
    {
        Igi2PlayerAction mapped = Igi2PlayerAction.None;
        foreach ((PlayerAction host, Igi2PlayerAction title) in Actions)
        {
            if ((actions & host) != 0)
            {
                mapped |= title;
            }
        }

        return mapped;
    }
}

/// <summary>What the player's states ask of the world, answered from the collision world and nothing else yet.</summary>
internal sealed class Igi2PlayerSurroundings(Igi2CollisionWorld world) : IIgi2HumanSurroundings
{
    /// <summary>Gets or sets the body these surroundings are asked about.</summary>
    public Igi2Human? Owner { get; set; }

    public IIgi2CollisionQuery Collision { get; } = world;

    public bool IsOnElevator => false;

    public bool FitsLyingFlat() => Owner?.FitsWhereItStands() ?? true;

    public void SendWalkEvent()
    {
    }

    public void SendLandingEvent()
    {
    }

    public bool IsUnderWater(out float depth)
    {
        depth = 0f;
        return false;
    }

    public void StirWater(bool isRunning)
    {
    }

    public Vector3f? PushOnConveyor(IIgi2Collider ground, Igi2Intersection footing) => null;

    public void BumpHumans(Vector3d start, Vector3d destination)
    {
    }

    public string? LandingSound(int material) => null;

    public void PlaySound(string name)
    {
    }

    public IIgi2SoundInstance StartSound(string name) => SilentSound.Instance;

    private sealed class SilentSound : IIgi2SoundInstance
    {
        public static SilentSound Instance { get; } = new();

        public bool IsAlive => false;

        public void MoveToHuman()
        {
        }

        public void Delete()
        {
        }
    }
}

/// <summary>An animation layer with no clips: every clip has played out, and steps move nothing.</summary>
/// <remarks>The stand-in until the clip layer (<c>MINA</c> sets through the weapon's animation table) drives the body.</remarks>
internal sealed class Igi2StillAnimation : IIgi2HumanAnimation
{
    public int MainTime { get; private set; }

    public int MainTotalTime => 0;

    public int MainTimeStep { get; set; }

    public bool MainEnded => true;

    public void LoopMain()
    {
    }

    public void StepMain() => MainTime += MainTimeStep;

    public void ClearUpperBody(float blendSeconds)
    {
    }

    public void PlayState(int state, int time, float blendSeconds) => MainTime = time;
}
