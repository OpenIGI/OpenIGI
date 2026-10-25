using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>What the level's lamps read of the host.</summary>
internal interface ILevelLampsHost
{
    /// <summary>Gets the lighting model, which holds the lamps' switchboard and fixtures.</summary>
    GlobalIllumination Gi { get; }

    /// <summary>Gets the eye the last frame was drawn from, whichever camera claimed it.</summary>
    Camera3D Eye { get; }

    /// <summary>Gets the level's placed collision, which a wall in front of a lamp stops the aim at; null for none.</summary>
    ObjectCollisionIndex? Solids { get; }

    /// <summary>Gets what an impact is traced against, which says whether a lamp is indoors.</summary>
    WorldRayCast? EffectWalls { get; }

    /// <summary>Gets what shelters the level from the sky, which says whether a lamp is under a roof.</summary>
    ShelterHeightField Shelter { get; }

    /// <summary>Gets a value indicating whether the sun is pinned (<c>-sundir</c>), which leaves the daylight out.</summary>
    bool SunPinned { get; }

    /// <summary>Gets the level's light by the hour.</summary>
    TimeOfDayLighting TimeOfDay { get; }

    /// <summary>Gets the sound system the lamps hum and strike through, or null with none.</summary>
    SoundSystem? Sounds { get; }

    /// <summary>Gets a value indicating whether a level is loaded.</summary>
    bool HasScene { get; }
}

/// <summary>
/// The level's lamps as a player meets them: the wall switch that aims at one and turns it off or breaks it, the photocells
/// that put them out by day, their hum and strike, and the bounce catching up with them.
/// </summary>
/// <param name="device">The device, told when the lamps' levels change so their shadows' history is dropped.</param>
/// <param name="host">What the lamps read of the host.</param>
/// <remarks>
/// The aim is cast from whichever eye is drawing, which is what makes this work while walking and while flying without
/// either mode knowing about it: the free camera and the player's head are two sources of the same <see cref="Camera3D"/>.
/// </remarks>
internal sealed class LevelLamps(IGraphicsDevice device, ILevelLampsHost host) : IHostService
{
    /// <inheritdoc/>
    public string Name => "Lamps";

    /// <summary>How far down the crosshair a lamp may stand and still be switchable, in metres.</summary>
    /// <remarks>
    /// Far enough to reach the fittings at the far end of a hangar, short enough that a lamp two rooms
    /// away — which the player cannot see and would not be able to tell had changed — is not what a key
    /// press lands on.
    /// </remarks>
    private const double LampReach = 60.0;

    // Which lamps the daylight switches, and what the lamps sound like.
    private readonly LampDaylightControl _lampDaylight = new();
    private int _lampMilliseconds;
    private readonly LampSoundPlayer _lampSounds = new();

    /// <summary>How much closer than the lamp something has to be to count as standing in front of it.</summary>
    /// <remarks>
    /// The fixture's position is the centroid of its emitting triangles and the collision ray stops at
    /// the housing's surface, so the world is legitimately a little nearer than the light every time. The
    /// slack is that gap plus the fixture's own size; anything closer than that is a wall.
    /// </remarks>
    private const double LampOcclusionSlack = 0.5;

