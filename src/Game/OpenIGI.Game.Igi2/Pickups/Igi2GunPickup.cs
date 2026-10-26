using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Pickups;

/// <summary>What a picked-up weapon gives, <c>GunPickupContext_t</c>: the rounds in its load and for each fire type.</summary>
/// <param name="WeaponType">The weapon.</param>
/// <param name="AmmoInClip">The rounds the load holds, <c>nAmmoInClip</c>.</param>
/// <param name="AmmoForFireType">The rounds for each fire type, <c>anAmmoForFiretype</c>.</param>
public readonly record struct Igi2GunPickupGrant(Igi2WeaponTypeTask WeaponType, int AmmoInClip, IReadOnlyList<int> AmmoForFireType);

/// <summary>Who is offered a pickup, <c>PickupContext_t</c>: the human, where its eyes are, and whether it takes the pickup outright.</summary>
/// <param name="Taker">The human.</param>
/// <param name="Eyes">Where its eyes are, <c>HumanView_t.tPos</c> (<c>HumanView+0x1A0</c>): the sight is checked from there.</param>
/// <param name="IsAutoPickup">Whether a pickup within reach is taken outright, <c>isAutoPickup</c>.</param>
public readonly record struct Igi2PickupOffer(IIgi2PickupTaker Taker, Vector3d Eyes, bool IsAutoPickup);

/// <summary>A human a pickup is offered to.</summary>
public interface IIgi2PickupTaker
{
    /// <summary>Gets what the human carries, or <see langword="null"/> for a human with no weapon.</summary>
    Igi2Inventory? Inventory { get; }

    /// <summary>Takes a weapon picked up, <c>Human_GunPickupHandler</c> (<c>igi2 0x500180</c>).</summary>
    /// <param name="grant">What the weapon gives.</param>
    void PickUpGun(in Igi2GunPickupGrant grant);
}

/// <summary>
/// A weapon lying in the level, <c>GunPickup</c>: where it lies, the weapon and rounds it gives, and the test a human
/// passes to take it.
/// </summary>
/// <remarks>
/// <para>
/// A <c>RigidDynCubeObj</c> carrying the weapon's <c>Fixed Model</c>. <c>GunPickup_UpdateInternalDataHandler</c>
/// (<c>igi2 0x46BA00</c>) works out what it gives: a grenade or a medipack one round in its load and one for its first fire
/// type, anything else a full load and nothing more for the first; the second fire type always a full load of its own.
/// </para>
/// <para>
/// Each tick <c>GunPickup_RunHandler</c> (<c>0x46A760</c>) lets a pickup still falling fall, and deletes one that was
/// taken. A pickup the level places starts at rest, so for it the handler is only the deleting. A thrown one
/// (<see cref="Thrown"/>, <c>GunPickup_New</c>, <c>0x46C2F0</c>) falls (<see cref="Fall"/>). Not yet: a multiplayer game's
/// respawn.
/// </para>
/// </remarks>
public sealed class Igi2GunPickup
{
    /// <summary>The least a pickup reaches, <c>4096</c> units: a metre.</summary>
    public const float MinimumReach = 4096f;

    /// <summary>How far above or below its reach a pickup still reaches, <c>8192</c> units.</summary>
    public const float HeightReach = 8192f;

    /// <summary>
    /// The groups the sight to a pickup meets, <c>0x08000000</c>: the solid models alone, with neither the ground, the
    /// trees nor the spline segments (<c>GunPickup_IsAbleToPickup</c>, <c>igi2 0x46BFB8</c>).
    /// </summary>
    public const uint SightGroups = Igi2RigidObject.RigidGroup;

    private readonly int[] _ammoForFireType = new int[Igi2WeaponTypeTask.FireTypeSlots];

