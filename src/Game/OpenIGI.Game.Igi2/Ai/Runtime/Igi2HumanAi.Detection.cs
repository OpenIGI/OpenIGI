using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <content>What a mind sees and how it looks (<c>HumanAI_RunDetection</c>, the views, the targets).</content>
public sealed partial class Igi2HumanAi
{
    private const double IndoorReach = 204800.0;
    private Igi2AiDetection? _detection;

    /// <summary>Gets its detection, <c>tAIDetection</c>, once readied.</summary>
    public Igi2AiDetection Detection => _detection ?? throw new InvalidOperationException("The mind's detection is not readied.");

    /// <summary>Gets the view its detection looks through, <c>tAIDetectionView</c>.</summary>
    public Igi2DetectionView DetectionView { get; } = new();

    /// <summary>Readies detection: friends dead and enemies alive of its soldier's side, a cone every 4 ticks, sight every 8 (<c>AIDetection_InitDetection</c>).</summary>
    private void InitDetection()
    {
        if (World.Detection is { } world)
        {
            _detection = new Igi2AiDetection(world, Soldier.Team, Igi2AiDetection.DeadFriends | Igi2AiDetection.LiveEnemies, Igi2DetectableKind.Human, 4, 8);
        }
    }

    /// <summary>Uses the combat view's cones, held to 50 metres indoors (<c>HumanAIContent_UseCombatView</c>).</summary>
    public void UseCombatView() => UseView(Igi2AiTweakIndex.CombatViewCone1Alpha, limitIndoors: true);

    /// <summary>Uses the investigating view's cones, held to 50 metres indoors (<c>HumanAIContent_UseInvestigateView</c>).</summary>
    public void UseInvestigateView() => UseView(Igi2AiTweakIndex.InvestigateViewCone1Alpha, limitIndoors: true);

    /// <summary>Uses the idle view's cones, held to 50 metres indoors unless told not to be (<c>HumanAIContent_UseIdleView</c>).</summary>
    public void UseIdleView() => UseView(Igi2AiTweakIndex.IdleViewCone1Alpha, limitIndoors: (Flags & 0x20) == 0);

    private void UseView(int first, bool limitIndoors)
    {
        double length = Tweaks[first + 1], secondary = Tweaks[first + 3];
        View.ConeAlpha = Tweaks[first];
        View.ConeGamma = Tweaks[first];
        View.ConeLength = length;
        View.SecondaryConeAlpha = Tweaks[first + 2];
        View.SecondaryConeGamma = Tweaks[first + 2];
        View.SecondaryConeLength = secondary;
        if (limitIndoors && World.IsIndoors(CurrentSector))
        {
            View.ConeLength = length >= IndoorReach ? IndoorReach : length;
            View.SecondaryConeLength = secondary >= IndoorReach ? IndoorReach : secondary;
        }
    }

    /// <summary>Puts the eyes where the soldier's head is, facing as its body faces; from its seat when it sits on a gun.</summary>
    private void PlaceView()
    {
        if (Soldier.SeatView is { } seat)
        {
            View.Position = seat.Position;
            View.Orientation = [.. seat.Orientation];
            View.ViewPosition = seat.Position;
            View.ViewOrientation = [.. seat.Orientation];
            return;
        }

        float[] body = Soldier.OrientationRows;
        View.Position = Soldier.CheapHeadBonePosition();
        View.Orientation = [.. body];
        View.ViewPosition = Soldier.CheapHeadBonePosition();
        View.ViewOrientation = [.. body];
    }