    /// <summary>Registers the commands the lamp keys are bound to.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// Commands rather than keys read from the loop, so the two are rebindable, scriptable and reachable
    /// from <c>+lamp_switch</c> on the command line — and so a headless capture can set a corridor up
    /// exactly the same way twice. <c>DefaultBinds</c> is what puts them on keys out of the box.
    /// </remarks>
    public void RegisterCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "lamp_switch",
            "Switches the lamp under the crosshair off, or a switched-off one back on.",
            context => AimedLamp(context, (board, index) => board.Switch(index)));

        console.Commands.Add(
            "lamp_break",
            "Makes the lamp under the crosshair flicker like a failing tube, or repairs one that does.",
            context => AimedLamp(context, (board, index) => board.Break(index)));

        console.Commands.Add(
            "lamp_reset",
            "Puts every lamp in the level back on.",
            context =>
            {
                if (host.Gi.LampSwitchboard is not { Count: > 0 } board)
                {
                    context.Warn("This level has no lamps to reset.");
                    return;
                }

                board.Reset();
                context.Print(
                    $"Every one of the level's {board.Count} lamp(s) is back on at the wall; the daylight still "
                        + "decides which of them are lit.");
            });
    }

    /// <summary>Runs an action on the lamp the crosshair is on, and reports what happened.</summary>
    private void AimedLamp(ConsoleCommandContext context, Action<LampSwitchboard, int> act)
    {
        if (host.Gi.LampSwitchboard is not { Count: > 0 } board)
        {
            context.Warn(
                !host.HasScene
                    ? "No level is loaded."
                    : "This level proposes no lamps, so there is nothing to switch. r_gi_lamps 0 turns "
                        + "the detection off entirely, and a level whose fixtures file is empty has none.");
            return;
        }

        if (!TryAimedLamp(board, out int index, out double distance))
        {
            context.Print("No lamp under the crosshair.");
            return;
        }

        act(board, index);
        context.Print(FormattableString.Invariant(
            $"Lamp {index} at {distance:0.#} m is now {Describe(board, index)}."));
    }

    /// <summary>Says what state a lamp is in, in the terms somebody flipping a switch thinks in.</summary>
    /// <remarks>
    /// The fault and the switch are two separate facts about a fitting — a lamp can be switched off AND
    /// have a dying tube in it, and turning it back on will show that — so both are reported rather than
    /// one standing in for the other.
    /// </remarks>
    /// <remarks>
    /// The daylight is a third: a lamp switched on at the wall that its photocell has put out for the day
    /// is on and dark, and says so.
    /// </remarks>
    private static string Describe(LampSwitchboard board, int index) => board.ModeOf(index) switch
    {
        LampMode.Off => board.IsFaulty(index) ? "off, with a faulty tube" : "off",
        LampMode.Broken => board.IsSupplied(index) ? "broken, and flickering" : "broken, and dark until dusk",
        _ => board.IsSupplied(index) ? "on" : "on, and dark until dusk",
    };

    /// <summary>Finds the lamp the frame's own eye is looking at, with the level's walls respected.</summary>
    /// <param name="board">The level's lamps.</param>
    /// <param name="index">Receives the fixture's index.</param>
    /// <param name="distance">Receives how far away it is, in metres.</param>
    /// <returns><see langword="true"/> when a lamp is under the crosshair and nothing is in front of it.</returns>
    /// <remarks>
    /// <para>
    /// Cast from <c>host.Eye</c>, which is the pose the last frame was drawn from whichever camera
    /// claimed it. That is one frame stale, which is exactly nothing for a key press: a person aiming at
    /// a lamp is holding still on it.
    /// </para>
    /// <para>
    /// The occlusion test is the level's own collision rather than anything the lamps know about, because
    /// a wall is not in the fixture list and never will be. Without it the crosshair reaches lamps in the
    /// next room, and a key press switches off a light nobody can see change — which reads as the key
    /// doing nothing.
    /// </para>
    /// </remarks>
    private bool TryAimedLamp(LampSwitchboard board, out int index, out double distance)
    {
        Camera3D eye = host.Eye;
        Vector3f forward = eye.Orientation.Row3;

        if (!board.TryPick(
                eye.Position,
                new Vector3d(forward.X, forward.Y, forward.Z),
                LampReach,
                out index,
                out distance))
        {
            return false;
        }

        if (host.Solids is null)
        {
            return true;
        }

        Vector3d originU = eye.Position * WorldScene.UnitsPerMetre;
        Vector3d endU = originU + (new Vector3d(forward.X, forward.Y, forward.Z) * (distance * WorldScene.UnitsPerMetre));

        if (host.Solids.TryIdentify(originU, endU, out _, out WorldRayHit hit))
        {
            double hitMetres = hit.Distance / WorldScene.UnitsPerMetre;
            double slack = LampOcclusionSlack + host.Gi.LightFixtures[index].Extent;

            if (hitMetres < distance - slack)
            {
                index = -1;
                distance = 0.0;
                return false;
            }
        }

        return true;
    }

    /// <summary>Advances every lamp, and lets the bounce catch up with them.</summary>
    /// <param name="milliseconds">The loop's real-time clock.</param>
    /// <param name="audible">Whether what the lamps do is to be heard: a level being played, not a backdrop.</param>
    /// <remarks>
    /// <para>
    /// Real elapsed time rather than the tick, and on every pass of the loop rather than every rendered
    /// frame, for the same reason the camera and the day/night sweep are: a fade tied to the frame rate
    /// is a different fade on a different machine, and a lamp striking is a tenth of a second of shape
    /// that a coarse step walks straight over.
    /// </para>
    /// <para>
    /// The surface cache's emitters are a storage buffer, so they are re-uploaded when the board says
    /// something moved and not otherwise — a level nobody has touched never uploads at all, and a
    /// flickering tube's bounce trails its own glow by a frame or two, which is invisible against a
    /// bounce that takes far longer than that to converge in the first place.
    /// </para>
    /// </remarks>
    public void Update(int milliseconds, bool audible)
    {
        if (host.Gi.LampSwitchboard is not { } board)
        {
            _lampMilliseconds = milliseconds;
            return;
        }

        float elapsed = Math.Clamp((milliseconds - _lampMilliseconds) / 1000f, 0f, 0.25f);
        _lampMilliseconds = milliseconds;

        // The daylight first, so a lamp its photocell switches this pass starts its fade this pass too.
        _lampDaylight.Update(
            board,
            elapsed,
            host.Gi.LightFixtures,
            host.Gi.LampsMissionId,
            host.Shelter,
            host.EffectWalls,
            host.SunPinned,
            host.TimeOfDay.DayNight,
            host.TimeOfDay.BoundSun ?? host.TimeOfDay.Sun ?? TimeOfDayLighting.FallbackSun,
            host.TimeOfDay.WeatherCloudCover());
        board.Update(elapsed);
        _lampSounds.Play(board, elapsed, audible, host.Sounds, host.Gi.LightFixtures);

        if (board.Revision != host.Gi.LampBounceRevision)
        {
            host.Gi.LampBounceRevision = board.Revision;
            host.Gi.CardLighting?.SetLocalLightLevels(board);

            // And the lamp shadows' history, which would otherwise hold the old lamps' verdict for half a
            // second after one switched.
            device.NoteLampLevelsChanged();
        }
    }

    // Which fixtures this frame submits, and how far outside its own reach the camera stands for each.
    // Held rather than allocated per frame: this runs on every frame of every level that has lamps.
    private readonly int[] _nearestOrder = new int[DeferredLocalLight.Maximum];
    private readonly float[] _nearestRank = new float[DeferredLocalLight.Maximum];

    /// <summary>Gets the fixtures the last <see cref="SelectNearest"/> chose, nearest first.</summary>
    public ReadOnlySpan<int> NearestOrder => _nearestOrder;

    /// <summary>Chooses which of the level's lamps this frame submits, nearest first.</summary>
    /// <param name="eye">Where the camera stands, in world metres.</param>
    /// <param name="range">Unused by the ranking; see the remarks on why it must not scale it.</param>
    /// <returns>How many entries of <see cref="NearestOrder"/> are valid.</returns>
    /// <remarks>
    /// <para>
    /// A level's lamps outnumber the shader's array several times over, so this is a choice and not a
    /// truncation. Each fixture is ranked by how far OUTSIDE its own reach the camera stands: a lamp the
    /// camera is inside ranks negative and always makes the cut, and beyond that a long fluorescent
    /// batten twenty metres off beats a desk lamp fifteen metres off, which is the right way round
    /// because reach is what decides whether a lamp can light anything the player can see.
    /// </para>
    /// <para>
    /// The camera's own position rather than the frustum: a lamp behind the player still lights the wall
    /// in front of them, and culling to what is on screen would switch that light off as they turned.
    /// </para>
    /// <para>
    /// A running insertion into an array of thirty-two rather than a sort of the whole list. The list is
    /// a few hundred long and this runs every frame; an insertion sort over a fixed, tiny destination is
    /// linear in the input and allocates nothing, where sorting the input is neither.
    /// </para>
    /// </remarks>
    public int SelectNearest(Vector3d eye, float range)
    {
        int kept = 0;

        for (int i = 0; i < host.Gi.LightFixtures.Count; i++)
        {
            LightFixture fixture = host.Gi.LightFixtures[i];

            // A lamp that has finished going out is not a dim light, it is not a light: passed over here
            // rather than submitted at zero, because the shader's array is a budget and a dark fixture
            // holding a slot switches a real one at the end of the corridor off instead.
            if (host.Gi.LampSwitchboard is { } board && board.LevelOf(i) <= 0f)
            {
                continue;
            }

            double dx = fixture.Position.X - eye.X;
            double dy = fixture.Position.Y - eye.Y;
            double dz = fixture.Position.Z - eye.Z;
            // The fixture's OWN size, not its size times the reach multiplier. Scaling this by the
            // multiplier meant raising r_local_light_range -- which people raise to make lamps carry
            // further -- also multiplied how much a fixture's size counted for a slot, so the big
            // battens took the budget and the small near fittings went dark EARLIER than before. Reach
            // decides how far a lamp lights; this decides which lamps are worth submitting, and the two
            // should not move together.
            float rank = (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) - fixture.Radius;

            // Nothing to insert: this fixture is further out than every one already held, and the array
            // is full. The common case by far once the level is more than thirty-two lamps deep.
            if (kept == _nearestOrder.Length && rank >= _nearestRank[kept - 1])
            {
                continue;
            }

            int at = Math.Min(kept, _nearestOrder.Length - 1);

            while (at > 0 && _nearestRank[at - 1] > rank)
            {
                _nearestRank[at] = _nearestRank[at - 1];
                _nearestOrder[at] = _nearestOrder[at - 1];
                at--;
            }

            _nearestRank[at] = rank;
            _nearestOrder[at] = i;

            if (kept < _nearestOrder.Length)
            {
                kept++;
            }
        }

        return kept;
    }

    /// <summary>Gets which lamps the daylight switches, for the lighting dump.</summary>
    public LampDaylightControl Daylight => _lampDaylight;

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <summary>Describes the lamp under the crosshair, for the debug overlay.</summary>
    /// <returns>The line, or <see langword="null"/> when the crosshair is not on a lamp.</returns>
    public string? DescribeAimed()
    {
        if (host.Gi.LampSwitchboard is not { Count: > 0 } board
            || !TryAimedLamp(board, out int index, out double distance))
        {
            return null;
        }

        LightFixture fixture = host.Gi.LightFixtures[index];
        string daylight = _lampDaylight.Photocells is { } cells && cells.Follows(index)
            ? FormattableString.Invariant($"dusk circuit {cells.CircuitOf(index)} on below {cells.ThresholdsOf(index).On:0.000}")
            : "kept on by day";

        return FormattableString.Invariant(
            $"lamp: #{index} {Describe(board, index)} at {board.LevelOf(index) * 100f:F0}%, {distance:F1}m, {fixture.Texture}, {board.CharacterOf(index)}, {daylight}");
    }
}