    /// <summary>Initializes a new instance of the <see cref="Igi2GunPickup"/> class, as the level places it.</summary>
    /// <param name="task">The task the level declares it with, or <see langword="null"/> for one made at run time.</param>
    /// <param name="weaponType">The weapon it gives.</param>
    /// <param name="tables">The session's weapon tables, which say what each fire type loads.</param>
    /// <param name="position">Where it lies.</param>
    /// <param name="orientation">Its orientation, nine floats row by row.</param>
    /// <param name="modelRadius">Its model's radius, LOD 0's <c>vRadius</c>; one for a model that does not exist.</param>
    public Igi2GunPickup(
        Igi2GunPickupTask? task,
        Igi2WeaponTypeTask weaponType,
        Igi2WeaponTables tables,
        Vector3d position,
        IReadOnlyList<float> orientation,
        float modelRadius)
    {
        ArgumentNullException.ThrowIfNull(weaponType);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(orientation);
        Task = task;
        WeaponType = weaponType;
        Position = position;
        Orientation = [.. orientation];
        ModelRadius = modelRadius;

        bool single = weaponType.WeaponCategory is Igi2WeaponCategory.Grenade or Igi2WeaponCategory.Medipack;
        AmmoInClip = single ? 1 : weaponType.AmmoInfo(0, tables).RoundsPerClip;
        _ammoForFireType[0] = single ? 1 : 0;
        _ammoForFireType[1] = weaponType.AmmoInfo(1, tables).RoundsPerClip;
    }

    /// <summary>Gets the task the level declares it with, or <see langword="null"/>.</summary>
    public Igi2GunPickupTask? Task { get; }

    /// <summary>Gets the weapon it gives.</summary>
    public Igi2WeaponTypeTask WeaponType { get; }

    /// <summary>The weapon a thrown pickup of which lies flat as it was thrown, <c>WEAPON_ID_C4BOMB</c> (<c>zLine</c>, <c>igi2 0x6204C8</c>).</summary>
    public const string FlatWeaponId = "WEAPON_ID_C4BOMB";

    /// <summary>The query groups a falling pickup's lines meet, <c>Forest_bQTaskGroup | 0xD8000000</c>.</summary>
    public const uint FallGroups = Igi2CollisionWorld.ForestGroup | 0xD8000000u;

    /// <summary>The collision mesh a falling pickup's lines meet, <c>1</c>.</summary>
    public const int FallMesh = 1;

    /// <summary>What gravity takes off a falling pickup's height speed a tick, <c>44.600887</c>.</summary>
    public const float FallGravity = 44.600887f;

    /// <summary>The share of its speed a falling pickup loses to each surface it rubs, <c>0.0083333338</c>.</summary>
    public const float Friction = 0.0083333338f;

    private static readonly float SpeedLimitDown = -1228.8f;
    private readonly WorldLineTrace _fall = new(6) { Mesh = FallMesh, Includes = static owner => !Igi2ObjectCollision.PassesThrough(owner) };

    /// <summary>Gets where it lies, <c>tPos</c> (<c>+0x158</c>).</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets its speed a tick while it falls (<c>+0x188</c>).</summary>
    public Vector3f Speed { get; private set; }

    /// <summary>Gets the normal of the ground it last fell onto (<c>+0x200</c>): straight up until it has.</summary>
    public Vector3f GroundNormal { get; private set; } = new(0f, 0f, 1f);

    /// <summary>Gets its orientation, <c>tOrientation</c>: nine floats, row by row.</summary>
    public float[] Orientation { get; }

    /// <summary>Gets its scale on each local axis, <c>tScale</c> (<c>+0xF8</c>).</summary>
    public Vector3f Scale { get; init; } = new(1f, 1f, 1f);

    /// <summary>Gets its model's radius, LOD 0's <c>vRadius</c>.</summary>
    public float ModelRadius { get; }

    /// <summary>Gets the rounds the load it gives holds, <c>nAmmoInClip</c> (<c>+0x148</c>).</summary>
    public int AmmoInClip { get; private set; }

    /// <summary>Gets the rounds it gives each fire type, <c>anAmmoForFiretype</c> (<c>+0x14C</c>).</summary>
    public IReadOnlyList<int> AmmoForFireType => _ammoForFireType;

    /// <summary>Gets a value indicating whether it was taken and is to be deleted, <c>isDelete</c> (<c>+0x1FC</c>).</summary>
    public bool IsDelete { get; private set; }

    /// <summary>Gets or sets a value indicating whether its own run handler runs it, in the level's tree or its dynamic tasks.</summary>
    public bool IsDriven { get; set; }

    /// <summary>Gets a value indicating whether it is falling, <c>isMoving</c> (<c>+0x1FD</c>): a dropped weapon's.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>Gets a value indicating whether a falling pickup has landed, <c>isLanded</c> (<c>+0x1FE</c>): its drop sound has played.</summary>
    public bool IsLanded { get; private set; }

