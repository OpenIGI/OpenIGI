using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>The command an I.G.I. client sends each tick: the 40-slot input frame and where it looks.</summary>
/// <remarks>
/// I.G.I. turns the view every frame from the mouse rather than from the frame's look slots, so the frame alone does not say
/// where a player looks. A command therefore carries the body's heading and the view's pitch after the frame's slots, as
/// Source's user command carries its view angles.
/// </remarks>
public static class Igi1Command
{
    /// <summary>The channels a command carries: the frame's slots, then the heading and the pitch.</summary>
    public const int ChannelCount = PlayerInputFrame.SlotCount + 2;

    /// <summary>Writes a tick's input and look into a command's channels.</summary>
    /// <param name="frame">The input frame.</param>
    /// <param name="yaw">The body's heading, radians.</param>
    /// <param name="pitch">The view's pitch, radians.</param>
    /// <param name="channels">At least <see cref="ChannelCount"/> channels.</param>
    public static void Pack(in PlayerInputFrame frame, float yaw, float pitch, Span<float> channels)
    {
        PlayerInputFrame copy = frame;
        copy.AsSpan().CopyTo(channels);
        channels[PlayerInputFrame.SlotCount] = yaw;
        channels[PlayerInputFrame.SlotCount + 1] = pitch;
    }

    /// <summary>Reads a command's channels back into a frame and a look.</summary>
    /// <param name="channels">At least <see cref="ChannelCount"/> channels.</param>
    /// <param name="yaw">The body's heading, radians.</param>
    /// <param name="pitch">The view's pitch, radians.</param>
    /// <returns>The input frame.</returns>
    public static PlayerInputFrame Unpack(ReadOnlySpan<float> channels, out float yaw, out float pitch)
    {
        PlayerInputFrame frame = default;
        channels[..PlayerInputFrame.SlotCount].CopyTo(frame.AsSpan());
        yaw = channels[PlayerInputFrame.SlotCount];
        pitch = channels[PlayerInputFrame.SlotCount + 1];
        return frame;
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
    public float? LadderClipTime => Player.IsOnLadder || Player.PlaysInteractionClip ? _ladderBody?.ClipTime : null;

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
            if (IsDead)
            {
                continue;
            }

            BeforeCommand?.Invoke(Commands.LastTakenTick, Commands.LastViewTick);
            try
            {
                RunCommand(ground, rung);
            }
            finally
            {
                AfterCommand?.Invoke();
            }
        }

        FiredThisTick = (Player.Weapons?.ShotSequence ?? 0) != shots;
    }

    /// <summary>
    /// Gets or sets what the world does before each of its commands runs, given the command's tick and the tick its client
    /// was drawing the others at: the server's lag compensation puts the soldiers back where that client saw them, for the
    /// command's rounds to meet.
    /// </summary>
    public Action<long, long>? BeforeCommand { get; set; }

    /// <summary>Gets or sets what the world does once a command has run: the soldiers put back where they stand now.</summary>
    public Action? AfterCommand { get; set; }

    /// <summary>Gets whether it fired on its last tick, in any of the commands the tick ran.</summary>
    /// <remarks>
    /// Its own: a tick runs as many of its client's commands as are due -- none, one or two -- and the weapon's own "fired
    /// this tick" is only the last command's.
    /// </remarks>
    public bool FiredThisTick { get; private set; }

    // One of its client's commands.
    private void RunCommand(IGroundHeightField? ground, Action<PlayerCamera> rung)
    {
        PlayerInputFrame frame = Igi1Command.Unpack(_channels, out float yaw, out float pitch);
        Player.Body.Yaw = yaw;
        Player.Body.Pitch = Math.Clamp(pitch, -PlayerBody.PitchLimit, PlayerBody.PitchLimit);
        Player.TickFrame(frame, ground);
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
