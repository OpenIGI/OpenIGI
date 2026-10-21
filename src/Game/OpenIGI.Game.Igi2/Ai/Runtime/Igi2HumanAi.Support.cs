using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>Where a mind's cover search stands, <c>HumanAICoverNodeSearchState_e</c>.</summary>
public enum Igi2AiCoverSearchState
{
    /// <summary>Not searching.</summary>
    Idle = 0,

    /// <summary>Checking the graph's cover, a couple of items a tick.</summary>
    FindNode = 1,

    /// <summary>A cover node is found and held.</summary>
    NodeFound = 2,
}

/// <summary>A mind's cover search, <c>HumanAICoverNodeSearchData_t</c>.</summary>
public sealed class Igi2AiCoverSearch
{
    /// <summary>Gets or sets the state.</summary>
    public Igi2AiCoverSearchState State { get; set; }

    /// <summary>Gets or sets the cover node found, or -1.</summary>
    public int CoverNode { get; set; } = -1;

    /// <summary>Gets or sets the next cover item to check.</summary>
    public int NodeToCheck { get; set; }

    /// <summary>Gets or sets how far the cover found is.</summary>
    public float Distance { get; set; }

    /// <summary>Gets the cover found's sheltered directions.</summary>
    public int[] Circle { get; } = new int[32];
}

/// <content>The helpers the actions and squads ask of a mind.</content>
public sealed partial class Igi2HumanAi
{
    /// <summary>Gets its cover search, <c>tCoverNodeSearchData</c>.</summary>
    public Igi2AiCoverSearch CoverSearch { get; } = new();

    /// <summary>Gets its patrols, <c>atPatrolPathStackItems</c>.</summary>
    public Igi2PatrolList Patrols { get; } = new();

    /// <summary>Gets or sets what it marked in use, <c>pxActivationEntry1</c>.</summary>
    public object? ActivationEntry { get; set; }

    /// <summary>Gets or sets the alarm control it marked in use, <c>pxActivationEntry2</c>.</summary>
    public object? ControlActivationEntry { get; set; }

    /// <summary>
    /// Gets a value fixed for the soldier that the original takes from the parity of its body's heap address's digit sum (the
    /// combat idle's first stance, the voice): here the digit sum of its task id.
    /// </summary>
    public int SoldierParity => DigitSum(Task.Id) % 2;

    /// <summary>Gets the voice its lines are said in, one or two, from <see cref="SoldierParity"/>.</summary>
    public int VoiceIndex => SoldierParity + 1;

    /// <summary>Gets the type of the weapon it holds, or <see langword="null"/>.</summary>
    public Igi2WeaponTypeTask? ActiveWeaponType => Soldier.Weapon.Inventory?.Current?.WeaponType;

    /// <summary>Gets a value indicating whether it is set to fire this tick, as the squad's fire order sees it.</summary>
    public bool IsReadyToFire =>
        Actions.Top is { } top && top.ChildRun == (Igi2AiChildCallback)Igi2AiContent.FireAtTargetPosition && FireDelayTimer == 0 && IsAllowedToFire;

    private static int DigitSum(int value)
    {
        int sum = 0;
        for (int v = value; v > 0; v /= 10)
        {
            sum += v % 10;
        }

        return sum;
    }

    /// <summary>Whether its soldier is in a content state, <c>HumanAIContent_IsState</c>.</summary>
    /// <param name="contentState">The content state.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsState(int contentState) => Soldier.Locomotion.CurrentState == Igi2AiContent.SoldierState(contentState);

    /// <summary>Writes a content channel, <c>HumanAIContent_SetChannel</c>.</summary>
    /// <param name="contentChannel">The content channel.</param>
    /// <param name="value">The value.</param>
    public void SetChannel(int contentChannel, float value) => _channels[Igi2AiContent.HumanChannel(contentChannel)] = value;

    /// <summary>Whether its top action is one, recognised by its Init.</summary>
    /// <param name="init">The action's Init.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsTop(Igi2AiActionCallback init) => Actions.Top is { } top && top.Init == init;

    /// <summary>Acts on a squad's order, <c>HumanAIContent_ReceiveSquadOrder</c>.</summary>
    /// <param name="order">The order.</param>
    public void ReceiveSquadOrder(Igi2AiSquadOrder order) => Igi2AiContent.ReceiveSquadOrder(this, order);