    /// <summary>
    /// A weapon thrown into the level, <c>GunPickup_New</c> (<c>igi2 0x46C2F0</c>): lying turned as it was held, about Z as it was
    /// and on its side (a C4 bomb as it was but flat), moving along a heading at 614.4 a tick and up at 273.07, a short throw a
    /// quarter as far and 68.27 up.
    /// </summary>
    /// <param name="weaponType">The weapon it gives.</param>
    /// <param name="tables">The session's weapon tables.</param>
    /// <param name="position">Where it starts.</param>
    /// <param name="orientation">How it was held, nine floats row by row.</param>
    /// <param name="throwGamma">The heading it is thrown along.</param>
    /// <param name="isMoving">Whether it falls.</param>
    /// <param name="isThrowShort">Whether the throw is a short one.</param>
    /// <param name="ammoInClip">The rounds its load holds.</param>
    /// <param name="ammoCount">The rounds it gives its two fire types, or <see langword="null"/> for what its type gives.</param>
    /// <param name="modelRadius">Its model's radius.</param>
    /// <returns>The pickup.</returns>
    /// <remarks>
    /// The heading's direction is the second column of a turn by it, made unit unless it is nought; a type's own ammunition
    /// (<c>GunPickup_UpdateInternalDataHandler</c>, <c>0x46BA00</c>) is then replaced by what the thrower gave it.
    /// </remarks>
    public static Igi2GunPickup Thrown(
        Igi2WeaponTypeTask weaponType,
        Igi2WeaponTables tables,
        Vector3d position,
        IReadOnlyList<float> orientation,
        float throwGamma,
        bool isMoving,
        bool isThrowShort,
        int ammoInClip,
        IReadOnlyList<int>? ammoCount,
        float modelRadius)
    {
        ArgumentNullException.ThrowIfNull(weaponType);
        ArgumentNullException.ThrowIfNull(orientation);
        (float alpha, float beta, float gamma) = Igi2Pose.Angles(orientation);
        if (string.Equals(weaponType.WeaponId, FlatWeaponId, StringComparison.Ordinal))
        {
            beta = 0f;
        }
        else
        {
            alpha = alpha < -1.5707964f || alpha > 1.5707964f ? 3.1415927f : 0f;
            beta = beta >= 0f ? 1.5707964f : -1.5707964f;
        }

        Igi2GunPickup pickup = new(null, weaponType, tables, position, EngineEuler.Rotation(alpha, beta, gamma), modelRadius)
        {
            IsMoving = isMoving,
        };

        float[] heading = EngineEuler.Rotation(0f, 0f, throwGamma);
        float x = heading[1], y = heading[4], z = 0f;
        if (x != 0f || y != 0f)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((Fpu)x * x) + ((Fpu)y * y));
            (x, y, z) = (((Fpu)x * inverse).F, ((Fpu)y * inverse).F, (inverse * 0.0).F);
        }

        x = ((Fpu)x * 614.4f).F;
        y = ((Fpu)y * 614.4f).F;
        z = BitConverter.Int32BitsToSingle(0x43888889);
        if (isThrowShort)
        {
            z = BitConverter.Int32BitsToSingle(0x42888889);
            x = ((Fpu)x * 0.25f).F;
            y = ((Fpu)y * 0.25f).F;
        }

        pickup.Speed = new Vector3f(x, y, z);
        pickup.AmmoInClip = ammoInClip;
        if (ammoCount is not null)
        {
            pickup._ammoForFireType[0] = ammoCount[0];
            pickup._ammoForFireType[1] = ammoCount[1];
        }

        return pickup;
    }

    /// <summary>
    /// A falling pickup's tick, <c>GunPickup_RunHandler</c> (<c>igi2 0x46A9B5</c>): gravity, then six lines from where it is, four
    /// to points around it on the plane of the ground it last fell on and two straight down and up; what the four strike pushes
    /// and slows it, what lies below lands it, and its speed carries it on.
    /// </summary>
    /// <param name="world">The level.</param>
    /// <param name="random">The run stream the drop sound draws from.</param>
    /// <param name="playSound">Plays a sound at a point.</param>
    /// <remarks>
    /// <para>
    /// The four points lie a radius ahead and behind along Y and 819.2 above and below, each with its own component along the ground
    /// normal taken off, then moved by the speed. A surface other than the ground among them turns the speed away from its face,
    /// loses a hundred and twentieth of it, and adds where the line struck short of its point to a correction, averaged over them.
    /// </para>
    /// <para>
    /// Below: the ground's normal is kept, and within two metres the fall slows to 409.6 a tick at most. Within 819.2 the pickup
    /// lands: the drop sound plays the first time (one of two, by a draw); on a slope steeper than about 37 degrees it slides,
    /// otherwise it stops there, ceases falling if it is the ground, and is turned to lie along it. Its height ends 122.88 above
    /// what is below. If it fell through, the line up puts it back 2048 above what it struck. It never falls faster than 1228.8 a tick.
    /// </para>
    /// <para>
    /// Water is no surface to it: the probes pass over it and, below, it lets the pickup sink (the C4 bomb alone lands on it, and if it
    /// falls under, comes back up). It comes to rest only on the ground or a building's room.
    /// </para>
    /// </remarks>
    public void Fall(IIgi2CollisionQuery world, IgiRandom random, Action<string, Vector3d> playSound)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(playSound);
        if (!IsMoving)
        {
            return;
        }

        float sx = Speed.X, sy = Speed.Y;
        float sz = ((Fpu)Speed.Z - FallGravity).F;
        float larger = Scale.Y <= Scale.Z ? Scale.Z : Scale.Y;
        float largest = larger >= Scale.X ? larger : Scale.X;
        float r = ((Fpu)largest * ModelRadius).F;
        Vector3f n = GroundNormal;
        float zeroZ = ((Fpu)n.Z * 0f).F, zeroX = ((Fpu)n.X * 0f).F, zeroY = ((Fpu)n.Y * 0f).F;

        Fpu ahead = (((Fpu)r * n.Y) + zeroX) + zeroZ;
        Fpu up = (((Fpu)n.Z * 819.2f) + zeroY) + zeroX;
        Fpu behind = (((-(Fpu)r) * n.Y) + zeroX) + zeroZ;
        Fpu down = ((zeroY - ((Fpu)n.Z * 819.2f)) + zeroX);
        Vector3d p = Position;
        Span<(double X, double Y, double Z)> around =
        [
            (-((Fpu)n.X * ahead).Value, ((Fpu)r - ((Fpu)n.Y * ahead).F).Value, -((Fpu)n.Z * ahead).F),
            (-((Fpu)n.X * up).Value, -((Fpu)n.Y * up).F, ((Fpu)819.2f - ((Fpu)n.Z * up).F).Value),
            (-((Fpu)n.X * behind).Value, (-(Fpu)r - ((Fpu)n.Y * behind).F).Value, -((Fpu)n.Z * behind).F),
            (-((Fpu)n.X * down).Value, -((Fpu)n.Y * down).F, ((Fpu)(-819.2f) - ((Fpu)n.Z * down).F).Value),
        ];

        for (int i = 0; i < 4; i++)
        {
            _fall.Starts[i] = p;
            _fall.Ends[i] = new Vector3d(
                (((Fpu)around[i].X + p.X) + sx).Value,
                (((Fpu)around[i].Y + p.Y) + sy).Value,
                (((Fpu)around[i].Z + p.Z) + sz).Value);
        }

        _fall.Starts[4] = p;
        _fall.Starts[5] = p;
        _fall.Ends[4] = new Vector3d(p.X, p.Y, ((Fpu)p.Z - 8192.0).Value);
        _fall.Ends[5] = new Vector3d(p.X, p.Y, ((Fpu)p.Z + 20480.0).Value);
        _fall.Evaluator = this;
        world.TraceLines(_fall, FallGroups);

        WorldIntersection below = _fall.Intersections[4], above = _fall.Intersections[5];
        bool anyHit = false;
        for (int i = 0; i < 6; i++)
        {
            anyHit |= _fall.Intersections[i].IsHit;
        }

        if (anyHit)
        {
            Fpu cx = 0f, cy = 0f;
            float cz = 0f;
            int hits = 0;
            for (int i = 0; i < 4; i++)
            {
                WorldIntersection hit = _fall.Intersections[i];
                if (!hit.IsHit || hit.HitObject is Igi2TerrainCollider or World.Igi2WaterBody)
                {
                    continue;
                }

                Vector3f face = Unit(hit.Normal);
                hit.Normal = face;
                Fpu d = (((Fpu)face.X * sx) + ((Fpu)face.Y * sy)) + ((Fpu)sz * face.Z);
                Vector3f away = d.Value >= 0.0 ? new Vector3f(((Fpu)face.X * -1.0).F, ((Fpu)face.Y * -1.0).F, ((Fpu)face.Z * -1.0).F) : face;
                (sx, sy, sz) = Rub(sx, sy, sz, away, d);
                hits++;
                cx = (cx + hit.Position.X) - _fall.Ends[i].X;
                cy += ((Fpu)hit.Position.Y - _fall.Ends[i].Y).F;
                cz = (((Fpu)hit.Position.Z - _fall.Ends[i].Z).F + (Fpu)cz).F;
            }

            if (hits > 1)
            {
                Fpu inverse = (Fpu)1.0 / (double)hits;
                cx *= inverse;
                cy *= inverse;
                cz = (inverse * cz).F;
            }

            p = new Vector3d((cx + p.X).Value, (cy + p.Y).Value, ((Fpu)cz + p.Z).Value);
            // Water below lets it sink, unless it is the C4 bomb, which floats.
            bool isFlat = string.Equals(WeaponType.WeaponId, FlatWeaponId, StringComparison.Ordinal);
            if (below.IsHit && (below.HitObject is not World.Igi2WaterBody || isFlat))
            {
                GroundNormal = Unit(below.Normal);
                Fpu height = (Fpu)_fall.Starts[4].Z - below.Position.Z;
                if (height.Value < 2048.0)
                {
                    sz = sz >= -409.60001f ? (sz <= 0f ? sz : 0f) : -409.60001f;
                }

                if (height.Value <= 819.2000122070312)
                {
                    Vector3f ground = Unit(below.Normal);
                    below.Normal = ground;
                    if (!IsLanded)
                    {
                        uint draw = random.NextRaw();
                        string sound = (ushort)draw * IgiRandom.Scale <= 0.5 ? "weapondrop_02" : "weapondrop_01";
                        playSound(sound, p);
                        IsLanded = true;
                    }

                    if (ground.Z <= 0.8f)
                    {
                        Fpu d = (((Fpu)ground.Z * sz) + ((Fpu)ground.Y * sy)) + ((Fpu)ground.X * sx);
                        (sx, sy, sz) = Rub(sx, sy, sz, ground, d);
                    }
                    else
                    {
                        sx = sy = sz = 0f;
                        if (below.HitObject is Igi2TerrainCollider || (below.HitObject?.Owner as Igi2CollisionOwner)?.IsSector == true)
                        {
                            IsMoving = false;
                        }

                        p = new Vector3d(p.X, p.Y, below.Position.Z);
                    }

                    Fpu settle = ((Fpu)p.Z - below.Position.Z) - 122.879997253418;
                    p = new Vector3d(p.X, p.Y, ((Fpu)p.Z - settle).Value);
                    LieAlong(ground);
                }
            }

            if (above.IsHit && above.Normal.Z > 0f)
            {
                // Fallen under water, the C4 bomb comes back up to float; under anything else, a pickup with no floor beneath is put
                // back on top.
                if (above.HitObject is World.Igi2WaterBody)
                {
                    if (isFlat)
                    {
                        IsMoving = true;
                        p = new Vector3d(p.X, p.Y, ((Fpu)above.Position.Z + 2048.0).Value);
                    }
                }
                else if (!below.IsHit || !(below.Normal.Z > 0f))
                {
                    p = new Vector3d(p.X, p.Y, ((Fpu)above.Position.Z + 2048.0).Value);
                }
            }
        }

        sz = sz <= SpeedLimitDown ? SpeedLimitDown : sz;
        Speed = new Vector3f(sx, sy, sz);
        Position = new Vector3d(((Fpu)sx + p.X).Value, ((Fpu)sy + p.Y).Value, ((Fpu)sz + p.Z).Value);
    }

    /// <summary>A speed turned off a face it rubs, <c>s − n(n·s)</c>, then a hundred and twentieth of it lost.</summary>
    private static (float X, float Y, float Z) Rub(float sx, float sy, float sz, Vector3f n, Fpu d)
    {
        float y = ((Fpu)n.Y * d).F, z = ((Fpu)n.Z * d).F;
        sx = ((Fpu)sx - ((Fpu)n.X * d)).F;
        sy = ((Fpu)sy - y).F;
        sz = ((Fpu)sz - z).F;
        float ly = ((Fpu)sy * Friction).F, lz = ((Fpu)sz * Friction).F;
        sx = ((Fpu)sx - ((Fpu)sx * Friction)).F;
        sy = ((Fpu)sy - ly).F;
        sz = ((Fpu)sz - lz).F;
        return (sx, sy, sz);
    }

    /// <summary>A vector made unit, unless it is nought: its length's reciprocal taken once and each part scaled by it.</summary>
    private static Vector3f Unit(Vector3f v)
    {
        if (v.X == 0f && v.Y == 0f && v.Z == 0f)
        {
            return v;
        }

        Fpu length = Fpu.Sqrt((((Fpu)v.X * v.X) + ((Fpu)v.Y * v.Y)) + ((Fpu)v.Z * v.Z));
        Fpu inverse = (Fpu)1.0 / length.F;
        return new Vector3f(((Fpu)v.X * inverse).F, ((Fpu)v.Y * inverse).F, ((Fpu)v.Z * inverse).F);
    }

    /// <summary>
    /// Turns the pickup to lie along the ground it landed on (<c>igi2 0x46B4EA</c>): the ground's normal in its own frame gives a tilt
    /// and a turn, and its orientation becomes itself times that; a C4 bomb is turned by nothing.
    /// </summary>
    private void LieAlong(Vector3f n)
    {
        float[] o = Orientation;
        float lx = ((((Fpu)n.Y * o[3]) + ((Fpu)n.Z * o[6])) + ((Fpu)n.X * o[0])).F;
        float ly = ((((Fpu)n.Y * o[4]) + ((Fpu)n.Z * o[7])) + ((Fpu)n.X * o[1])).F;
        float lz = ((((Fpu)n.Y * o[5]) + ((Fpu)n.Z * o[8])) + ((Fpu)n.X * o[2])).F;
        float beta = 0f, gamma = 0f;
        if (!string.Equals(WeaponType.WeaponId, FlatWeaponId, StringComparison.Ordinal) && lx != 0f)
        {
            beta = (-(Fpu)Math.Atan(((Fpu)lz / lx).Value)).F;
            gamma = ((Fpu)Math.Atan(((Fpu)ly / lx).Value)).F;
        }

        float[] t = EngineEuler.Rotation(0f, beta, gamma);
        float[] turned = new float[9];
        for (int j = 0; j < 3; j++)
        {
            turned[j] = (((Fpu)t[6 + j] * o[2]) + ((Fpu)t[3 + j] * o[1]) + ((Fpu)t[j] * o[0])).F;
            turned[3 + j] = (((Fpu)t[6 + j] * o[5]) + ((Fpu)t[j] * o[3]) + ((Fpu)t[3 + j] * o[4])).F;
            turned[6 + j] = (((Fpu)t[6 + j] * o[8]) + ((Fpu)t[3 + j] * o[7]) + ((Fpu)t[j] * o[6])).F;
        }

        Array.Copy(turned, Orientation, 9);
    }

    /// <summary>
    /// Gets how far it reaches: its model's radius scaled by its largest scale, and never under a metre
    /// (<c>GunPickup_IsAbleToPickup</c>, <c>igi2 0x46BC80</c>).
    /// </summary>
    public float Reach
    {
        get
        {
            float larger = Scale.Y <= Scale.Z ? Scale.Z : Scale.Y;
            float largest = larger >= Scale.X ? larger : Scale.X;
            Fpu reach = (Fpu)largest * ModelRadius;
            return reach.Value < MinimumReach ? MinimumReach : reach.F;
        }
    }

    /// <summary>Whether a human with its eyes at a point may take it, <c>GunPickup_IsAbleToPickup</c> (<c>igi2 0x46BC80</c>).</summary>
    /// <param name="eyes">Where the human's eyes are.</param>
    /// <param name="tolerance">How much further across it may be, <c>vRadiusTolerance</c>.</param>
    /// <param name="sight">The level the sight to it is checked through.</param>
    /// <param name="materials">The session's materials, which the sight passes.</param>
    /// <returns><see langword="true"/> when it may be taken.</returns>
    /// <remarks>
    /// Not while it falls. The eyes must be within its reach across, and no more than its reach and two metres above or
    /// below it. And the solid models must leave clear the sight from the eyes to one of three points: the pickup's own
    /// position and its reach either way along its own Y axis, tried in that order from the near end.
    /// </remarks>
    public bool IsAbleToPickup(Vector3d eyes, float tolerance, IIgi2SightQuery sight, Igi2GameMaterials materials)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(materials);
        if (IsMoving && !IsLanded)
        {
            return false;
        }

        float reach = Reach;
        Fpu dx = (Fpu)Position.X - eyes.X, dy = (Fpu)Position.Y - eyes.Y, dz = (Fpu)Position.Z - eyes.Z;
        Fpu across = Fpu.Sqrt((dy * dy) + (dx * dx));
        if (((Fpu)tolerance + reach).Value <= across.Value
            || ((Fpu)(-HeightReach) - reach).Value >= dz.Value
            || ((Fpu)reach + HeightReach).Value <= dz.Value)
        {
            return false;
        }

        // Each point along the pickup's own Y axis: the matrix's second column times the offset, the zero products of
        // the other two summed after it, then the position added at 24 bits.
        float[] m = Orientation;
        foreach (float along in (ReadOnlySpan<float>)[-reach, 0f, reach])
        {
            Fpu ox = ((Fpu)m[1] * along) + ((Fpu)m[0] * 0f) + ((Fpu)m[2] * 0f);
            Fpu oy = ((Fpu)m[4] * along) + ((Fpu)m[3] * 0f) + ((Fpu)m[5] * 0f);
            Fpu oz = ((Fpu)m[7] * along) + ((Fpu)m[6] * 0f) + ((Fpu)m[8] * 0f);
            Vector3d point = new((ox + Position.X).Value, (oy + Position.Y).Value, (oz + Position.Z).Value);
            if (sight.LineOfSight(new WorldLineOfSight(eyes, point, materials), SightGroups))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Offers the pickup to a human, <c>GunPickup_PickupHandler</c> (<c>igi2 0x46C0A0</c>).
    /// </summary>
    /// <param name="offer">The human and how it takes pickups.</param>
    /// <param name="sight">The level the sight to the pickup is checked through.</param>
    /// <param name="materials">The session's materials.</param>
    /// <returns>
    /// <see langword="true"/> when the human may take it, <c>isAbleToPickUp</c>: within reach and in sight, and, for a
    /// thrown weapon, a medipack or a mine, not already carrying all the rounds of it that it may.
    /// </returns>
    /// <remarks>A human that takes pickups outright takes one it may (<see cref="Pickup"/>).</remarks>
    public bool Offer(in Igi2PickupOffer offer, IIgi2SightQuery sight, Igi2GameMaterials materials)
    {
        ArgumentNullException.ThrowIfNull(offer.Taker);
        if (!IsAbleToPickup(offer.Eyes, 0f, sight, materials))
        {
            return false;
        }

        if (Igi2WeaponCategory.IsCarriedAsRounds(WeaponType.WeaponCategory)
            && offer.Taker.Inventory is { } inventory
            && WeaponType.AmmoInfo(0, inventory.Tables).AmmoType is { } ammoType
            && inventory.FindAmmo(ammoType.AmmoId) is { } carried
            && carried.Count == carried.Limit)
        {
            return false;
        }

        if (offer.IsAutoPickup)
        {
            Pickup(offer.Taker);
        }

        return true;
    }

    /// <summary>
    /// Gives the human the weapon and marks the pickup for deleting, <c>GunPickup_Pickup</c> (<c>igi2 0x46C010</c>).
    /// </summary>
    /// <param name="taker">The human.</param>
    public void Pickup(IIgi2PickupTaker taker)
    {
        ArgumentNullException.ThrowIfNull(taker);
        taker.PickUpGun(new Igi2GunPickupGrant(WeaponType, AmmoInClip, [.. _ammoForFireType]));
        IsDelete = true;
    }
}
