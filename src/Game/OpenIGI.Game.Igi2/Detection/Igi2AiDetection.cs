using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Detection;

/// <summary>One thing a detector is watching, <c>AIDetectionListItem_s</c>.</summary>
public sealed class Igi2DetectionItem
{
    internal Igi2DetectionItem(IAiDetectable target, int tick)
    {
        Target = target;
        DetectionTick = tick;
        BecameVisibleTick = tick;
    }

    /// <summary>Gets what it watches, <c>ptQTaskRef</c>.</summary>
    public IAiDetectable Target { get; }

    /// <summary>Gets the ticks until its cone is checked again, <c>nTicksToConeUpdate</c>.</summary>
    public int TicksToConeUpdate { get; internal set; }

    /// <summary>Gets the ticks until it is looked at again, <c>nTicksToFullUpdate</c>.</summary>
    public int TicksToFullUpdate { get; internal set; }

    /// <summary>Gets the level tick it was first watched on, <c>nDetectionTick</c>.</summary>
    public int DetectionTick { get; }

    /// <summary>Gets the level tick it was first fully seen on, <c>nBecameVisibleTick</c>.</summary>
    public int BecameVisibleTick { get; internal set; }

    /// <summary>Gets which of its three points is looked at next, <c>nCurrentDetectionPosition</c>.</summary>
    public int CurrentPoint { get; internal set; }

    /// <summary>Gets how far its first point is from the view, <c>vDistance</c>.</summary>
    public double Distance { get; internal set; }

    /// <summary>Gets whether each of its points is inside the cone, <c>aisConeVisible</c>.</summary>
    public bool[] ConeVisible { get; } = new bool[3];

    /// <summary>Gets whether each of its points is in plain sight, <c>aisFullVisible</c>.</summary>
    public bool[] FullVisible { get; } = new bool[3];

    /// <summary>Gets the level material each point's sight last crossed, <c>aptQMaterial</c>.</summary>
    public int?[] Materials { get; } = new int?[3];

    /// <summary>Gets a value indicating whether it has ever been in plain sight, <c>isHasBeenFullVisible</c>.</summary>
    public bool HasBeenFullVisible { get; internal set; }

    /// <summary>Gets or sets a value indicating whether a gunshot put it on the list, <c>isCreatedByGunShot</c>.</summary>
    public bool IsCreatedByGunShot { get; set; }

    /// <summary>Gets or sets a value indicating whether it stays listed once out of sight, <c>isKeepInListAfterLostSight</c>.</summary>
    public bool IsKeepInListAfterLostSight { get; set; }

    /// <summary>Gets or sets a value indicating whether its holder is still identifying it, <c>isIdentifying</c>.</summary>
    public bool IsIdentifying { get; set; }

    /// <summary>Gets where it was last in plain sight, <c>tLastVisiblePosition</c>.</summary>
    public Vector3d LastVisiblePosition { get; internal set; }

    /// <summary>Gets a value indicating whether any point is in plain sight, <c>AIDetection_IsFullVisible</c> (<c>igi2 0x474EE0</c>).</summary>
    public bool IsFullVisible => FullVisible[0] || FullVisible[1] || FullVisible[2];

    /// <summary>Gets a value indicating whether any point is inside the cone, <c>AIDetection_IsConeVisible</c> (<c>igi2 0x474F00</c>).</summary>
    public bool IsConeVisible => ConeVisible[0] || ConeVisible[1] || ConeVisible[2];
}

/// <summary>Where a detector looks from, <c>AIDetectionView_s</c>.</summary>
public sealed class Igi2DetectionView
{
    /// <summary>Gets or sets where it looks from, <c>tViewPosition</c>.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its frame, row by row, <c>tViewOrientation</c>.</summary>
    public float[] Orientation { get; set; } = Igi2Pose.Rotation(0f, 0f, 0f);

    /// <summary>Gets or sets its cone's half angle up and down, <c>vViewConeAlpha</c>.</summary>
    public double ConeAlpha { get; set; }

