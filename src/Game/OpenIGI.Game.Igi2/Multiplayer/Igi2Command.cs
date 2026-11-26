using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>How a player stood as one of its commands began: where it looked, how its controls read, and the seed its draws take.</summary>
/// <param name="IdealGamma">The heading the command's turn is added to, <c>Human_s.vIdealGamma</c>.</param>
/// <param name="IdealPitch">The view's pitch the command's look is added to, <c>HumanView_s.vIdealPitch</c>.</param>
/// <param name="Unfiltered">Whether the vertical look skipped the original's mouse filters.</param>
/// <param name="SteadyTurns">Whether the ground speed turns with the body (<c>g_steady_turns</c>).</param>
/// <param name="InvertMouse">Whether the mouse is inverted, which a peek's height reads.</param>
/// <param name="Movement">Whose movement the player has (<c>g_igi2_movement</c>).</param>
/// <param name="Igi1Weight">Whether its weapons slow Project I.G.I.'s walk (<c>g_igi2_igi1_weight</c>).</param>
/// <param name="Seed">The seed the player's stream is set to before the command runs, at most <see cref="Igi2Command.SeedLimit"/>.</param>
/// <param name="Loadout">What the player may carry of its primary weapons (<c>g_igi2_loadout</c>).</param>
/// <param name="GrenadeCook">When its grenades cook (<c>g_grenade_cook</c>).</param>
/// <param name="Aim">
/// The crosshair's ray over the shoulder, as an offset from the view's eye and a direction (<see cref="AimRayChannels"/>), or
/// <see langword="null"/> in first person. <b>Not retail.</b>
/// </param>
public readonly record struct Igi2CommandStart(
    float IdealGamma,
    float IdealPitch,
    bool Unfiltered,
    bool SteadyTurns,
    bool InvertMouse,
    Igi2PlayerMovement Movement,
    bool Igi1Weight,
    uint Seed,
    Igi2Loadout Loadout = Igi2Loadout.Original,
    GrenadeCook GrenadeCook = GrenadeCook.Original,
    (Vector3d EyeOffset, Vector3f Direction)? Aim = null);

/// <summary>What a command left the player with that a lost key press would leave otherwise on the two machines.</summary>
/// <param name="Chosen">The weapon chosen, by number, or -1 for none.</param>
/// <param name="Walk">Whether it walks rather than runs.</param>
/// <param name="FieldOfView">The view's field of view, which the look's turn is scaled by.</param>
/// <param name="ObjectFieldOfView">The first-person weapon's field of view.</param>
/// <param name="Climb">What it hangs on, <see cref="Player.Igi2HumanPlayer.ClimbMode"/>: nought for nothing.</param>
public readonly record struct Igi2CommandEnd(int Chosen, bool Walk, float FieldOfView, float ObjectFieldOfView, int Climb = 0);

/// <summary>
/// An I.G.I. 2 client's command as it goes to the server: the player's channel frame, how the player stood as it began, and what
/// it left the player with.
/// </summary>
/// <remarks>
/// <para>
/// The channel frame turns the player by the mouse's movement, not to a heading. Run from a heading the server's copy did not
/// share, the same frame turns it somewhere else, and every step after goes another way. So the command says where the client's
/// player looked before the frame turned it, as Project I.G.I.'s command carries its yaw and pitch, and the server's copy
/// starts each command from there.
/// </para>
/// <para>
/// The same for everything else the two copies must agree on. The player's draws -- a round's scatter, a gun's kick -- come from
/// its own stream, which the server's copy began afresh when the client joined: both are set from the command's seed before it
/// runs (Source's <c>random_seed</c>). The client's own settings that steer the simulation -- steady turns, the inverted mouse
/// a peek reads, the loadout a pickup is held to, when a grenade cooks -- were the server's defaults on its copy. And the toggles a key press flips --
/// walk or run, the field of view, the weapon -- are carried as what they came to: a press lost with a dropped command left the
/// two copies apart for good, and the client was put back on every step after.
/// </para>
/// </remarks>
public static class Igi2Command
{
    /// <summary>The largest seed a command carries: a float holds every whole number to 2^24 exactly.</summary>
    public const uint SeedLimit = (1u << 24) - 1;