    /// <summary>The enemies it lists that it sees and that live, <c>HumanAI_GetNumberOfVisibleTargets</c>.</summary>
    /// <returns>The count.</returns>
    public int VisibleTargetCount()
    {
        int count = 0;
        foreach (Igi2DetectionItem item in Detection.Enemies)
        {
            if (item.IsFullVisible && item.Target is not IAiHuman { IsDead: true })
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The nearest enemy it lists that lives, by the soldier's position.</summary>
    /// <returns>The enemy, or <see langword="null"/>.</returns>
    public IAiDetectable? NearestLiveEnemyListed()
    {
        Vector3d at = Soldier.Position;
        IAiDetectable? best = null;
        float bestDistance = 0f;
        foreach (Igi2DetectionItem item in Detection.Enemies)
        {
            if (item.Target is IAiHuman { IsDead: true })
            {
                continue;
            }

            Vector3d p = item.Target.DetectablePosition;
            double dx = at.X - p.X, dy = at.Y - p.Y, dz = at.Z - p.Z;
            double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (best is null || distance < bestDistance)
            {
                bestDistance = (float)distance;
                best = item.Target;
            }
        }

        return best;
    }

    /// <summary>Whether any enemy listed is being identified, <c>HumanAI_IsIdentifyingTarget</c>.</summary>
    /// <returns><see langword="true"/> when one is.</returns>
    public bool IsIdentifyingTarget() => Detection.Enemies.Exists(static e => e.IsIdentifying);

    /// <summary>Whether it stands on the cover node it found, <c>HumanAI_IsOnCoverNode</c>.</summary>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool IsOnCoverNode() => CoverSearch.State == Igi2AiCoverSearchState.NodeFound && CoverSearch.CoverNode == CurrentNode;

    /// <summary>Whether it heads for the cover node it found, <c>HumanAI_IsOnTheWayToCoverNode</c>.</summary>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool IsOnTheWayToCoverNode() => CoverSearch.State == Igi2AiCoverSearchState.NodeFound && CoverSearch.CoverNode == DestinationNode;

    /// <summary>Whether its weapon is a shotgun, <c>HumanAI_IsShotGunSholdier</c>.</summary>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsShotgunSoldier() => ActiveWeaponType is { WeaponType: Igi2WeaponConstants.WeaponTypeShotgun };

    /// <summary>Whether its weapon fires missiles, <c>HumanAI_IsRPGSoldier</c>.</summary>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool IsRpgSoldier() => Soldier.Weapon.ActiveIsMissileLauncher;

    /// <summary>Whether its weapon is a scoped primary, which a leapfrog keeps covering.</summary>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool HasSniperScope() => ActiveWeaponType is { WeaponCategory: 3, SightDisplayType: 8 };

    /// <summary>The rounds left in its weapon's first fire type.</summary>
    /// <returns>The rounds.</returns>
    public int AmmoInWeapon() => Soldier.Weapon.Inventory?.Current?.Loaded(0) ?? 0;

    /// <summary>The rounds its weapon's first fire type holds.</summary>
    /// <returns>The rounds.</returns>
    public int RoundsPerClip() =>
        Soldier.Weapon.Inventory is { Current: { } item } inventory ? item.WeaponType.AmmoInfo(0, inventory.Tables).RoundsPerClip : 0;

    /// <summary>Whether less than a share of a clip is left, <c>HumanAI_IsLessThanPercentageAmmoLeftInWeapon</c>.</summary>
    /// <param name="percentage">The share, in percent.</param>
    /// <returns><see langword="true"/> when less is left.</returns>
    public bool IsLessThanPercentageAmmoLeftInWeapon(float percentage) =>
        AmmoInWeapon() < Fpu.ToInt32(((Fpu)(double)RoundsPerClip() * percentage) * 0.009999999776482582);

    /// <summary>The ticks the detection delay takes to a point, <c>HumanAI_GetDelayFromDetectionTimeDistanceFactor</c>.</summary>
    /// <param name="position">The point.</param>
    /// <returns>The ticks.</returns>
    public int DelayFromDetectionTimeDistanceFactor(Vector3d position)
    {
        float distance = Igi2AiMath.Distance(Soldier.Position, position);
        return Fpu.ToInt32((((Fpu)distance * 2.441406286379788e-05) * Tweaks[Igi2AiTweakIndex.DetectionTimeDistanceFactor]) * 30.0);
    }

    /// <summary>
    /// Walks the graph out from a node, breadth first, at most a number of nodes added, until a test holds
    /// (<c>HumanAI_RecurseNodesFromNode</c>); a link to a node past the 2048th ends the walk.
    /// </summary>
    /// <param name="start">The node.</param>
    /// <param name="maxChecks">The most nodes added.</param>
    /// <param name="test">The test.</param>
    /// <returns><see langword="true"/> when the test held.</returns>
    public bool RecurseNodesFromNode(Igi2AiGraphNode start, int maxChecks, Func<Igi2AiGraphNode, bool> test)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(test);
        if (Graph is not { } graph)
        {
            return false;
        }

        bool[] scanned = new bool[2048];
        List<int> queue = [start.Id];
        scanned[start.Id] = true;
        int searches = 0;
        for (int at = 0; at < queue.Count; at++)
        {
            Igi2AiGraphNode? node = graph.NodeAt(queue[at]);
            if (node is null)
            {
                return false;
            }

            if (test(node))
            {
                return true;
            }

            foreach (Igi2AiGraphLink link in node.Links)
            {
                if (searches >= maxChecks)
                {
                    break;
                }

                int other = link.Other(queue[at]);
                if (other >= 2048)
                {
                    return false;
                }

                if (!scanned[other])
                {
                    queue.Add(other);
                    scanned[other] = true;
                    searches++;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Searches the graph's cover a couple of items a tick (<c>HumanAI_HandleCoverSearch</c>): from a danger at a point, a free cover
    /// within five metres and two up or down that shelters from it; from its target, one within twenty metres, nearer the target than
    /// the soldier is, at least five metres from it, that shelters from the target's head. The cover found is held.
    /// </summary>
    /// <param name="danger">The danger's position, or <see langword="null"/> to take cover from the target.</param>
    /// <returns><see langword="true"/> once a cover is found.</returns>
    public bool HandleCoverSearch(Vector3d? danger)
    {
        IAiDetectable? target = GetTarget(visibleOnly: true);
        Igi2AiCoverSearch search = CoverSearch;
        if (search.State == Igi2AiCoverSearchState.Idle)
        {
            search.CoverNode = -1;
            search.NodeToCheck = 0;
            search.State = Igi2AiCoverSearchState.FindNode;
            search.Distance = 0f;
            return false;
        }

        if (search.State != Igi2AiCoverSearchState.FindNode || Graph is not { } graph)
        {
            return true;
        }

        for (int count = 0; ; count++)
        {
            Igi2AiCoverItem? cover = search.NodeToCheck < graph.Cover.Count ? graph.Cover[search.NodeToCheck] : null;
            search.NodeToCheck++;
            if (cover is null)
            {
                search.State = Igi2AiCoverSearchState.Idle;
                return false;
            }

            if ((cover.Flags & 1) != 0)
            {
                return false;
            }

            if (graph.NodeAt(cover.NodeId) is not { } node)
            {
                search.State = Igi2AiCoverSearchState.Idle;
                return false;
            }

            Vector3d p = node.Position, at = Soldier.Position;
            float cx = (float)(p.X - at.X), cy = (float)(p.Y - at.Y);
            double cz = p.Z - at.Z;
            float toCover = (float)Math.Sqrt((cx * (double)cx) + (cy * (double)cy) + (cz * cz));
            if (danger is { } from)
            {
                if (toCover < 20480f && Math.Abs(cz) < 8192.0 && graph.ProvidesCover(cover, from))
                {
                    return Take(search, cover, toCover);
                }
            }
            else if (target is not null && toCover < 81920f && Math.Abs(cz) < 8192.0)
            {
                AiDetectionInfo info = target.DetectionInfo();
                Vector3d t = target.DetectablePosition;
                float tx = (float)(t.X - at.X), ty = (float)(t.Y - at.Y);
                float toTarget = (float)Math.Sqrt((tx * (double)tx) + (ty * (double)ty) + ((t.Z - at.Z) * (t.Z - at.Z)));
                float nx = (float)(t.X - p.X), ny = (float)(t.Y - p.Y);
                double coverToTarget = Math.Sqrt((nx * (double)nx) + (ny * (double)ny) + ((t.Z - p.Z) * (t.Z - p.Z)));
                if (Math.Abs(t.Z - p.Z) < 8192.0 && coverToTarget < toTarget && toCover < toTarget && toTarget > 20480f
                    && graph.ProvidesCover(cover, info.Head))
                {
                    return Take(search, cover, toCover);
                }
            }

            if (search.NodeToCheck >= graph.Cover.Count)
            {
                search.State = Igi2AiCoverSearchState.Idle;
            }

            if (count + 1 >= 2)
            {
                return false;
            }
        }
    }

    private static bool Take(Igi2AiCoverSearch search, Igi2AiCoverItem cover, float distance)
    {
        Array.Copy(cover.CoverCircle, search.Circle, 32);
        search.Distance = distance;
        search.CoverNode = cover.NodeId;
        search.State = Igi2AiCoverSearchState.NodeFound;
        cover.Flags |= 1;
        return true;
    }

    /// <summary>
    /// Steps aside from a friend standing on the same spot (<c>HumanAI_HandleDispersal</c>): a team member that is not its leader,
    /// within a metre of another soldier who stands still, picks another node near it.
    /// </summary>
    /// <returns><see langword="true"/> when it moves.</returns>
    public bool HandleDispersal()
    {
        int current = CurrentNode;
        if (current != DestinationNode || Squad is not { } squad || squad.IsTeamLeader(this))
        {
            return false;
        }

        DestinationNode = current;
        foreach (Igi2Human human in World.Humans)
        {
            if (ReferenceEquals(human, Soldier) || human is not Igi2HumanSoldier { Mind: Igi2HumanAi other } soldier || soldier.IsDead)
            {
                continue;
            }

            if (other.CurrentNode != other.DestinationNode)
            {
                continue;
            }

            Vector3d a = Soldier.Position, b = soldier.Position;
            float dx = (float)(a.X - b.X), dy = (float)(a.Y - b.Y);
            double dz = a.Z - b.Z;
            if (Math.Sqrt((dx * (double)dx) + (dy * (double)dy) + (dz * dz)) >= 4096.0)
            {
                continue;
            }

            DestinationNode = -1;
            FindAlternativeNode();
            if (DestinationNode != -1)
            {
                return true;
            }

            DestinationNode = CurrentNode;
            return false;
        }

        return false;
    }

    /// <summary>
    /// Picks a node near its own to step to (<c>HumanAI_FindAlternativeNode</c>): out from its node, each other node is taken on a
    /// coin toss, when no soldier stands on or makes for it, and when (against a human target) it is no nearer the target than a metre
    /// and not nearer than its own node is.
    /// </summary>
    private void FindAlternativeNode()
    {
        if (Graph?.NodeAt(CurrentNode) is not { } start)
        {
            return;
        }

        int found = -1;
        RecurseNodesFromNode(start, 100, node =>
        {
            if (node.Id == CurrentNode || World.Random.NextRaw() % 100u < 50u)
            {
                return false;
            }

            IAiDetectable? enemy = GetTarget(visibleOnly: true) ?? NearestLiveEnemyListed();
            if (enemy is IAiHuman human)
            {
                Vector3d e = human.Position, n = node.Position;
                float ex = (float)(e.X - n.X), ey = (float)(e.Y - n.Y);
                double toNode = Math.Sqrt((ex * (double)ex) + (ey * (double)ey) + ((e.Z - n.Z) * (e.Z - n.Z)));
                if (toNode < 4096.0)
                {
                    return false;
                }

                Vector3d here = Graph.NodeAt(CurrentNode)?.Position ?? default;
                float hx = (float)(e.X - here.X), hy = (float)(e.Y - here.Y);
                if (Math.Sqrt((hx * (double)hx) + (hy * (double)hy) + ((e.Z - here.Z) * (e.Z - here.Z))) < (float)toNode)
                {
                    return false;
                }
            }

            foreach (Igi2Human body in World.Humans)
            {
                if (!ReferenceEquals(body, Soldier) && !body.IsDead && body is Igi2HumanSoldier { Mind: Igi2HumanAi other }
                    && (other.DestinationNode == node.Id || other.CurrentNode == node.Id))
                {
                    return false;
                }
            }

            found = node.Id;
            return true;
        });

        if (found != -1)
        {
            DestinationNode = found;
        }
    }

    /// <summary>Tells every other soldier going to use a task that it is used, <c>HumanAI_NotifyTaskHasBeenActivated</c>.</summary>
    /// <param name="taskId">The task.</param>
    public void NotifyTaskHasBeenActivated(int taskId)
    {
        foreach (Igi2Human human in World.Humans)
        {
            if (human is Igi2HumanSoldier { Mind: Igi2HumanAi other } && !ReferenceEquals(other, this)
                && other.Actions.Top is { } top && top.Init == (Igi2AiActionCallback)Igi2AiContent.ActivateInit
                && top.Context is Igi2AiContent.ActivateContext { } context && context.TaskId == taskId)
            {
                other.Actions.Pop(other, top);
            }
        }
    }

    /// <summary>
    /// Whether its rounds go through a material seen in the way (the line-of-fire test of <c>HumanAI_RunDetection</c>): a gun whose
    /// round is a bullet does when its penetration is at least the material's; a rocket or anything else a gun fires does not;
    /// what is no gun leaves sight alone.
    /// </summary>
    /// <param name="material">The material.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool CanShootThrough(int material)
    {
        if (ActiveWeaponType is not { } weaponType || weaponType.WeaponType > Igi2WeaponConstants.WeaponTypeShotgun)
        {
            return true;
        }

        if (weaponType.FireType(0) is not Igi2GunTask gun || gun.ProjectileTaskType != World.FireTaskTypes.GunShot
            || World.Detection is not { } world)
        {
            return false;
        }

        return !(Igi2GunShotParameters.Of(gun).PenetrationPower < (world.Materials[material]?.PenetrationPower ?? 0f));
    }
}