    /// <summary>Gets or sets its cone's half angle to either side, <c>vViewConeGamma</c>.</summary>
    public double ConeGamma { get; set; }

    /// <summary>Gets or sets its cone's length, <c>vViewConeLength</c>.</summary>
    public double ConeLength { get; set; }

    /// <summary>Gets or sets the second cone's half angle up and down, <c>vSecondaryViewConeAlpha</c>.</summary>
    public double SecondaryConeAlpha { get; set; }

    /// <summary>Gets or sets the second cone's half angle to either side, <c>vSecondaryViewConeGamma</c>.</summary>
    public double SecondaryConeGamma { get; set; }

    /// <summary>Gets or sets the second cone's length, <c>vSecondaryViewConeLength</c>.</summary>
    public double SecondaryConeLength { get; set; }

    /// <summary>Gets or sets a value indicating whether the second cone counts, <c>isSecondaryViewUsed</c>.</summary>
    public bool IsSecondaryViewUsed { get; set; }

    /// <summary>
    /// Whether a point is inside the view, <c>AIDetection_IsPositionInsideView</c> (<c>igi2 0x474390</c>): within the cone's
    /// length, and under its half angles up and down and to either side; or, where it counts, the second cone's likewise.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when it is inside.</returns>
    public bool IsInside(Vector3d point)
    {
        float[] m = Orientation;
        Fpu dx = (Fpu)point.X - Position.X, dy = (Fpu)point.Y - Position.Y, dz = (Fpu)point.Z - Position.Z;
        Fpu x = (((Fpu)m[6] * dz) + ((Fpu)m[3] * dy)) + ((Fpu)m[0] * dx);
        Fpu y = (((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx);
        Fpu z = (((Fpu)m[8] * dz) + ((Fpu)m[5] * dy)) + ((Fpu)m[2] * dx);
        double length = Fpu.Sqrt(((y * y) + (z * z)) + (x * x)).Value;
        float alpha = Math.Abs(Igi2Pose.CalcAlpha(y.Value, z.Value));
        float gamma = Math.Abs(Igi2Pose.CalcGamma(x.Value, y.Value));

        // The lengths pass edges included and anything not a number; the angles only strictly inside.
        if (!(length > ConeLength) && alpha < ConeAlpha && gamma < ConeGamma)
        {
            return true;
        }

        return IsSecondaryViewUsed && !(length > SecondaryConeLength) && alpha < SecondaryConeAlpha && gamma < SecondaryConeGamma;
    }
}

/// <summary>
/// The things detectors may see, <c>AIDetection_ptDetectionQTaskList</c>, and how they are seen: the world sight is traced
/// through, and the clocks.
/// </summary>
/// <param name="sight">The world lines of sight are traced through.</param>
/// <param name="materials">The session's materials, whose transparency sight passes.</param>
public sealed class Igi2DetectionWorld(IIgi2SightQuery? sight, Igi2GameMaterials materials)
{
    /// <summary>The query groups a detection's sight is traced against: <c>Forest_bQTaskGroup | 0x98000000</c>, ground included.</summary>
    public const uint SightGroups = 0x98000000 | Igi2CollisionWorld.ForestGroup;

    /// <summary>The transparency at which sight still counts as clear, <c>0.5</c>.</summary>
    public const float ClearEnough = 0.5f;

    private int _nextDisplace;

    /// <summary>Gets the detectables, in the order they were listed.</summary>
    public List<IAiDetectable> Detectables { get; } = [];

    /// <summary>Lists a detectable at the end, unless it is listed already (<c>QTaskList_IsQTaskRegistered</c>, <c>QTaskList_Register</c>).</summary>
    /// <param name="detectable">The detectable.</param>
    public void Register(IAiDetectable detectable)
    {
        if (!Detectables.Contains(detectable))
        {
            Detectables.Add(detectable);
        }
    }

    /// <summary>
    /// Takes a detectable off the list, the last one moved into its place (<c>QTaskList_Remove</c>, <c>tasklist.c</c>), which
    /// changes the order the detectors look over the rest in.
    /// </summary>
    /// <param name="detectable">The detectable.</param>
    public void Remove(IAiDetectable detectable)
    {
        int index = Detectables.IndexOf(detectable);
        if (index < 0)
        {
            return;
        }

        int last = Detectables.Count - 1;
        Detectables[index] = Detectables[last];
        Detectables.RemoveAt(last);
    }

    /// <summary>Gets or sets the game's tick count, <c>Flow_ptFlow->nTicks</c>, which spreads detectors over four ticks.</summary>
    public Func<int> FlowTicks { get; set; } = static () => 0;

    /// <summary>Gets or sets the level's tick, <c>LevelFlow nTick</c>, which stamps what is seen.</summary>
    public Func<int> LevelTick { get; set; } = static () => 0;

    /// <summary>Gets or sets which objects a line of sight may meet: <c>AIDetection_eLineOfSightQTaskEvent</c> as each type answers it.</summary>
    /// <remarks>Glass never stops it (<c>Glass_AIDetectionLineOfSightHandler</c>, <c>igi2 0x4DBE30</c>); every other placed type does.</remarks>
    public Func<object, bool> SightIncludes { get; set; } =
        static owner => owner is not Igi2CollisionOwner { GlassKey: not null };

    /// <summary>Takes the next of the four ticks a new detector runs on, <c>AIDetection_nDisplaceTick</c>.</summary>
    /// <returns>Its tick, nought to three.</returns>
    internal int TakeDisplaceTick()
    {
        int tick = _nextDisplace;
        _nextDisplace = (_nextDisplace + 1) % 4;
        return tick;
    }

    /// <summary>Gets the session's materials, which lines of sight are traced against.</summary>
    public Igi2GameMaterials Materials => materials;

    /// <summary>
    /// Traces a line of sight through some groups, <c>World_LineOfSight</c> with <c>AIDetection_eLineOfSightQTaskEvent</c>.
    /// </summary>
    /// <param name="from">Where it starts.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="groups">The query groups.</param>
    /// <param name="transparency">Receives how much the faces met let through.</param>
    /// <param name="passes">The one human the line passes through, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when it met nothing.</returns>
    public bool LineOfSight(Vector3d from, Vector3d to, uint groups, out float transparency, object? passes = null)
    {
        transparency = 1f;
        if (sight is null)
        {
            return true;
        }

        WorldLineOfSight line = new(from, to, materials) { Includes = Through(passes, passes) };
        bool clear = sight.LineOfSight(line, groups);
        transparency = line.Transparency;
        return clear;
    }

    /// <summary>Traces a line of sight through some groups that passes through whatever a test names.</summary>
    /// <param name="from">Where it starts.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="groups">The query groups.</param>
    /// <param name="passes">Whether the line passes through an owner it meets.</param>
    /// <param name="transparency">Receives how much the faces met let through.</param>
    /// <returns><see langword="true"/> when it met nothing.</returns>
    public bool LineOfSight(Vector3d from, Vector3d to, uint groups, Func<object, bool> passes, out float transparency)
    {
        ArgumentNullException.ThrowIfNull(passes);
        transparency = 1f;
        if (sight is null)
        {
            return true;
        }

        Func<object, bool> includes = SightIncludes;
        WorldLineOfSight line = new(from, to, materials) { Includes = owner => !passes(owner) && includes(owner) };
        bool clear = sight.LineOfSight(line, groups);
        transparency = line.Transparency;
        return clear;
    }

    /// <summary>
    /// Whether a point is in sight of the view, <c>AIDetection_CheckTargetVisibility</c> (<c>igi2 0x4744E0</c>): a line from the
    /// view meets nothing, or lets at least half through.
    /// </summary>
    /// <param name="from">The view.</param>
    /// <param name="to">The point.</param>
    /// <param name="viewer">What looks, which the line passes through.</param>
    /// <param name="target">What is looked at, which the line passes through.</param>
    /// <param name="material">The material crossed, when it is crossed and seen through.</param>
    /// <returns><see langword="true"/> when the point is seen.</returns>
    internal bool CanSee(Vector3d from, Vector3d to, object? viewer, object? target, out int? material)
    {
        material = null;
        if (sight is null)
        {
            return true;
        }

        WorldLineOfSight line = new(from, to, materials) { Includes = Through(viewer, target) };
        if (sight.LineOfSight(line, SightGroups))
        {
            return true;
        }

        if (line.Transparency < ClearEnough)
        {
            return false;
        }

        material = line.Material;
        return true;
    }

    /// <summary>
    /// What a line may meet: as <see cref="SightIncludes"/> says, but never the viewer or the target, as a human answers the line
    /// (<c>Human_AIDetectionLineOfSightHandler</c>: every other human stops it).
    /// </summary>
    private Func<object, bool> Through(object? viewer, object? target)
    {
        Func<object, bool> includes = SightIncludes;
        return viewer is null && target is null
            ? includes
            : owner => !ReferenceEquals(owner, viewer) && !ReferenceEquals(owner, target) && includes(owner);
    }
}

/// <summary>
/// What one detector watches and how, <c>AIDetection_s</c>: the friends and enemies it has listed, and the intervals it looks at
/// them at.
/// </summary>
public sealed class Igi2AiDetection
{
    /// <summary>The most things one detector lists, <c>192</c>.</summary>
    public const int Capacity = 192;

    /// <summary>The flag that spares a human the visibility-factor test, bit one of <c>bFlags</c>.</summary>
    public const int NoVisibilityRange = 1;

    /// <summary>The flag that looks at every listed thing every tick, bit two of <c>bFlags</c>.</summary>
    public const int Continuous = 2;

    /// <summary>Lists live friends, bit one of <c>bTeamCheckMask</c>.</summary>
    public const int LiveFriends = 1;

    /// <summary>Lists dead friends, bit two.</summary>
    public const int DeadFriends = 2;

    /// <summary>Lists live enemies, bit three.</summary>
    public const int LiveEnemies = 4;

    /// <summary>Lists dead enemies, bit four.</summary>
    public const int DeadEnemies = 8;

    /// <summary>
    /// Starts a detector, <c>AIDetection_InitDetection</c> (<c>igi2 0x4748C0</c>): its side, what it looks for, and the next of the
    /// four ticks; its intervals are a quarter of those asked for, as it runs every fourth tick.
    /// </summary>
    /// <param name="world">The detectables.</param>
    /// <param name="team">Its side.</param>
    /// <param name="teamMask">Which of friends and enemies, live and dead, it lists.</param>
    /// <param name="kinds">Which kinds it looks for.</param>
    /// <param name="coneInterval">The ticks between cone checks.</param>
    /// <param name="visualInterval">The ticks between looks.</param>
    public Igi2AiDetection(Igi2DetectionWorld world, int team, int teamMask, AiDetectableKind kinds, int coneInterval, int visualInterval)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Team = team;
        TeamMask = teamMask;
        Kinds = kinds;
        DisplaceTick = world.TakeDisplaceTick();
        ConeInterval = coneInterval / 4;
        VisualInterval = visualInterval / 4;
    }

    /// <summary>Gets the world it looks in.</summary>
    public Igi2DetectionWorld World { get; }

    /// <summary>Gets or sets its flags, <c>bFlags</c>.</summary>
    public int Flags { get; set; }

    /// <summary>Gets its side, <c>eTeam</c>.</summary>
    public int Team { get; }

    /// <summary>Gets the ticks between cone checks, <c>nConeUpdateInterval</c>.</summary>
    public int ConeInterval { get; }

    /// <summary>Gets the ticks between looks, <c>nVisualUpdateInterval</c>.</summary>
    public int VisualInterval { get; }

    /// <summary>Gets which of the four ticks it runs on, <c>nDisplaceTick</c>.</summary>
    public int DisplaceTick { get; }

    /// <summary>Gets which friends and enemies it lists, <c>bTeamCheckMask</c>.</summary>
    public int TeamMask { get; }

    /// <summary>Gets which kinds it looks for, <c>bQTaskCheckMask</c>.</summary>
    public AiDetectableKind Kinds { get; }

    /// <summary>Gets the enemies it is watching, <c>tEnemyList</c>, oldest first.</summary>
    public List<Igi2DetectionItem> Enemies { get; } = [];

    /// <summary>Gets the friends it is watching, <c>tFriendList</c>, oldest first.</summary>
    public List<Igi2DetectionItem> Friends { get; } = [];

    /// <summary>
    /// Looks, <c>AIDetection_RunDetection</c> (<c>igi2 0x474B60</c>): what is gone is let go; then, on its tick of four, every
    /// detectable but its source that is near enough, or already an enemy listed, and of a kind, side and life it lists, is listed
    /// and looked at; anything else listed is unseen and, unless kept, let go.
    /// </summary>
    /// <param name="view">Where it looks from.</param>
    /// <param name="source">The detectable it looks for, which it never sees.</param>
    /// <param name="sourceTeam">The source's side.</param>
    public void Run(Igi2DetectionView view, IAiDetectable? source, int sourceTeam)
    {
        ArgumentNullException.ThrowIfNull(view);
        RemoveGone();
        if (World.FlowTicks() % 4 != DisplaceTick)
        {
            return;
        }

        foreach (IAiDetectable target in World.Detectables)
        {
            if (ReferenceEquals(target, source))
            {
                continue;
            }

            if (!(Igi2AiMath.DistanceInRegister(target.DetectablePosition, view.Position).Value + target.DetectableRadius <= view.ConeLength)
                && (!view.IsSecondaryViewUsed
                    || !(Igi2AiMath.DistanceInRegister(target.DetectablePosition, view.Position).Value + target.DetectableRadius <= view.SecondaryConeLength))
                && FindItem(Enemies, target) is null)
            {
                continue;
            }

            AiDetectionInfo info = target.DetectionInfo();
            List<Igi2DetectionItem>? list = ListFor(info, sourceTeam);
            if (list is not null && (Flags & NoVisibilityRange) == 0 && target.VisibilityFactor is { } factor)
            {
                double distance = Igi2AiMath.DistanceInRegister(target.DetectablePosition, view.Position).Value;
                if (distance > ((Fpu)factor * view.ConeLength).Value && distance > ((Fpu)factor * view.SecondaryConeLength).Value)
                {
                    list = null;
                }
            }

            if (list is null)
            {
                Forget(target);
                continue;
            }

            Check(Item(list, target), info, view, source);
        }
    }

    /// <summary>The listed item of a target, an enemy first, <c>AIDetection_GetListItemFromQTask</c> (<c>igi2 0x474610</c>).</summary>
    /// <param name="target">The target.</param>
    /// <returns>Its item, or <see langword="null"/> when it is not listed.</returns>
    public Igi2DetectionItem? Find(IAiDetectable target) =>
        FindItem(Enemies, target) ?? FindItem(Friends, target);

    /// <summary>Lists a target among the enemies, or finds it there, <c>AIDetection_NewListItem</c>.</summary>
    /// <param name="target">The target.</param>
    /// <returns>Its item.</returns>
    public Igi2DetectionItem Track(IAiDetectable target) => Find(target) ?? Item(Enemies, target);

    /// <summary>Lets go of everything, <c>AIDetection_ClearDetection</c> (<c>igi2 0x474DF0</c>).</summary>
    public void Clear()
    {
        Enemies.Clear();
        Friends.Clear();
    }

    private List<Igi2DetectionItem>? ListFor(AiDetectionInfo info, int sourceTeam)
    {
        if (!info.IsActive || (info.Kind & Kinds) == 0)
        {
            return null;
        }

        if (info.Team == sourceTeam)
        {
            return (TeamMask & (info.IsAlive ? LiveFriends : DeadFriends)) != 0 ? Friends : null;
        }

        return (TeamMask & (info.IsAlive ? LiveEnemies : DeadEnemies)) != 0 ? Enemies : null;
    }

    // AIDetection_NewListItem (igi2 0x474660): the listed item of a target, or a new one at the list's end.
    private Igi2DetectionItem Item(List<Igi2DetectionItem> list, IAiDetectable target)
    {
        if (FindItem(list, target) is { } listed)
        {
            return listed;
        }

        if (Enemies.Count + Friends.Count >= Capacity)
        {
            throw new InvalidOperationException("Ran out of listitems in AIDetection");
        }

        Igi2DetectionItem item = new(target, World.LevelTick());
        list.Add(item);
        return item;
    }

    // The target is not to be seen: unseen if listed, and let go unless kept (AIDetection_DeleteListItemFromQTask, 0x474580).
    private void Forget(IAiDetectable target)
    {
        if (Find(target) is { } item)
        {
            Array.Clear(item.ConeVisible);
            Array.Clear(item.FullVisible);
            if (item.IsKeepInListAfterLostSight)
            {
                return;
            }
        }

        RemoveTarget(Enemies, target);
        RemoveTarget(Friends, target);
    }

    // aidetection.c AIDetection_GetListItemFromQTask: direct searches avoid per-target capturing predicates.
    private static Igi2DetectionItem? FindItem(List<Igi2DetectionItem> list, IAiDetectable target)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i].Target, target)) { return list[i]; }
        }
        return null;
    }

    private static void RemoveTarget(List<Igi2DetectionItem> list, IAiDetectable target)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(list[i].Target, target)) { list.RemoveAt(i); }
        }
    }

    // AIDetection_RemoveDeadItems (igi2 0x474AB0): what no longer exists is let go.
    private void RemoveGone()
    {
        Enemies.RemoveAll(static item => item.Target.IsGone);
        Friends.RemoveAll(static item => item.Target.IsGone);
    }

    // AIDetection_RunDetectionCheck (igi2 0x474950): the cone checked at its interval, and while any point is inside it, one
    // point looked at in turn at its interval; either every tick when continuous.
    private void Check(Igi2DetectionItem item, AiDetectionInfo info, Igi2DetectionView view, IAiDetectable? source)
    {
        bool wasSeen = item.HasBeenFullVisible;
        bool continuous = (Flags & Continuous) != 0;
        if (item.TicksToConeUpdate == 0 || continuous)
        {
            for (int i = 0; i < 3; i++)
            {
                item.ConeVisible[i] = view.IsInside(info.Point(i));
                if (!item.ConeVisible[i])
                {
                    item.FullVisible[i] = false;
                }
            }

            if (item.TicksToConeUpdate == 0)
            {
                item.TicksToConeUpdate = ConeInterval + 1;
            }
        }

        if (item.IsConeVisible)
        {
            if (item.TicksToFullUpdate == 0 || continuous)
            {
                int point = item.CurrentPoint;
                Vector3d at = info.Point(point);
                bool seen = World.CanSee(view.Position, at, source, item.Target, out int? material);
                if (seen)
                {
                    item.Materials[point] = material;
                }

                item.FullVisible[point] = seen;
                item.HasBeenFullVisible |= seen;
                if (seen)
                {
                    item.IsCreatedByGunShot = false;
                    if (!wasSeen)
                    {
                        item.BecameVisibleTick = World.LevelTick();
                    }

                    item.LastVisiblePosition = at;
                }

                item.CurrentPoint = (point + 1) % 3;
                if (item.TicksToFullUpdate == 0)
                {
                    item.TicksToFullUpdate = VisualInterval + 1;
                }
            }

            item.TicksToFullUpdate--;
        }

        item.TicksToConeUpdate--;
        Vector3d head = info.Head;
        Fpu ex = (Fpu)view.Position.X - head.X, ey = (Fpu)view.Position.Y - head.Y, ez = (Fpu)view.Position.Z - head.Z;
        item.Distance = Fpu.Sqrt(((ez * ez) + (ey * ey)) + (ex * ex)).Value;
    }
}
