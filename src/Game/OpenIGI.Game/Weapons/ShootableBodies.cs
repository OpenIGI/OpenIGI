using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Weapons;

/// <summary>A human a bullet's trace can meet: its own colliders, and the task the hit is sent to.</summary>
/// <remarks>
/// The engine keeps humans among the world's colliders: the shot's trace (Project I.G.I. <c>0x47B750</c>, I.G.I. 2
/// <c>GunShot_RunInstant</c>) asks each it meets whether the round may strike it (<c>WeaponCollisionEvaluate</c>) and sends
/// the nearest that agrees its <c>WeaponHit</c>. A body is one such collider, whichever game's human it is.
/// </remarks>
public interface IShootableBody
{
    /// <summary>Gets the task the round asks and strikes.</summary>
    GameTask Task { get; }

    /// <summary>Casts a round at the body's colliders.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="target">Where it stops.</param>
    /// <param name="hit">Receives where it first meets the body.</param>
    /// <returns><see langword="true"/> when the round meets the body.</returns>
    bool TryMeet(Vector3d origin, Vector3d target, out HumanHit hit);
}

/// <summary>One game's humans as a level's shots find them.</summary>
public interface IShootableBodies
{
    /// <summary>Gets the bodies a round may meet now: the living, in the level's running containers.</summary>
    /// <returns>The bodies.</returns>
    IEnumerable<IShootableBody> Bodies();

    /// <summary>A round went by: the humans that did not stop it hear it pass (<c>Human_EvaluateGunShot</c>).</summary>
    /// <param name="origin">Where the round started.</param>
    /// <param name="target">Where it was cast to.</param>
    /// <param name="struck">The task it struck, or <see langword="null"/> for none.</param>
    void RoundPassed(Vector3d origin, Vector3d target, GameTask? struck);

    /// <summary>A round is cast past these bodies, before it is known what it meets: whatever a cast does to the humans it visits.</summary>
    /// <param name="origin">Where the round starts.</param>
    /// <param name="target">Where it is cast to.</param>
    /// <param name="shooter">Who fired it.</param>
    /// <remarks>Project I.G.I.'s round visit (<c>0x45F500</c>) rebuilds the bone palette, and so the eye, of each human it passes.</remarks>
    void RoundCast(Vector3d origin, Vector3d target, GameTask? shooter)
    {
    }

    /// <summary>
    /// Gets what a round's damage is multiplied by as it strikes one of these bodies: one for the game's own humans; content that
    /// brings another game's humans may tune how the two games' damage meets.
    /// </summary>
    float DamageScale => 1f;
}

/// <summary>Every source of bodies a level's shots may meet, asked as one: the game's own humans, and any a mod brings.</summary>
public sealed class LevelBodies
{
    private readonly List<IShootableBodies> _sources = [];

    /// <summary>Gets the sources, in the order they joined.</summary>
    public IReadOnlyList<IShootableBodies> Sources => _sources;