    /// <summary>
    /// The nearest enemy it lists that is alive, among those it sees when asked for only those (<c>HumanAI_GetTargetQTask</c>); with
    /// any allowed, one it sees first.
    /// </summary>
    /// <param name="visibleOnly">Whether only an enemy it sees counts.</param>
    /// <returns>The target, or <see langword="null"/>.</returns>
    public IIgi2Detectable? GetTarget(bool visibleOnly)
    {
        if (_detection is not { } detection)
        {
            return null;
        }

        if (!visibleOnly && GetTarget(visibleOnly: true) is { } seen)
        {
            return seen;
        }

        IIgi2Detectable? best = null;
        float bestDistance = 0f;
        foreach (Igi2DetectionItem item in detection.Enemies)
        {
            if (visibleOnly && !item.IsFullVisible)
            {
                continue;
            }

            if (item.Target is IAiHuman { IsDead: true })
            {
                continue;
            }

            Vector3d at = item.Target.DetectablePosition;
            float dx = ((Fpu)Soldier.Position.X - at.X).F, dy = ((Fpu)Soldier.Position.Y - at.Y).F;
            Fpu dz = (Fpu)Soldier.Position.Z - at.Z;
            Fpu distance = Fpu.Sqrt((((Fpu)dx * dx) + ((Fpu)dy * dy)) + (dz * dz));
            if (best is null || distance.Value < bestDistance)
            {
                bestDistance = distance.F;
                best = item.Target;
            }
        }

        return best;
    }

    /// <summary>
    /// Where its target is (<c>HumanAI_GetTargetPosition</c>): the target position while it sees one or lists a live enemy, else the
    /// last target position.
    /// </summary>
    /// <param name="target">Receives the target, or <see langword="null"/>.</param>
    /// <returns>The position.</returns>
    public Vector3d GetTargetPosition(out IIgi2Detectable? target)
    {
        target = GetTarget(visibleOnly: true) ?? GetTarget(visibleOnly: false);
        return target is not null ? TargetPosition : LastTargetPosition;
    }

