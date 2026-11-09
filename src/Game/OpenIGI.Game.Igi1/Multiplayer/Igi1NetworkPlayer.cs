using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>The command an I.G.I. client sends each tick: the 40-slot input frame, where it looks, and where its crosshair is.</summary>
/// <remarks>
/// <para>
/// I.G.I. turns the view every frame from the mouse rather than from the frame's look slots, so the frame alone does not say
/// where a player looks. A command therefore carries the body's heading and the view's pitch after the frame's slots, as
/// Source's user command carries its view angles.
/// </para>
/// <para>
/// Over the shoulder the crosshair is the camera's ray, not the eye's: the player's own game converges its rounds on what
/// that ray meets (<see cref="PlayerCamera.AimConvergenceRay"/>). The server's copy of the player has no camera, so the ray
/// comes with the command -- its origin as an offset from the eye, then its direction, all nought in first person. Without
/// it the server fired a third-person joiner's rounds along the eye, the camera's half-metre beside where its crosshair sat.
/// </para>
/// </remarks>
public static class Igi1Command
{
    // Where the look and the crosshair's ray sit after the frame's slots.
    private const int Look = PlayerInputFrame.SlotCount;
    private const int Aim = Look + 2;
    private const int Chosen = Aim + 6;

    /// <summary>
    /// The channels a command carries: the frame's slots, the heading and the pitch, the crosshair's ray, and the weapon its
    /// player had chosen once the tick ran.
    /// </summary>
    public const int ChannelCount = Chosen + 1;

    /// <summary>The farthest a crosshair's ray may start from the eye, in world units: the over-the-shoulder camera's reach and some.</summary>
    /// <remarks>
    /// The camera sits at most 2.6 m back, 0.55 m aside and 0.25 m up (<see cref="ThirdPersonCamera"/>). A ray from further off
    /// is a client aiming from somewhere its player is not, and is drawn back to this.
    /// </remarks>
    public const double MaxAimReach = 3.5 * WorldUnits.PerMetre;

    /// <summary>Writes a tick's input, look and crosshair into a command's channels.</summary>
    /// <param name="frame">The input frame.</param>
    /// <param name="yaw">The body's heading, radians.</param>
    /// <param name="pitch">The view's pitch, radians.</param>
    /// <param name="channels">At least <see cref="ChannelCount"/> channels.</param>
    /// <param name="aim">The crosshair's ray as an offset from the eye and a direction, or <see langword="null"/> in first person.</param>
    /// <param name="chosen">The weapon the player had chosen once the tick ran (<see cref="PlayerWeapons.ChosenWeaponId"/>), or -1.</param>
    public static void Pack(in PlayerInputFrame frame, float yaw, float pitch, Span<float> channels, (Vector3d EyeOffset, Vector3f Direction)? aim = null, int chosen = -1)
    {
        PlayerInputFrame copy = frame;
        copy.AsSpan().CopyTo(channels);
        channels[Look] = yaw;
        channels[Look + 1] = pitch;
        (Vector3d offset, Vector3f direction) = aim ?? default;
        channels[Aim] = (float)offset.X;
        channels[Aim + 1] = (float)offset.Y;
        channels[Aim + 2] = (float)offset.Z;
        channels[Aim + 3] = direction.X;
        channels[Aim + 4] = direction.Y;
        channels[Aim + 5] = direction.Z;
        channels[Chosen] = chosen + 1;
    }

    /// <summary>Reads the weapon a command's player had chosen once its tick ran.</summary>
    /// <param name="channels">At least <see cref="ChannelCount"/> channels.</param>
    /// <returns>The weapon's id, or <see langword="null"/> for none or a value that is no weapon id.</returns>
    public static int? UnpackChosen(ReadOnlySpan<float> channels)
    {
        float value = channels[Chosen];
        return float.IsFinite(value) && value >= 1f && value <= 65536f ? (int)MathF.Round(value) - 1 : null;
    }

    /// <summary>Reads a command's channels back into a frame and a look.</summary>
    /// <param name="channels">At least <see cref="ChannelCount"/> channels.</param>
    /// <param name="yaw">The body's heading, radians.</param>
    /// <param name="pitch">The view's pitch, radians.</param>
    /// <returns>The input frame.</returns>
    /// <remarks>The developer chords are cleared: a client's frame never flies its player on the server.</remarks>
    public static PlayerInputFrame Unpack(ReadOnlySpan<float> channels, out float yaw, out float pitch)
    {
        PlayerInputFrame frame = default;
        channels[..PlayerInputFrame.SlotCount].CopyTo(frame.AsSpan());
        PlayerInputFrame.ClearDeveloperSlots(frame.AsSpan());
        yaw = float.IsFinite(channels[Look]) ? MathF.IEEERemainder(channels[Look], MathF.Tau) : 0f;
        pitch = float.IsFinite(channels[Look + 1]) ? channels[Look + 1] : 0f;
        return frame;
    }

