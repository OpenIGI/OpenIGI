using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// Project I.G.I.'s laid proximity mine, the <c>Mine</c> task (<c>Mine_New</c>, <c>0x476040</c>): an
/// <c>ExplodeObject</c> made under the level's dynamic task at the surface the weapon's trace found, turned so its
/// frame's second column is that surface's normal, with the shared <see cref="ProximityMineFuse{TFpu}"/> deciding when
/// it goes off.
/// </summary>
/// <remarks>
/// <para>
/// <c>Mine_New</c> hands the exploding half <c>ExplodeObject_Setup(1, explode radius, 0, 1, 0, 5, 1, "explo_proxy")</c>
/// (<c>0x481360</c>): health 1, the blast's full-damage radius, no falloff, a damage factor of 1, no delay after a blast,
/// five fragments and one FireBall. The parameter change (<c>0x475D90</c>) then rounds the countdown seconds into ticks.
/// </para>
/// <para>
/// A mine is a solid in the world like any <c>ExplodeObject</c>: a round that strikes it (<c>0x481E60</c>) and a blast
/// that reaches it (<c>0x481EA0</c>) both set its countdown to nought, so it goes off on its next run.
/// </para>
/// </remarks>
public sealed class PlacedMine
{
    /// <summary>The sound its blast makes (<c>Mine_New</c>'s <c>"explo_proxy"</c>).</summary>
    public const string BlastSound = "explo_proxy";

    /// <summary>The sound it makes when a human sets it off (<c>0x475DF0</c>'s <c>"proxy_arm"</c>).</summary>
    public const string TriggerSound = "proxy_arm";

    /// <summary>The fragments its blast throws (<c>ExplodeObject+0x20C</c>).</summary>
    public const int Fragments = 5;

    /// <summary>The FireBalls its blast shows (<c>ExplodeObject+0x210</c>).</summary>
    public const int Fireballs = 1;

    /// <summary>The weapon config's defaults (<c>0x475960</c>): arm after 2 s, count 0.5 s, sense 1.5 m, blast 3 m.</summary>
    public const float DefaultArmSeconds = 2f;

    /// <inheritdoc cref="DefaultArmSeconds"/>
    public const float DefaultCountdownSeconds = 0.5f;

    /// <inheritdoc cref="DefaultArmSeconds"/>
    public const float DefaultActivateRadiusMetres = 1.5f;

    /// <inheritdoc cref="DefaultArmSeconds"/>
    public const float DefaultExplosionRadiusMetres = 3f;

    private readonly ProximityMineFuse<X87> _fuse;
    private float _damage;
    private int _timer = -1;
    private bool _explodeFlag;

    /// <summary>Initializes a new instance of the <see cref="PlacedMine"/> class.</summary>
    /// <param name="position">The trace's contact, where it lies.</param>
    /// <param name="orientation">Its frame, <c>0x414A60</c> of the contact normal.</param>
    /// <param name="model">Its model, the weapon's key.</param>
    /// <param name="armSeconds">The config's arming time.</param>
    /// <param name="countdownSeconds">The config's countdown.</param>
    /// <param name="activateRadiusMetres">The config's activation radius.</param>
    /// <param name="explosionRadiusMetres">The config's explosion radius.</param>
    /// <param name="remote">Whether it was laid with the alternate fire and waits for the computer map.</param>
    /// <param name="contactRadiusUnits">Its model's bound (<c>0x4D0950</c>), which a blast is measured against.</param>
    /// <param name="blastPointOffset">Its model's magic vertex 0 in its own frame, where its blast starts (<c>0x414E20</c>).</param>
    public PlacedMine(
        Vector3d position,
        Matrix3f orientation,
        string model,
        float armSeconds = DefaultArmSeconds,
        float countdownSeconds = DefaultCountdownSeconds,
        float activateRadiusMetres = DefaultActivateRadiusMetres,
        float explosionRadiusMetres = DefaultExplosionRadiusMetres,
        bool remote = false,
        float contactRadiusUnits = 0f,
        Vector3f blastPointOffset = default)
    {
        Position = position;
        Orientation = orientation;
        Model = model ?? string.Empty;
        ExplosionRadiusUnits = explosionRadiusMetres * 4096f;
        ContactRadiusUnits = contactRadiusUnits;
        BlastPointOffset = blastPointOffset;

        // 0x4755A0 rounds the arming seconds at thirty a tick; 0x475D90 rounds the countdown the same way.
        _fuse = new ProximityMineFuse<X87>(
            ProximityMineRules.Igi1,
            RoundTicks(armSeconds),
            RoundTicks(countdownSeconds),
            activateRadiusMetres,
            remote);
    }