    /// <summary>Whether an enemy it is still identifying shows a bone it fully sees inside its detection view (<c>sub_46D9F0</c>).</summary>
    /// <returns><see langword="true"/> when one does.</returns>
    private bool IsAnyIdentifyingBoneInView()
    {
        DetectionView.IsSecondaryViewUsed = true;
        foreach (Igi2DetectionItem item in Detection.Enemies)
        {
            Igi2DetectionInfo info = item.Target.DetectionInfo();
            for (int i = 0; i < 3; i++)
            {
                if (DetectionView.IsInside(info.Point(i)) && item.FullVisible[i])
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A visual gunshot heard (<c>HumanAI_SendEventHandler</c>, event 39): a shooter detectors look for is listed as seen at once, all
    /// its points in view, and the event moves to where it shot from; detection then ignores how lit targets are for 150 ticks.
    /// </summary>
    private void OnVisualGunshot(in Igi2AiEvent aiEvent, Igi2AiEventRecord record)
    {
        if (aiEvent.Originator is IIgi2Detectable shooter && World.Detection is { } world && world.Detectables.Contains(shooter))
        {
            bool fresh = Detection.Find(shooter) is null;
            Igi2DetectionItem item = Detection.Track(shooter);
            for (int i = 0; i < 3; i++)
            {
                item.ConeVisible[i] = true;
                item.FullVisible[i] = true;
            }

            item.HasBeenFullVisible = true;
            item.IsCreatedByGunShot = true;
            if (fresh)
            {
                item.TicksToConeUpdate = 52;
                item.TicksToFullUpdate = 52;
            }

            if (shooter is IAiHuman human)
            {
                item.LastVisiblePosition = human.OriginalViewPosition;
                record.Position = item.LastVisiblePosition;
            }
        }

        IgnoreVisibilityFactorTimer = 150;
    }

    /// <summary>
    /// A tick of perception (<c>HumanAI_RunDetection</c>): the detection view is built from the head, turned by the idle sweep and,
    /// while aiming, toward the target; detection runs; each enemy listed is identified over a delay that grows with distance and
    /// gloom (and on easier difficulties), and raises <c>AIEVENT_ENEMYDETECTION</c> (or <c>_NO_LOF</c> when no bone it sees can be
    /// shot through) once confirmed, <c>AIEVENT_SEENSOMETHING</c> before; a dead friend seen raises
    /// <c>AIEVENT_FRIENDLYDETECTION</c>. Then the target position follows the target, or with none the weapon is reloaded under nine
    /// tenths and, unless the squad shares a target, the target is forgotten after ten seconds.
    /// </summary>
    public void RunDetection()
    {
        if (!Soldier.IsDead)
        {
            BuildDetectionView();
            DetectionView.ConeAlpha = View.ConeAlpha;
            DetectionView.ConeGamma = View.ConeGamma;
            DetectionView.ConeLength = View.ConeLength;
            DetectionView.SecondaryConeAlpha = View.SecondaryConeAlpha;
            DetectionView.SecondaryConeGamma = View.SecondaryConeGamma;
            DetectionView.SecondaryConeLength = View.SecondaryConeLength;
            DetectionView.IsSecondaryViewUsed = true;
            if (IgnoreVisibilityFactorTimer <= 0)
            {
                Detection.Flags &= ~Igi2AiDetection.NoVisibilityRange;
            }
            else
            {
                Detection.Flags |= Igi2AiDetection.NoVisibilityRange;
                IgnoreVisibilityFactorTimer--;
            }

            Detection.Run(DetectionView, Soldier, Soldier.Team);
            ReviewEnemies();
            ReviewFriends();
        }

        RefreshTarget();
    }

    private void BuildDetectionView()
    {
        if (Soldier.SeatView is { } seat)
        {
            DetectionView.Position = seat.Position;
            DetectionView.Orientation = [.. seat.Orientation];
            return;
        }

        DetectionView.Position = Soldier.CheapHeadBonePosition();
        float[] o = Soldier.OrientationRows;
        float[] d = Igi2Pose.Rotation(ViewConeRotation.Alpha, 0f, ViewConeRotation.Gamma);
        float[] n = new float[9];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                n[(3 * i) + j] = ((((Fpu)d[j] * o[3 * i]) + ((Fpu)d[6 + j] * o[(3 * i) + 2])) + ((Fpu)d[3 + j] * o[(3 * i) + 1])).F;
            }
        }

        if (Soldier.Channels.Current[30] != 0f || ((uint)Soldier.Signal & 0x1000000u) != 0)
        {
            n = AimAtTarget(n);
        }

        DetectionView.Orientation = n;
    }

    /// <summary>Turns the detection view toward the target as the head sees it: by the target's tilt, then its bearing.</summary>
    private float[] AimAtTarget(float[] n)
    {
        Vector3d target = GetTargetPosition(out _);
        Vector3f head = Soldier.HeadBoneLocalPosition();
        float[] o = Soldier.OrientationRows;
        float dx = (((Fpu)target.X - Soldier.Position.X) - head.X).F;
        Fpu dy = ((Fpu)target.Y - Soldier.Position.Y) - head.Y;
        float dzStored = (((Fpu)target.Z - Soldier.Position.Z) - head.Z).F;
        Fpu dz = dzStored;
        Fpu lx = ((dz * o[6]) + (dy * o[3])) + ((Fpu)dx * o[0]);
        Fpu ly = ((dz * o[7]) + (dy * o[4])) + ((Fpu)dx * o[1]);
        float lz = (((Fpu)dzStored * o[8]) + (dy * o[5]) + ((Fpu)dx * o[2])).F;

        double alpha = Igi2Pose.CalcAlpha(ly.Value, lz);
        double s = Math.Sin(alpha);
        float c = (float)Math.Cos(alpha), ns = (float)-s;
        Fpu sw = s;
        float[] r =
        [
            ((((Fpu)n[2] + n[1]) * 0.0) + n[0]).F,
            ((((Fpu)c * n[1]) + (sw * n[2])) + ((Fpu)n[0] * 0.0)).F,
            ((((Fpu)c * n[2]) + ((Fpu)ns * n[1])) + ((Fpu)n[0] * 0.0)).F,
            ((((Fpu)n[5] + n[4]) * 0.0) + n[3]).F,
            ((((Fpu)c * n[4]) + (sw * n[5])) + ((Fpu)n[3] * 0.0)).F,
            ((((Fpu)c * n[5]) + ((Fpu)ns * n[4])) + ((Fpu)n[3] * 0.0)).F,
            ((((Fpu)n[8] + n[7]) * 0.0) + n[6]).F,
            ((((Fpu)c * n[7]) + (sw * n[8])) + ((Fpu)n[6] * 0.0)).F,
            ((((Fpu)ns * n[7]) + ((Fpu)c * n[8])) + ((Fpu)n[6] * 0.0)).F,
        ];

        double gamma = Igi2Pose.CalcGamma(lx.Value, ly.Value);
        double s2 = Math.Sin(gamma);
        float c2 = (float)Math.Cos(gamma), ns2 = (float)-s2;
        Fpu sw2 = s2;
        return
        [
            (((sw2 * r[1]) + ((Fpu)c2 * r[0])) + ((Fpu)r[2] * 0.0)).F,
            ((((Fpu)c2 * r[1]) + ((Fpu)ns2 * r[0])) + ((Fpu)r[2] * 0.0)).F,
            ((((Fpu)r[0] + r[1]) * 0.0) + r[2]).F,
            (((sw2 * r[4]) + ((Fpu)c2 * r[3])) + ((Fpu)r[5] * 0.0)).F,
            ((((Fpu)c2 * r[4]) + ((Fpu)ns2 * r[3])) + ((Fpu)r[5] * 0.0)).F,
            ((((Fpu)r[3] + r[4]) * 0.0) + r[5]).F,
            ((((Fpu)c2 * r[6]) + (sw2 * r[7])) + ((Fpu)r[8] * 0.0)).F,
            ((((Fpu)ns2 * r[6]) + ((Fpu)c2 * r[7])) + ((Fpu)r[8] * 0.0)).F,
            ((((Fpu)r[6] + r[7]) * 0.0) + r[8]).F,
        ];
    }

    private void ReviewEnemies()
    {
        foreach (Igi2DetectionItem item in Detection.Enemies.ToArray())
        {
            Igi2DetectionInfo info = item.Target.DetectionInfo();
            IAiHuman? human = item.Target as IAiHuman;
            bool onVehicle = Squad?.SquadType == 2;
            if (onVehicle && !item.IsFullVisible)
            {
                item.LastVisiblePosition = info.Point(0);
            }

            float ex = ((Fpu)Soldier.Position.X - item.LastVisiblePosition.X).F;
            float ey = ((Fpu)Soldier.Position.Y - item.LastVisiblePosition.Y).F;
            Fpu ez = (Fpu)Soldier.Position.Z - item.LastVisiblePosition.Z;
            float distance = Fpu.Sqrt((((Fpu)ex * ex) + ((Fpu)ey * ey)) + (ez * ez)).F;
            int delay = 0;
            if (distance > 40960f && ((Flags & 4) == 0 || item.IsCreatedByGunShot))
            {
                delay = IdentificationDelay(distance, item.Target);
            }

            if (distance < 10240f)
            {
                for (int i = 0; i < 3; i++)
                {
                    item.ConeVisible[i] = true;
                    item.FullVisible[i] = true;
                    item.Materials[i] = null;
                }
            }

            int tick = World.LevelTick();
            if (item.HasBeenFullVisible && tick < delay + item.BecameVisibleTick)
            {
                item.IsIdentifying = true;
                Vector3d? seen = item.IsCreatedByGunShot ? item.LastVisiblePosition : FirstFullVisiblePoint(item, info);
                if (seen is { } position)
                {
                    TargetPosition = position;
                    LastTargetPosition = position;
                }
            }

            if (!item.IsFullVisible || human is { IsDead: true })
            {
                continue;
            }

            int kind;
            bool suppressed = false;
            bool confirmed;
            if (distance < 10240f)
            {
                confirmed = tick >= delay + item.BecameVisibleTick;
            }
            else
            {
                bool inView = false;
                for (int i = 0; i < 3 && !inView; i++)
                {
                    inView = DetectionView.IsInside(info.Point(i)) && item.FullVisible[i];
                }

                confirmed = (inView || item.IsCreatedByGunShot) && tick >= delay + item.BecameVisibleTick;
            }

            if (confirmed || onVehicle)
            {
                item.IsIdentifying = false;
                ClearUnshootableBones(item);
                kind = 33;
                if (item.IsFullVisible)
                {
                    kind = 21;
                    IgnoreVisibilityFactorTimer = 150;
                }
            }
            else
            {
                if (item.Target is IAiPlayer { IsKeepingLow: true } && distance > 10240f)
                {
                    suppressed = true;
                }

                kind = 26;
                if (!item.IsCreatedByGunShot)
                {
                    Array.Clear(item.FullVisible);
                }

                if (suppressed)
                {
                    continue;
                }
            }

            if (onVehicle && item.Target is IAiPlayer)
            {
                item.IsKeepInListAfterLostSight = true;
            }

            Receive(new Igi2AiEvent(
                kind, human?.Team ?? 0, 0, 0f, item.LastVisiblePosition, default, item.Target, item.Target, item.Materials.ToArray()));
        }
    }

    /// <summary>
    /// The ticks an enemy takes to identify (the C's local delay): distance over ten metres times the distance factor, in ticks; a
    /// human's gloom adds a second's share; easy adds 30 percent and medium 15, each truncated; never more than two seconds.
    /// </summary>
    private int IdentificationDelay(float distance, IIgi2Detectable target)
    {
        int delay = Fpu.ToInt32((((Fpu)distance * 2.441406286379788e-05) * Tweaks[Igi2AiTweakIndex.DetectionTimeDistanceFactor]) * 30.0);
        if (target is IAiHuman && target.VisibilityFactor is float visible)
        {
            delay += Fpu.ToInt32(((Fpu)1.0 - visible) * 30.0);
            delay = World.Difficulty switch
            {
                0 => Fpu.ToInt32((Fpu)(double)delay * 1.2999999523162842),
                1 => Fpu.ToInt32((Fpu)(double)delay * 1.149999976158142),
                _ => delay,
            };
        }

        return delay >= 60 ? 60 : delay;
    }

    private static Vector3d? FirstFullVisiblePoint(Igi2DetectionItem item, Igi2DetectionInfo info)
    {
        for (int i = 0; i < 3; i++)
        {
            if (item.FullVisible[i])
            {
                return info.Point(i);
            }
        }

        return null;
    }

    /// <summary>Takes out of sight each bone behind a material the mind's own rounds cannot go through: its line of fire.</summary>
    private void ClearUnshootableBones(Igi2DetectionItem item)
    {
        for (int i = 0; i < 3; i++)
        {
            if (item.FullVisible[i] && item.Materials[i] is int material && !CanShootThrough(material))
            {
                item.FullVisible[i] = false;
            }
        }
    }

    private void ReviewFriends()
    {
        foreach (Igi2DetectionItem item in Detection.Friends.ToArray())
        {
            if (item.IsFullVisible && item.Target is IAiHuman { IsDead: true } dead)
            {
                Receive(new Igi2AiEvent(22, dead.Team, 0x100, 0f, dead.Position, default, dead, dead));
            }
        }
    }

    /// <summary>The target position follows a target in sight; with none, the reload under nine tenths, then the shared target or forgetting.</summary>
    private void RefreshTarget()
    {
        if (GetTarget(visibleOnly: true) is { } target)
        {
            if (target is IAiHuman)
            {
                if (Detection.Find(target) is { } item)
                {
                    Vector3d? seen = item.IsCreatedByGunShot ? item.LastVisiblePosition : FirstFullVisiblePoint(item, target.DetectionInfo());
                    if (seen is { } position)
                    {
                        TargetPosition = position;
                    }
                }
            }
            else
            {
                TargetPosition = target.DetectablePosition;
            }

            LastTargetPosition = TargetPosition;
            TargetPosUpdateTimer = 0;
            return;
        }

        Soldier.ReloadIfBelow(0.8999999761581421);
        if (Soldier.Locomotion.CurrentState == 12 || _channels[32] != 0f)
        {
            return;
        }

        if (Squad is { SharedTargets: <= 0 })
        {
            TargetPosUpdateTimer++;
            // Counted up to 301 before it forgets; the timer is not reset.
            if (TargetPosUpdateTimer > 300 && _channels[30] == 0f)
            {
                TargetPosition = default;
                LastTargetPosition = default;
            }
        }
        else if (Squad is { } squad)
        {
            TargetPosition = squad.SharedTargetPosition;
            LastTargetPosition = squad.SharedTargetPosition;
            TargetPosUpdateTimer = 0;
        }
    }
}