    /// <summary>Reads the crosshair's ray a command carries.</summary>
    /// <param name="channels">At least <see cref="ChannelCount"/> channels.</param>
    /// <returns>Its origin as an offset from the eye and its direction, or <see langword="null"/> when the player was in first person.</returns>
    /// <remarks>
    /// What a client sends is not taken on trust: a direction that is no direction leaves the player in first person, it is
    /// made unit length, and an origin past <see cref="MaxAimReach"/> is drawn back to it.
    /// </remarks>
    public static (Vector3d EyeOffset, Vector3f Direction)? UnpackAim(ReadOnlySpan<float> channels)
    {
        Vector3f direction = new(channels[Aim + 3], channels[Aim + 4], channels[Aim + 5]);
        float length = MathF.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (!float.IsFinite(length) || length < 0.5f || length > 2f)
        {
            return null;
        }

        Vector3d offset = new(channels[Aim], channels[Aim + 1], channels[Aim + 2]);
        double reach = offset.Length;
        if (!double.IsFinite(reach))
        {
            return null;
        }

        if (reach > MaxAimReach)
        {
            offset *= MaxAimReach / reach;
        }

        return (offset, new Vector3f(direction.X / length, direction.Y / length, direction.Z / length));
    }
}

/// <summary>Another player in an I.G.I. co-op session: a rig of the mission's own kind, driven by its client's commands.</summary>
/// <param name="player">The rig.</param>
/// <param name="health">Its health.</param>
/// <remarks>
/// Hurt as the mission's own player is, by the same handlers (<see cref="World.IHarmedPlayer"/>): what they decide happens
/// to the rig and health here, and what it feels of it is recorded (<see cref="RemoteSenses"/>) for its client to feel.
/// </remarks>
public sealed class Igi1NetworkPlayer(PlayerCamera player, HumanHealth health) : World.IHarmedPlayer
{
    private readonly float[] _channels = new float[Igi1Command.ChannelCount];
    private bool _down;

    /// <summary>Gets the rig.</summary>
    public PlayerCamera Player { get; } = player ?? throw new ArgumentNullException(nameof(player));

    /// <summary>Gets or sets its player's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the look it wears, a <see cref="OpenIGI.Game.Multiplayer.PlayerSkins"/> id.</summary>
    public string Skin { get; set; } = string.Empty;

    /// <summary>Gets or sets the entity it is in the session's snapshots, or -1.</summary>
    public int Entity { get; set; } = -1;

    /// <summary>Gets its health.</summary>
    public HumanHealth Health { get; } = health ?? throw new ArgumentNullException(nameof(health));

    /// <summary>Gets its client's commands, by the tick each is for.</summary>
    public UserCommandBuffer Commands { get; } = new(Igi1Command.ChannelCount);

    /// <summary>Gets or sets whether it lies dead: its health spent, or put down by the death handler.</summary>
    public bool IsDead
    {
        get => _down || Health.IsDead;
        set => _down = value;
    }

    /// <inheritdoc/>
    PlayerCamera? World.IHarmedPlayer.Rig => Player;

    /// <inheritdoc/>
    World.IPlayerSenses World.IHarmedPlayer.Senses => RemoteSenses;

    /// <summary>Gets what it felt since the last snapshot, recorded for its client.</summary>
    internal RemotePlayerSenses RemoteSenses => _remote ??= new RemotePlayerSenses(() => Entity, () => Commands.LastTakenTick);

    private RemotePlayerSenses? _remote;

    /// <summary>Runs its tick on its client's command for the tick: the look set, then the frame run as the mission's player runs its own.</summary>
    /// <param name="tick">The tick being run.</param>
    /// <param name="ground">The level's ground.</param>
    public void Run(long tick, IGroundHeightField? ground) => Run(tick, ground, static _ => { });

    /// <summary>Gets the partner's ladder, climbed on a body of its own that only the simulation keeps.</summary>
    public World.LadderClimb Climb { get; } = new();

    /// <summary>Gets the partner's world interaction, played on that same body.</summary>
    public World.InteractionMotion Interaction { get; } = new();

    private SimulatedLadderBody? _ladderBody;

    /// <summary>Gets where the partner is in its ladder or interaction clip, or <see langword="null"/> when it plays neither.</summary>
    public float? ClipTime => Player.ModeClipTimeShown ?? (Player.BodyClip is null ? null : _ladderBody?.ClipTime);

