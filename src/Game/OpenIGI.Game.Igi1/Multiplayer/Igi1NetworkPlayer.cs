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
public sealed class Igi1NetworkPlayer(PlayerCamera player, HumanHealth health)
{
    private readonly float[] _channels = new float[Igi1Command.ChannelCount];

    /// <summary>Gets the rig.</summary>
    public PlayerCamera Player { get; } = player ?? throw new ArgumentNullException(nameof(player));

    /// <summary>Gets its health.</summary>
    public HumanHealth Health { get; } = health ?? throw new ArgumentNullException(nameof(health));

    /// <summary>Gets its client's commands, by the tick each is for.</summary>
    public UserCommandBuffer Commands { get; } = new(Igi1Command.ChannelCount);

    /// <summary>Gets a value indicating whether it has died.</summary>
    public bool IsDead => Health.IsDead;

    /// <summary>Runs its tick on its client's command for the tick: the look set, then the frame run as the mission's player runs its own.</summary>
    /// <param name="tick">The tick being run.</param>
    /// <param name="ground">The level's ground.</param>
    public void Run(long tick, IGroundHeightField? ground)
    {
        Commands.Take(tick, _channels);
        if (IsDead)
        {
            return;
        }

        PlayerInputFrame frame = Igi1Command.Unpack(_channels, out float yaw, out float pitch);
        Player.Body.Yaw = yaw;
        Player.Body.Pitch = Math.Clamp(pitch, -PlayerBody.PitchLimit, PlayerBody.PitchLimit);
        Player.TickFrame(frame, ground);
    }
}