    private const int Gamma = Igi2HumanChannels.Count;
    private const int Pitch = Gamma + 1;
    private const int Flags = Pitch + 1;
    private const int Chosen = Flags + 1;
    private const int SeedAt = Chosen + 1;
    private const int FieldOfViewAt = SeedAt + 1;
    private const int ObjectFieldOfViewAt = FieldOfViewAt + 1;
    private const int AimAt = ObjectFieldOfViewAt + 1;

    /// <summary>The command's length.</summary>
    public const int ChannelCount = AimAt + AimRayChannels.Count;

    // The flags. A blank command, all noughts, carries none of the state and changes nobody's.
    private const int HasState = 1;
    private const int UnfilteredLook = 2;
    private const int SteadyTurnsFlag = 4;
    private const int InvertMouseFlag = 8;
    private const int WalkFlag = 16;

    // What the player hangs on after the command, two bits from here up.
    private const int ClimbShift = 5;
    private const int ClimbMask = 3;

    // Project I.G.I.'s movement, above the climb's two bits, and whether it carries its weapons' weight.
    private const int Igi1MovementFlag = 128;
    private const int Igi1WeightFlag = 256;

    // A tactical loadout: a client that never sends it carries one primary weapon on the server, as the original does.
    private const int TacticalLoadoutFlag = 512;

    // A manual grenade cook: a client that never sends it cooks as the original does.
    private const int ManualCookFlag = 1024;

    // The keys that step the weapon from what is in hand, which the chosen weapon stands in for on the server.
    private static readonly Igi2HumanChannel[] WeaponSteps =
    [
        Igi2HumanChannel.NextWeapon,
        Igi2HumanChannel.PrevWeapon,
        Igi2HumanChannel.WeaponCategory1,
        Igi2HumanChannel.WeaponCategory1 + 1,
        Igi2HumanChannel.WeaponCategory1 + 2,
        Igi2HumanChannel.WeaponCategory1 + 3,
        Igi2HumanChannel.WeaponCategory1 + 4,
        Igi2HumanChannel.WeaponCategory1 + 5,
        Igi2HumanChannel.WeaponCategory1 + 6,
        Igi2HumanChannel.WeaponCategory9,
    ];

    /// <summary>Writes a command.</summary>
    /// <param name="frame">The player's channel frame, <see cref="Igi2HumanChannels.Count"/> values.</param>
    /// <param name="start">How the player stood as the frame was run.</param>
    /// <param name="end">What the frame left it with.</param>
    /// <param name="command">Where the command goes, <see cref="ChannelCount"/> values.</param>
    public static void Pack(ReadOnlySpan<float> frame, in Igi2CommandStart start, in Igi2CommandEnd end, Span<float> command)
    {
        frame[..Igi2HumanChannels.Count].CopyTo(command);
        command[Gamma] = start.IdealGamma;
        command[Pitch] = start.IdealPitch;
        command[Flags] = HasState
            | (start.Unfiltered ? UnfilteredLook : 0)
            | (start.SteadyTurns ? SteadyTurnsFlag : 0)
            | (start.InvertMouse ? InvertMouseFlag : 0)
            | (end.Walk ? WalkFlag : 0)
            | ((end.Climb & ClimbMask) << ClimbShift)
            | (start.Movement == Igi2PlayerMovement.Igi1 ? Igi1MovementFlag : 0)
            | (start.Igi1Weight ? Igi1WeightFlag : 0)
            | (start.Loadout == Igi2Loadout.Tactical ? TacticalLoadoutFlag : 0)
            | (start.GrenadeCook == GrenadeCook.Manual ? ManualCookFlag : 0);
        command[Chosen] = end.Chosen < 0 ? 0f : end.Chosen + 1;
        command[SeedAt] = start.Seed & SeedLimit;
        command[FieldOfViewAt] = end.FieldOfView;
        command[ObjectFieldOfViewAt] = end.ObjectFieldOfView;
        AimRayChannels.Pack(command.Slice(AimAt, AimRayChannels.Count), start.Aim);
    }