    /// <summary>Gets the task the mine is to whatever names it -- the computer map's click.</summary>
    public GameTask Task { get; } = new MineTask();

    /// <summary>Gets where it lies.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets its frame.</summary>
    public Matrix3f Orientation { get; }

    /// <summary>Gets its face's normal, the frame's second column (<c>m[1], m[4], m[7]</c>).</summary>
    public Vector3f Face => new(Orientation.M12, Orientation.M22, Orientation.M32);

    /// <summary>Gets its model.</summary>
    public string Model { get; }

    /// <summary>Gets its blast's full-damage radius, in units.</summary>
    public float ExplosionRadiusUnits { get; }

    /// <summary>Gets its model's bound, which a blast reaching it is measured against.</summary>
    public float ContactRadiusUnits { get; }

    /// <summary>Gets its model's magic vertex 0, in its own frame.</summary>
    public Vector3f BlastPointOffset { get; }

    /// <summary>Gets or sets its model as a round's trace meets it, or <see langword="null"/>.</summary>
    public OpenIGI.Game.World.RetailRayModel? Solid { get; set; }

    /// <summary>Gets its fuse.</summary>
    public ProximityMineFuse<X87> Fuse => _fuse;

    /// <summary>Gets a value indicating whether it waits for the computer map.</summary>
    public bool IsRemote => _fuse.IsRemote;

    /// <summary>Gets a value indicating whether it has exploded (<c>ExplodeObject+0x190</c>).</summary>
    public bool IsExploded { get; private set; }

    /// <summary>Gets where its blast starts: magic vertex 0 turned by its frame, plus its position (<c>0x481A60</c>).</summary>
    public Vector3d BlastPoint
    {
        get
        {
            Vector3f offset = Orientation.Transposed().Transform(BlastPointOffset);
            return new Vector3d(Position.X + offset.X, Position.Y + offset.Y, Position.Z + offset.Z);
        }
    }

    /// <summary>A human's place (<c>0x475DF0</c>), told by its run.</summary>
    /// <param name="origin">The human's origin.</param>
    /// <param name="radius">The human's radius.</param>
    /// <returns><see langword="true"/> on the tick it is set off, when <see cref="TriggerSound"/> plays at it.</returns>
    public bool Sense(Vector3d origin, float radius) => !IsExploded && _fuse.Sense(Position, Face, origin, radius);

    /// <summary>A round struck it (<c>0x481E60</c>): its damage made up past its health of 1 sets its timer to nought.</summary>
    /// <param name="damage">The round's damage.</param>
    public void Hit(float damage)
    {
        if (IsExploded)
        {
            return;
        }

        // The sum stays in the register for the test; only its store is chopped (RC = chop), which is why five 0.2 rounds
        // leave 0.99999994 and a sixth is needed.
        X87 total = (X87)damage + _damage;
        _damage = total.F;
        if (total.Value >= 1.0)
        {
            _timer = 0;
        }
    }

    /// <summary>A blast reached it (<c>0x481EA0</c>): its dose made up past its health sets its timer to its delay of nought.</summary>
    /// <param name="dose">The blast's dose at it (<c>0x416C90</c>).</param>
    public void Blast(float dose)
    {
        if (IsExploded)
        {
            return;
        }

        X87 total = (X87)dose + _damage;
        _damage = total.F;
        if (total.Value >= 1.0)
        {
            _timer = 0;
        }
    }

    /// <summary>
    /// Its run (<c>0x475CC0</c>): the exploding object's (<c>0x481970</c>) -- which explodes it on its flag or a spent
    /// timer -- then the fuse, which raises that flag once the countdown is spent.
    /// </summary>
    /// <returns><see langword="true"/> when it exploded this run; it is gone afterwards.</returns>
    public bool Run()
    {
        bool exploded = false;
        if (!IsExploded)
        {
            if (_explodeFlag || _timer == 0)
            {
                // 0x481A60. A mine names no destroyed model, so the run returns before its timer is counted.
                IsExploded = true;
                exploded = true;
            }
            else if (_timer > 0)
            {
                _timer--;
            }
        }

        if (_fuse.Run().Explode)
        {
            _explodeFlag = true;
        }

        return exploded;
    }

    // The Mine task's identity; it joins no tree, being run by its owner.
    private sealed class MineTask : GameTask
    {
    }

    // round_int at thirty ticks a second: the FPU's round-to-nearest-even.
    private static int RoundTicks(float seconds) =>
        (int)Math.Round((double)seconds * 30.0, MidpointRounding.ToEven);
}
