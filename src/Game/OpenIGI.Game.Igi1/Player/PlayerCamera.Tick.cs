using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.World;
using OpenIGI.Input;

namespace OpenIGI.Game.Igi1.Player;

public sealed partial class PlayerCamera
{
    /// <summary>Advances the body by one simulation tick.</summary>
    /// <param name="input">The window's input.</param>
    /// <param name="terrain">The level's terrain.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The order is <c>HumanPlayer_Update</c>'s, and it is load-bearing in two places. The weapon
    /// machine runs <b>before</b> the rig's clips advance, so it sees the clip-end flag as of the last
    /// advance — which is what makes "the animation has finished" a one-tick-late signal in the original
    /// too. And the trigger message goes to the gun <b>after</b> the rig has been placed, because the
    /// muzzle is this tick's eye.
    /// </para>
    /// </remarks>
    public void Tick(InputState input, IGroundHeightField? terrain)
    {
        ArgumentNullException.ThrowIfNull(input);
        TickFrame(PlayerInputSampler.SampleFrame(input, InputProfile), terrain);
    }

    /// <summary>Advances the body by one simulation tick on a frame of input already sampled.</summary>
    /// <param name="frame">The tick's input frame: the host's own controls, or a client's command on a server.</param>
    /// <param name="terrain">The level's terrain.</param>
    /// <remarks>The rest of <see cref="Tick"/>: everything after the controls are read is the same for a player at the keyboard and one across the network.</remarks>
    public void TickFrame(in PlayerInputFrame frame, IGroundHeightField? terrain)
    {
        ApplyCheats(PlayerCheatOptions.Current);

        _input.Accept(frame);
        _locomotion.TakeToggle(_input);
        _interactions?.Tick();

        // On a ladder the ordinary walk, gravity and collision do not run. The rung-by-rung climb (state
        // 37) is driven by the host once per tick through DecideLadderStep and the body clip, because
        // it is coupled to the third-person body's animation; nothing to do here.
        if (_ladder.IsOnLadder)
        {
            return;
        }

        if (_interaction.Active is { } active)
        {
            InteractionActivateType type = active.Type;
            _interaction.Tick(terrain);

            // A finished fence vault enters state 29 inside HumanPlayer_Update's state machine (0x4109A0), and
            // the same update's mover then makes the fall's first move. Every other interaction stays put
            // for the tick it ends on, as before.
            if (_interaction.Active is not null || type != InteractionActivateType.Fence)
            {
                return;
            }
        }

        if (_interaction.OfferAndActivate(_ladder, _interactions, terrain))
        {
            return;
        }

        PlayerCommand command = PlayerInputSampler.Command(_input, _locomotion.Running, CanJump);

        Vector3d previousPosition = _body.Position;

        // The weapon machine, before the clips advance: 0x4EC1E0 over the weapon table runs in
        // HumanPlayer_Update ahead of 0x464800, so its clip-end flag is last tick's.
        TickWeapons();

        _locomotion.Move(command, terrain);

        _interaction.TouchFences(_interactions, previousPosition);

        // Diagnostic: name whatever the wall sweep stopped the body against, so a doorway you cannot walk
        // through can be traced to the exact model and task blocking it. Only while trying to move.
        _wallBlocks.LogIfAny(_walls, _interactions, command.Forward, command.Strafe);

        _pickups.Collect(_weapons);

        _firstPerson.Rig?.Tick();

        // Retail probes the normal placed-object collision level and terrain from the eye, not the
        // enlarged collision LOD used to keep the player's body out of narrow gaps. This affects only
        // the first-person rig: the camera and the fired round still begin at the true eye.
        SyncView();
        if (_firstPerson.Rig is { } view)
        {
            view.TickWallPullback(PlayerWorldTraces.ViewModel(_walls, TerrainTrace));
        }

        // Where 0x464BD0 steps it: after the state machines have read it and before the rig is placed.
        _weapons?.Sway.Advance();

        // The hit shake and the landing dip are stepped by the same function, in the same place, and for
        // the same reason: they displace the camera this tick places rather than the body the state
        // machines just moved. The shake comes first (0x464BD0).
        _firstPerson.AdvanceKicks(_random.NextDouble);

        PoseChanged = true;
        SyncView();

        // 0x462E50, which HumanPlayer_Update calls after 0x464BD0 has committed the view.
        ResolveFire();

        // After the fire, so a round spawned this tick flies its first segment in the same tick --
        // otherwise every shot would carry a one-tick delay on top of its real travel time.
        _shots.TickBallistics();

        // After the fire has (possibly) started a new clip, so the pair describes what this tick
        // actually shows.
        CaptureViewTickState();
        ViewError.Tick();
    }
}