    /// <summary>
    /// Reads a command: the channel frame, with the weapon its client chose put in as the pick and its weapon steps taken out.
    /// </summary>
    /// <param name="command">The command, <see cref="ChannelCount"/> values.</param>
    /// <param name="frame">Where the channel frame goes, <see cref="Igi2HumanChannels.Count"/> values.</param>
    /// <param name="end">What the frame left the client's player with, when the command carries it.</param>
    /// <returns>
    /// How the player stood as the frame was run, a pitch out of the view's reach held to it; or <see langword="null"/> for a
    /// command that carries none.
    /// </returns>
    public static Igi2CommandStart? Unpack(ReadOnlySpan<float> command, Span<float> frame, out Igi2CommandEnd? end)
    {
        command[..Igi2HumanChannels.Count].CopyTo(frame);
        end = null;
        float chosen = command[Chosen] - 1f;
        if (float.IsFinite(chosen) && chosen >= 0f)
        {
            foreach (Igi2HumanChannel step in WeaponSteps)
            {
                frame[(int)step] = 0f;
            }

            frame[(int)Igi2HumanChannel.SelectWeapon] = MathF.Floor(chosen);
        }

        int flags = float.IsFinite(command[Flags]) ? (int)command[Flags] : 0;
        if ((flags & HasState) == 0 || !float.IsFinite(command[Gamma]) || !float.IsFinite(command[Pitch]))
        {
            return null;
        }

        float seed = command[SeedAt];
        float fieldOfView = command[FieldOfViewAt];
        float objectFieldOfView = command[ObjectFieldOfViewAt];
        if (float.IsFinite(fieldOfView) && fieldOfView > 0f && float.IsFinite(objectFieldOfView) && objectFieldOfView > 0f)
        {
            end = new Igi2CommandEnd(
                float.IsFinite(chosen) && chosen >= 0f ? (int)chosen : -1,
                (flags & WalkFlag) != 0,
                Math.Clamp(fieldOfView, MinFieldOfView, MaxFieldOfView),
                Math.Clamp(objectFieldOfView, MinFieldOfView, MaxFieldOfView),
                (flags >> ClimbShift) & ClimbMask);
        }

        return new Igi2CommandStart(
            command[Gamma],
            Math.Clamp(command[Pitch], -Igi2HumanView.PitchLimit, Igi2HumanView.PitchLimit),
            (flags & UnfilteredLook) != 0,
            (flags & SteadyTurnsFlag) != 0,
            (flags & InvertMouseFlag) != 0,
            (flags & Igi1MovementFlag) != 0 ? Igi2PlayerMovement.Igi1 : Igi2PlayerMovement.Igi2,
            (flags & Igi1WeightFlag) != 0,
            float.IsFinite(seed) && seed >= 0f ? (uint)Math.Min(seed, SeedLimit) : 0u,
            (flags & TacticalLoadoutFlag) != 0 ? Igi2Loadout.Tactical : Igi2Loadout.Original,
            (flags & ManualCookFlag) != 0 ? GrenadeCook.Manual : GrenadeCook.Original,
            AimRayChannels.Unpack(command.Slice(AimAt, AimRayChannels.Count)));
    }

    /// <summary>Sets a player's stream from a command's seed, as both machines do before the command runs.</summary>
    /// <param name="random">The player's stream.</param>
    /// <param name="seed">The seed.</param>
    public static void Reseed(IgiRandom random, uint seed)
    {
        ArgumentNullException.ThrowIfNull(random);
        random.Seed(seed | 1u, seed ^ 0x9E3779B9u, ~seed | 2u);
    }

    /// <summary>The seed a client gives its command for a tick: its tick number spread over the seed's bits.</summary>
    /// <param name="tick">The client's tick.</param>
    /// <returns>The seed.</returns>
    public static uint SeedFor(long tick) => unchecked((uint)tick * 2654435761u) & SeedLimit;

    // The field of view a command may set: a zoom's narrowest to a wide view's widest, so a client can't widen the server's.
    private const float MinFieldOfView = 0.01f;
    private const float MaxFieldOfView = 2f;
}
