using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Read-only actor state for diagnosing the live desktop startup path.</content>
public sealed partial class Igi2TitlePlayer : ITitlePlayerDiagnostics
{
    /// <inheritdoc/>
    public IEnumerable<string> DescribeActors()
    {
        yield return $"levelTicks={Level?.Ticks}, flowTick={Level?.LevelFlow?.Tick}, aiTick={Ai?.LevelTick()}, playerTicks={_ticks}, guards={Soldiers?.Bodies.Count ?? 0}, "
            + $"ai={Ai is not null}, defaultScript={Ai?.Default is not null}, squadScript={Ai?.SquadDefault is not null}, "
            + $"nativeNamespaceMatches={ReferenceEquals(Ai?.Natives, Level?.Natives)}, cutscene={IsCutscenePlaying}";
        yield return $"playerTeam={Human.Team}, playerActive={Human.DetectionInfo().IsActive}, playerDead={Human.IsDead}, "
            + $"aiPlayerMatches={ReferenceEquals(Ai?.Players.Primary, Human)}, playerPosition={Human.Position}";
        yield return $"playerState={Human.Locomotion.CurrentState}, action={Human.ActionState}, held={Inventory?.Current?.WeaponType.WeaponName}, "
            + $"zoomAllowed={Human.IsZoomedModeAllowed}, overlayStatus={Weapons?.Firing.OverlayStatus}, overlay={ZoomOverlay}";
        if (Soldiers is not { } soldiers) { yield break; }
        foreach (Soldiers.Igi2HumanSoldier body in soldiers.Bodies.OrderBy(body =>
            (body.Position - Human.Position).LengthSquared).Take(8))
        {
            Igi2HumanAi? mind = body.Mind as Igi2HumanAi;
            string clip = (body.Animation as Igi2HumanAnimation)?.Main.Clip?.Name ?? "none";
            yield return $"AI {mind?.Task.Id}: active={body.IsActive}, dead={body.IsDead}, bodyTicks={body.MissionTime}, "
                + $"started={mind?.HasStarted}, script={mind?.Script is not null}, graph={mind?.Task.GraphId}/{mind?.Graph is not null}, "
                + $"squad={mind?.Squad is not null}, state={body.Locomotion.CurrentState}, clip={clip}, "
                + $"animation={body.Animation.MainTime}/{body.Animation.MainTotalTime}, step={body.Animation.MainTimeStep}, "
                + $"animSpeed={mind?.Tweaks[Igi2AiTweakIndex.AnimSpeedFactor]}, position={body.Position}";
            yield return $"  team={body.Team}, locked={body.Channels.IsLocked}, signal={body.Signal}, upper={body.UpperBodyState}, "
                + $"action={mind?.Actions.Top?.Run?.Method.Name}, child={mind?.Actions.Top?.ChildRun?.Method.Name}, "
                + $"patrols={mind?.Patrols.Count}, enemies={mind?.Detection.Enemies.Count}, visible={mind?.VisibleTargetCount()}, "
                + $"squadState={mind?.Squad?.State}, squadTargets={mind?.Squad?.Targets.Count}, "
                + $"ammo={mind?.AmmoInWeapon()}, allowedFire={mind?.IsAllowedToFire}, fireDelay={mind?.FireDelayTimer}";
        }
    }
}
