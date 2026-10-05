using OpenIGI.Core.Configuration;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Configuration;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// The wall switch: aiming at one of a level's lamps and turning it off, or breaking it.
/// </summary>
/// <remarks>
/// <para>
/// A partial of the game rather than a type of its own, for the same reason the inspector is one:
/// everything here needs what the loop already holds — the eye the frame was drawn from, the level's
/// collision, the fixtures and the surface cache.
/// </para>
/// <para>
/// The aim is cast from whichever eye is drawing, which is what makes this work while walking and while
/// flying without either mode knowing about it: the free camera and the player's head are two sources of
/// the same <see cref="Camera3D"/>, and the frame loop has already settled which one won by the time a
/// key is read.
/// </para>
/// </remarks>
internal sealed partial class GameHost
{
    /// <summary>How far down the crosshair a lamp may stand and still be switchable, in metres.</summary>
    /// <remarks>
    /// Far enough to reach the fittings at the far end of a hangar, short enough that a lamp two rooms
    /// away — which the player cannot see and would not be able to tell had changed — is not what a key
    /// press lands on.
    /// </remarks>
    private const double LampReach = 60.0;

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
    private void RegisterLampCommands(ConsoleSystem console)
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
                if (_lampSwitchboard is not { Count: > 0 } board)
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
        if (_lampSwitchboard is not { Count: > 0 } board)
        {
            context.Warn(
                _scene is null
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
    /// Cast from <c>_inspectorEye</c>, which is the pose the last frame was drawn from whichever camera
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
        Camera3D eye = _inspectorEye;
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

        if (_worldSolids is null)
        {
            return true;
        }

        Vector3d originU = eye.Position * WorldScene.UnitsPerMetre;
        Vector3d endU = originU + (new Vector3d(forward.X, forward.Y, forward.Z) * (distance * WorldScene.UnitsPerMetre));

        if (_worldSolids.TryIdentify(originU, endU, out _, out WorldRayHit hit))
        {
            double hitMetres = hit.Distance / WorldScene.UnitsPerMetre;
            double slack = LampOcclusionSlack + _lightFixtures[index].Extent;

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
    private void UpdateLamps(int milliseconds, bool audible)
    {
        if (_lampSwitchboard is not { } board)
        {
            _lampMilliseconds = milliseconds;
            return;
        }

        float elapsed = Math.Clamp((milliseconds - _lampMilliseconds) / 1000f, 0f, 0.25f);
        _lampMilliseconds = milliseconds;

        // The daylight first, so a lamp its photocell switches this pass starts its fade this pass too.
        UpdateLampDaylight(board, elapsed);
        board.Update(elapsed);
        PlayLampSounds(board, elapsed, audible);

        if (board.Revision != _lampBounceRevision)
        {
            _lampBounceRevision = board.Revision;
            _cardLighting?.SetLocalLightLevels(board);

            // And the lamp shadows' history, which would otherwise hold the old lamps' verdict for half a
            // second after one switched.
            _device.NoteLampLevelsChanged();
        }
    }

    /// <summary>Describes the lamp under the crosshair, for the debug overlay.</summary>
    /// <returns>The line, or <see langword="null"/> when the crosshair is not on a lamp.</returns>
    private string? DescribeAimedLamp()
    {
        if (_lampSwitchboard is not { Count: > 0 } board
            || !TryAimedLamp(board, out int index, out double distance))
        {
            return null;
        }

        LightFixture fixture = _lightFixtures[index];
        string daylight = _lampPhotocells is { } cells && cells.Follows(index)
            ? FormattableString.Invariant($"dusk circuit {cells.CircuitOf(index)} on below {cells.ThresholdsOf(index).On:0.000}")
            : "kept on by day";

        return FormattableString.Invariant(
            $"lamp: #{index} {Describe(board, index)} at {board.LevelOf(index) * 100f:F0}%, {distance:F1}m, {fixture.Texture}, {board.CharacterOf(index)}, {daylight}");
    }
}