    /// <summary>Adds a source of bodies.</summary>
    /// <param name="source">The source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public void Add(IShootableBodies source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
    }

    /// <summary>Removes a source of bodies.</summary>
    /// <param name="source">The source.</param>
    /// <returns><see langword="true"/> when it was one of them.</returns>
    public bool Remove(IShootableBodies source) => _sources.Remove(source);

    /// <summary>Removes every source, as a level is left.</summary>
    public void Clear() => _sources.Clear();

    /// <summary>Offers a round to the level's bodies, as <see cref="Shoot(IReadOnlyList{IShootableBodies}, Vector3d, Vector3d, GameTask?, double, bool, Func{HumanHit, WeaponHit}, out HumanHit)"/>.</summary>
    /// <param name="origin">Where the round starts.</param>
    /// <param name="target">Where it stops.</param>
    /// <param name="shooter">Who fired it, which the bodies weigh it by.</param>
    /// <param name="hitFor">The hit the struck body is sent, made from where the round met it.</param>
    /// <param name="hit">Receives where the round met the body it struck.</param>
    /// <returns><see langword="true"/> when a body took the round.</returns>
    public bool Shoot(Vector3d origin, Vector3d target, GameTask? shooter, Func<HumanHit, WeaponHit> hitFor, out HumanHit hit) =>
        Shoot(_sources, origin, target, shooter, double.PositiveInfinity, alreadyHeard: false, hitFor, out hit);

    /// <summary>Offers a round to the level's bodies short of the surface that stops it.</summary>
    /// <param name="origin">Where the round starts.</param>
    /// <param name="target">Where it stops.</param>
    /// <param name="shooter">Who fired it.</param>
    /// <param name="stopDistance">The distance to the surface the round's own trace met first; see <see cref="HumanShot"/>.</param>
    /// <param name="alreadyHeard">Whether this round's near miss was already offered; see <see cref="HumanShot"/>.</param>
    /// <param name="hitFor">The hit the struck body is sent, made from where the round met it.</param>
    /// <param name="hit">Receives where the round met the body it struck.</param>
    /// <returns><see langword="true"/> when a body short of the stop met the round.</returns>
    public bool Shoot(
        Vector3d origin,
        Vector3d target,
        GameTask? shooter,
        double stopDistance,
        bool alreadyHeard,
        Func<HumanHit, WeaponHit> hitFor,
        out HumanHit hit) =>
        Shoot(_sources, origin, target, shooter, stopDistance, alreadyHeard, hitFor, out hit);

    /// <summary>Offers a round to some bodies: the nearest that agrees to be struck takes it, and every source hears it pass.</summary>
    /// <param name="sources">The bodies' sources, in order: a tie goes to the first.</param>
    /// <param name="origin">Where the round starts.</param>
    /// <param name="target">Where it stops.</param>
    /// <param name="shooter">Who fired it, which each body weighs in its <c>WeaponCollisionEvaluate</c>.</param>
    /// <param name="stopDistance">
    /// How far from <paramref name="origin"/> the world lets the round reach: the distance to the nearest surface its own
    /// trace met, or <see cref="double.PositiveInfinity"/>. A body at or beyond it is behind that surface and is not struck.
    /// </param>
    /// <param name="alreadyHeard">
    /// Whether the bodies have already heard this round pass: retail offers the near miss once per round, over its whole
    /// range, before the march (<c>0x47B690</c>), so a march casting again past a surface or a body passes true.
    /// </param>
    /// <param name="hitFor">The hit the struck body is sent, made from where the round met it.</param>
    /// <param name="hit">
    /// Receives where the round met the body it struck. An exit (<see cref="HumanHit.IsExit"/>) is the round leaving a body
    /// it went into: no handler runs for it (<c>0x47B750</c>), so it is reported and nothing is sent.
    /// </param>
    /// <returns><see langword="true"/> when a body short of the stop met the round.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The trace hands the gunshot handler its nearest contact alone (<c>0x47BA54</c>), so a body behind a wall is untouched
    /// -- but every body hears the round over the whole segment, the part beyond the wall included.
    /// </remarks>
    public static bool Shoot(
        IReadOnlyList<IShootableBodies> sources,
        Vector3d origin,
        Vector3d target,
        GameTask? shooter,
        double stopDistance,
        bool alreadyHeard,
        Func<HumanHit, WeaponHit> hitFor,
        out HumanHit hit)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(hitFor);
        hit = default;
        IShootableBody? struck = null;
        float scale = 1f;
        WeaponCollisionQuery query = new(shooter, null, 1f);
        foreach (IShootableBodies source in sources)
        {
            source.RoundCast(origin, target, shooter);
        }

        foreach (IShootableBodies source in sources)
        {
            foreach (IShootableBody body in source.Bodies())
            {
                if (body.Task is IWeaponCollisionTarget evaluate && !evaluate.EvaluateWeaponCollision(query))
                {
                    continue;
                }

                if (body.TryMeet(origin, target, out HumanHit candidate) && (struck is null || candidate.Distance < hit.Distance))
                {
                    hit = candidate;
                    struck = body;
                    scale = source.DamageScale;
                }
            }
        }

        if (struck is not null && hit.Distance >= stopDistance)
        {
            hit = default;
            struck = null;
        }

        if (!alreadyHeard)
        {
            foreach (IShootableBodies source in sources)
            {
                source.RoundPassed(origin, target, hit.IsExit ? null : struck?.Task);
            }
        }

        if (struck is null)
        {
            return false;
        }

        if (hit.IsExit)
        {
            return true;
        }

        if (struck.Task is IWeaponHitTarget taker)
        {
            WeaponHit sent = hitFor(hit);
            taker.WeaponHit(scale == 1f ? sent : sent with { Damage = sent.Damage * scale });
        }

        return true;
    }
}
