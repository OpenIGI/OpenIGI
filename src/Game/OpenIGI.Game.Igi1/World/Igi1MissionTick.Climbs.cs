using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>The ladders and the world interactions the mission's own player is in, a tick at a time.</summary>
public sealed partial class Igi1MissionTick
{
    // The own player's ladder body when nothing draws it: a dedicated server's, a headless run's.
    private ILadderBody? _ownLadderBody;

    /// <summary>Advances the ladder and the world interactions on the fixed tick, and the camera that frames them.</summary>
    private void TickClimbsAndInteractions()
    {
        ISoldierPresenter? soldiers = mission.SoldierPresenter;
        // The complete IGI1 ladder graph: 37 pauses animation 168 at authored event 8, 38/39 play
        // animation 170 backwards/forwards at the top, and 40 loops 169 while its damped velocity
        // falls to a ground collision. Every advance happens here, on the fixed 30 Hz tick.
        if (mission.Player is not null && mission.Player.IsOnLadder)
        {
            mission.Player.InputProfile = host.InputProfile;

            // Up, down and use as the tick's input frame has them -- the frame a co-op client sends its server, whose copy
            // climbs on exactly these (Igi1NetworkPlayer), so both copies take the same rung and start the same slide. A body
            // nothing draws -- a dedicated server's, a headless run's -- climbs on one only the simulation keeps, as a
            // partner's does.
            PlayerInputFrame frame = mission.Player.LastFrame;
            ILadderBody? body = soldiers is not null
                ? new PresentedLadderBody(soldiers)
                : _ownLadderBody ??= mission.Player.BodyAnimations is { } set ? new SimulatedLadderBody(set) : null;
            _ownClimb.Tick(
                mission.Player,
                body,
                frame[(int)PlayerInputSlot.MoveUp] > 0.5f,
                frame[(int)PlayerInputSlot.MoveDown] > 0.5f,
                frame[(int)PlayerInputSlot.Activate] > 0.5f,
                host.Ground!,
                LadderRung);

            // All four states stay in third person until their actual deinitialization signal.
            if (mission.Player.IsOnLadder)
            {
                if (!host.IsInteractionViewActive)
                {
                    WorldRayCast? cameraCast = mission.Solids?.Caster();
                    host.BeginInteractionView(mission.Player.LadderFeet, mission.Player.LadderFacing, cameraCast);
                }
                else
                {
                    host.FollowInteractionView(mission.Player.LadderFeet);
                }

                host.TickInteractionView();
            }
        }

        // Outside the ladder block on purpose: the tick the slide reaches the ground has already left the ladder, and that
        // tick is the one that must stop the sound.
        audio.TickLadderSlideSound(mission.Player);

        if (mission.Player is { IsInWorldInteraction: true } interacting)
        {
            _ownInteraction.Tick(interacting, soldiers is null ? null : new PresentedLadderBody(soldiers), audio.PlayLadderStepSound);
            if (interacting.IsMountedStationaryGun)
            {
                // The gun's sight sits just in front of the gunner's own chest, and retail draws
                // no body in that view (captured in mission 4: barrel, stand and scenery only).
                mission.Soldiers?.HidePlayerBodyClimbing();
            }

            Vector3d interactionCameraFeet = mission.Soldiers?.PlayerInteractionCameraFeet(
                interacting.InteractionFeet) ?? interacting.InteractionFeet;

            // State 33 is the one interaction that keeps a first-person camera: the gun hands
            // the view its own sight (0x474420), so no orbit is started behind the body.
            if (interacting.IsMountedStationaryGun)
            {
                host.ResetInteractionView();
            }
            else if (!host.IsInteractionViewActive)
            {
                WorldRayCast? cameraCast = mission.Solids?.Caster();
                host.BeginInteractionView(interactionCameraFeet, interacting.InteractionFacing, cameraCast);
            }
            else
            {
                host.FollowInteractionView(interactionCameraFeet);
            }

            if (host.IsInteractionViewActive)
            {
                host.TickInteractionView();
            }
        }

        if ((mission.Player is null || (!mission.Player.IsOnLadder && !mission.Player.IsInWorldInteraction))
            && (_ownClimb.IsActive || _ownInteraction.IsActive || host.IsInteractionViewActive))
        {
            mission.Soldiers?.HidePlayerBodyClimbing();
            host.ResetInteractionView();
            _ownClimb.Reset();
            _ownInteraction.Reset();
        }
    }
}
