using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons;

/// <summary>Where the two titles' mines part ways; everything else about the fuse is the same task.</summary>
/// <param name="ArmsWhenClear">
/// IGI 2's run (<c>Mine_RunHandler</c>, 0x487D30) arms only on a tick nobody stood before it and plays <c>proxy_arm</c>
/// as it does; IGI 1's (<c>0x475CC0</c>) is armed the moment its delay has run and plays nothing.
/// </param>
/// <param name="ReachAddsHumanRadius">
/// IGI 2's notify (0x487E90) compares <c>radius * 4096 + human radius &gt; distance</c>; IGI 1's (<c>0x475DF0</c>)
/// compares <c>radius * 4096 &gt; distance - human radius</c>. The same test, rounded differently.
/// </param>
public readonly record struct ProximityMineRules(bool ArmsWhenClear, bool ReachAddsHumanRadius)
{
    /// <summary>Gets Project I.G.I.'s mine.</summary>
    public static ProximityMineRules Igi1 { get; } = new(ArmsWhenClear: false, ReachAddsHumanRadius: false);

    /// <summary>Gets IGI 2's mine.</summary>
    public static ProximityMineRules Igi2 { get; } = new(ArmsWhenClear: true, ReachAddsHumanRadius: true);
}

/// <summary>What one run of a mine's fuse did.</summary>
/// <param name="ArmedNow">It armed this tick, and plays <c>proxy_arm</c> (IGI 2 only).</param>
/// <param name="Explode">Its countdown ran out: the exploding object's flag is raised, and it goes off on its next run.</param>
public readonly record struct ProximityMineStep(bool ArmedNow, bool Explode);

/// <summary>
/// The <c>Mine</c> task's own fields both titles share (the IGI 1 <c>Mine</c> at <c>+0x238</c>, IGI 2's at the same
/// slots): the arming delay, the countdown once set off, the activation radius, the remote flag, and the test a passing
/// human's position is put to.
/// </summary>
/// <typeparam name="TFpu">The title's float unit: IGI 1 computes in extended precision, IGI 2 in single.</typeparam>
/// <remarks>
/// The exploding half is the title's <c>ExplodeObject</c>; this only decides when its flag is raised. A mine's own run
/// calls the exploding object's run first, then <see cref="Run"/>, then deletes itself once it has exploded.
/// </remarks>
public sealed class ProximityMineFuse<TFpu>
    where TFpu : struct, IX87<TFpu>
{
    private readonly ProximityMineRules _rules;
    private bool _near;

    /// <summary>Initializes a new instance of the <see cref="ProximityMineFuse{TFpu}"/> class, <c>Mine_New</c>'s fields.</summary>
    /// <param name="rules">The title's rules.</param>
    /// <param name="armTicks">Ticks before it can be set off (<c>+0x238</c>).</param>
    /// <param name="countdownTicks">Ticks it counts once set off (<c>+0x23C</c>), already converted by the title.</param>
    /// <param name="activateRadiusMetres">How near, in metres, a human sets it off (<c>+0x244</c>).</param>
    /// <param name="remote">Whether it waits for the computer map instead of a passer-by (<c>+0x249</c>).</param>
    public ProximityMineFuse(ProximityMineRules rules, int armTicks, int countdownTicks, float activateRadiusMetres, bool remote)
    {
        _rules = rules;
        ArmTicks = armTicks;
        CountdownTicks = countdownTicks;
        ActivateRadiusMetres = activateRadiusMetres;
        IsRemote = remote;
    }

    /// <summary>The ticks a mine set off from the computer map waits (<c>+0x23C = 9</c>, both titles' map trace).</summary>
    public const int MapDetonationTicks = 9;

    /// <summary>Gets the ticks left before it can be set off.</summary>
    public int ArmTicks { get; private set; }

    /// <summary>Gets the ticks left on its countdown.</summary>
    public int CountdownTicks { get; private set; }

    /// <summary>Gets how near, in metres, a human sets it off.</summary>
    public float ActivateRadiusMetres { get; }

    /// <summary>Gets a value indicating whether it waits for the computer map.</summary>
    public bool IsRemote { get; }

    /// <summary>Gets a value indicating whether it has armed (IGI 2's <c>+0x241</c>; IGI 1 once its delay is spent).</summary>
    public bool IsArmed => _rules.ArmsWhenClear ? _armed : ArmTicks == 0;

    /// <summary>Gets a value indicating whether it has been set off (<c>+0x248</c>).</summary>
    public bool IsTriggered { get; private set; }

    private bool _armed;

    /// <summary>
    /// A human's place, told by its run (<c>Human_SendPosition</c>, 0x463080): within reach and in front of the mine's
    /// face (its frame's second column) it is near, and near an armed mine not yet set off sets it off.
    /// </summary>
    /// <param name="mine">Where the mine is.</param>
    /// <param name="face">The mine frame's second column, the face's normal (<c>m[1], m[4], m[7]</c>).</param>
    /// <param name="human">The human's origin.</param>
    /// <param name="humanRadius">The human's own radius, in units.</param>
    /// <returns><see langword="true"/> on the tick it is set off.</returns>
    public bool Sense(Vector3d mine, Vector3f face, Vector3d human, float humanRadius)
    {
        if (IsRemote)
        {
            return false;
        }

        TFpu reach = (TFpu)ActivateRadiusMetres * 4096.0;
        TFpu dx, dy, dz;
        bool within;
        if (_rules.ReachAddsHumanRadius)
        {
            // IGI 2 keeps the differences on the stack and sums y, z, x.
            dx = (TFpu)human.X - mine.X;
            dy = (TFpu)human.Y - mine.Y;
            dz = (TFpu)human.Z - mine.Z;
            TFpu distance = TFpu.Sqrt(((dy * dy) + (dz * dz)) + (dx * dx));
            within = (reach + humanRadius).Value > distance.Value;
        }
        else
        {
            // IGI 1 stores the difference as a Vec3d first, then Vec3d_Length (0x4B3130) sums x, y, z.
            dx = human.X - mine.X;
            dy = human.Y - mine.Y;
            dz = human.Z - mine.Z;
            TFpu distance = TFpu.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            within = reach.Value > (distance - humanRadius).Value;
        }
        if (!within)
        {
            return false;
        }

        TFpu ahead = ((TFpu)face.Z * dz) + ((TFpu)face.Y * dy) + ((TFpu)face.X * dx);
        if (!(ahead.Value > 0.0))
        {
            return false;
        }

        _near = true;
        if (!IsArmed || ArmTicks != 0 || IsTriggered)
        {
            return false;
        }

        IsTriggered = true;
        return true;
    }

    /// <summary>The click on it in the computer map: set off, nine ticks to go.</summary>
    public void SetOffFromMap()
    {
        IsTriggered = true;
        CountdownTicks = MapDetonationTicks;
    }

    /// <summary>The mine's own run after its exploding object's: the arming delay, then the countdown.</summary>
    /// <returns>What happened.</returns>
    public ProximityMineStep Run()
    {
        bool armedNow = false;
        if (ArmTicks != 0)
        {
            ArmTicks--;
        }
        else if (_rules.ArmsWhenClear && !_near)
        {
            armedNow = !_armed;
            _armed = true;
        }

        bool explode = false;
        if (IsTriggered)
        {
            if (CountdownTicks != 0)
            {
                CountdownTicks--;
            }
            else
            {
                explode = true;
            }
        }

        _near = false;
        return new ProximityMineStep(armedNow, explode);
    }
}