    /// <summary>Runs the partner a tick on its client's command for the tick, its ladder climbed as the mission's own player's is.</summary>
    /// <param name="tick">The tick.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="rung">Sounds a rung it crossed.</param>
    /// <remarks>
    /// Each of its client's commands is run once, in order, so the partner goes exactly where its client predicted it: none
    /// on a tick the queue is dry (the partner waits), two on a tick it is catching up.
    /// </remarks>
    public void Run(long tick, IGroundHeightField? ground, Action<PlayerCamera> rung)
    {
        int shots = Player.Weapons?.ShotSequence ?? 0;
        for (int due = Commands.Due(); due > 0 && Commands.TryTake(_channels); due--)
        {
            // A fallen partner runs nothing of its own, but what its weapons put in the world is the level's (Level_GetDynamic):
            // it flies on, a step for each of its client's commands, as it would have.
            if (IsDead)
            {
                Player.TickProjectilesAlone();
                CommandRan?.Invoke(this);
                continue;
            }

            BeforeCommand?.Invoke(this, Commands.LastTakenTick, Commands.LastViewTick, Commands.LastViewFraction);
            try
            {
                RunCommand(ground, rung);
            }
            finally
            {
                AfterCommand?.Invoke();
            }

            CommandRan?.Invoke(this);
        }

        FiredThisTick = (Player.Weapons?.ShotSequence ?? 0) != shots;
    }

    /// <summary>
    /// Gets or sets what the world does before each of its commands runs, given the player, the command's tick, and the tick
    /// (and fraction of one past it) its client was drawing the others at: the server's lag compensation puts the soldiers and
    /// the other players back where that client saw them, for the command's rounds to meet.
    /// </summary>
    public Action<Igi1NetworkPlayer, long, long, float>? BeforeCommand { get; set; }

    /// <summary>Gets or sets what the world does once a command has run: the soldiers put back where they stand now.</summary>
    public Action? AfterCommand { get; set; }

    /// <summary>
    /// Gets or sets what the world makes of a command once the soldiers are back where they stand: the grenades, rockets and
    /// launcher rounds it set off and the rounds it fired at the level's objects, each command's before the next clears them.
    /// </summary>
    public Action<Igi1NetworkPlayer>? CommandRan { get; set; }

    /// <summary>Gets whether it fired on its last tick, in any of the commands the tick ran.</summary>
    /// <remarks>
    /// Its own: a tick runs as many of its client's commands as are due -- none, one or two -- and the weapon's own "fired
    /// this tick" is only the last command's.
    /// </remarks>
    public bool FiredThisTick { get; private set; }

    // The weapon its client had chosen once the same tick ran there, taken here when this copy chose another. The switches
    // themselves come as keys -- next, previous, a category, the wheel -- each a step from what is in hand; once the two
    // copies' carried lists differ (the bomb given here a moment before the client hears of it, a buy, a pickup), every step
    // lands on another weapon and they never meet again: a live S&D bomb carrier held the bomb on its own game and the knife
    // here, and could plant only with the knife out. The binoculars are left to their own key, which toggles them.
    private void KeepChosenWeapon(int? chosen)
    {
        if (chosen is not { } wanted
            || Player.Weapons is not { } weapons
            || weapons.ChosenWeaponId == wanted
            || !weapons.HasWeapon(wanted)
            || weapons.IsBinoculars(wanted)
            || weapons.IsBinoculars(weapons.ChosenWeaponId))
        {
            return;
        }

        // The client kept what this copy has in hand: drop the switch this copy is making. Otherwise switch to the client's.
        if (weapons.InHand?.Weapon.WeaponId == wanted)
        {
            weapons.KeepInHand();
        }
        else
        {
            weapons.RequestWeapon(wanted);
        }
    }

    // One of its client's commands.
    private void RunCommand(IGroundHeightField? ground, Action<PlayerCamera> rung)
    {
        PlayerInputFrame frame = Igi1Command.Unpack(_channels, out float yaw, out float pitch);
        Player.Body.Yaw = yaw;
        Player.Body.Pitch = Math.Clamp(pitch, -PlayerBody.PitchLimit, PlayerBody.PitchLimit);

        // Its rounds converge where its crosshair was, as on its client's own game.
        Player.AimConvergenceRay = Igi1Command.UnpackAim(_channels) is { } aim ? (Player.Body.Eye + aim.EyeOffset, aim.Direction) : null;
        Player.TickFrame(frame, ground);
        KeepChosenWeapon(Igi1Command.UnpackChosen(_channels));
        _ladderBody ??= Player.BodyAnimations is { } set ? new SimulatedLadderBody(set) : null;

        // At a computer, on a wire, over a wall: the interaction's clip on the partner's body, as the player's own plays.
        if (Player.IsInWorldInteraction)
        {
            Interaction.Tick(Player, _ladderBody);
        }
        else
        {
            Interaction.Reset();
        }

        if (!Player.IsOnLadder)
        {
            Climb.Reset();
            return;
        }

        if (ground is not null)
        {
            Climb.Tick(
                Player,
                _ladderBody,
                frame[(int)PlayerInputSlot.MoveUp] > 0.5f,
                frame[(int)PlayerInputSlot.MoveDown] > 0.5f,
                frame[(int)PlayerInputSlot.Activate] > 0.5f,
                ground,
                rung);
        }
    }
}
