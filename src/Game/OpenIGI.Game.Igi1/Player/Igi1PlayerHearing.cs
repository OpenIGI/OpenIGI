using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// What Project I.G.I.'s soldiers hear of its player, wherever the two stand: its rounds landing, the noises its doings make, its
/// gunfire and its steps. The game's own mission and another game's level the two stand in tell the soldiers alike.
/// </summary>
public static class Igi1PlayerHearing
{
    /// <summary>Has the soldiers hear what the player sets off as it acts: its rounds landing, and the noises its doings make.</summary>
    /// <param name="player">The player.</param>
    /// <param name="soldiers">The soldiers.</param>
    public static void Attach(PlayerCamera player, SoldierSimulation soldiers)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(soldiers);

        // A round's impact, which another game's humans tell from a landing by its flight from the eye.
        player.GroundImpact = impact => soldiers.NotifyGroundImpact(
            impact,
            player.Body.Position,
            flight: new Vector3f(
                (float)(impact.X - player.Body.Eye.X), (float)(impact.Y - player.Body.Eye.Y), (float)(impact.Z - player.Body.Eye.Z)));
        player.AiNoise = noise => soldiers.NotifyHumanNoise(noise, player.Body.Position, player.Body.Position);
    }

    /// <summary>Has the soldiers hear the round the player fired this tick, when it fired one.</summary>
    /// <param name="player">The player.</param>
    /// <param name="soldiers">The soldiers, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// Hearing is emitted once per round, not once per projectile intersection. The weapon row carries the exact retail radius (4096
    /// for suppressed fire, 122880 for an ordinary rifle), so shotguns post no duplicate noise and silenced weapons do not wake the
    /// whole neighbourhood. The line is along the view, as far as the gun reaches: what another game's humans see the shot by.
    /// </remarks>
    public static void Gunshot(PlayerCamera player, SoldierSimulation? soldiers)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (soldiers is null || !player.FiredThisTick || player.HeldWeaponType is not { } weapon)
        {
            return;
        }

        Matrix3f view = player.Camera.Orientation;
        float reach = weapon.EffectiveRange * (float)WorldScene.UnitsPerMetre;
        soldiers.NotifyGunshot(player.Body.Eye, weapon.AiDetectionRange, line: new Vector3f(view.M31 * reach, view.M32 * reach, view.M33 * reach));
    }

    /// <summary>Has the soldiers hear a step the player took, unless it crouches.</summary>
    /// <param name="player">The player.</param>
    /// <param name="step">The step.</param>
    /// <param name="soldiers">The soldiers, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// <c>0x463920</c>'s tail posts <c>AIEVENT_WALK</c> (type 10, filter <c>0x46</c>) with the ground material's own radius, unless the
    /// walker is crouching -- sneaking really is silent to the AI. <c>guard.qvm</c>'s WALK branch is <c>AIAction_SetCombat</c>, so a
    /// guard that hears footsteps it cannot see starts hunting.
    /// </remarks>
    public static void Step(PlayerCamera player, in Footstep step, SoldierSimulation? soldiers)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (soldiers is not null && !player.IsCrouched)
        {
            soldiers.NotifyWalk(step.PositionInUnits, player.Body.Position, step.HearingRadiusInUnits);
        }
    }
}
