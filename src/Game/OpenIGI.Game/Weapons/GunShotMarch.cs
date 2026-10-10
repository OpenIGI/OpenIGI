using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Weapons;

/// <summary>One round in a gun's <c>GunShot</c> table: the 72-byte record <c>0x47B190</c> fills.</summary>
/// <remarks>
/// The round is a point and a direction with range left to spend. Nothing about it moves between ticks:
/// the march spends the whole range in the tick after the gun queued it, and the record is freed.
/// </remarks>
public sealed class GunShotRound
{
    /// <summary>Gets or sets where the round is, in world units (<c>+8</c>): the muzzle, then each step-off.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the unit direction it travels (<c>+32</c>), turned by a glance.</summary>
    public Vector3f Direction { get; set; }

    /// <summary>Gets the weapon that fired it (<c>+44</c>).</summary>
    public required WeaponType Weapon { get; init; }

    /// <summary>Gets or sets the range left, in world units (<c>+52</c>: the weapon's range x 4096 at the start).</summary>
    public float Remaining { get; set; }

    /// <summary>Gets the damage factor the hit message carries (<c>+56</c>, the weapon's <c>+1620</c>).</summary>
    public float DamageFactor { get; init; }

    /// <summary>Gets the penetration power a surface's resistance is taken from (<c>+60</c>, the weapon's <c>+1624</c>).</summary>
    public float PenetrationPower { get; init; }

    /// <summary>Gets or sets whether the round has glanced (<c>+64</c>); a round glances once.</summary>
    public bool Deflected { get; set; }

    /// <summary>Gets or sets whether the last surface's bite passed the round (<c>+65</c>).</summary>
    public bool Penetrated { get; set; }

    /// <summary>Gets the shooter's hit probability the cast's evaluate rolls against the player (<c>+68</c>).</summary>
    public float Probability { get; init; }

    /// <summary>Gets whoever fired it -- the cast context's owner, which the evaluate filters by side.</summary>
    public object? Shooter { get; init; }

    /// <summary>Gets where the round left the muzzle, for whoever hears it pass.</summary>
    public Vector3d Origin { get; init; }
}

/// <summary>One visible tracer a gun's <c>GunShot</c> task keeps: a 200-byte slot <c>0x47C4C0</c> fills.</summary>
/// <remarks>
/// <para>
/// A tracer is one PASS of a round's march, muzzle or step-off to contact or the range's end, filed when the
/// march draws one. It lives until the task's render has drawn it once: <c>0x47ABD0</c> zeroes its life after
/// the line, and <c>0x47C560</c> frees a zero life on the task's next tick. The thirty ticks it is filed with
/// only matter while nothing draws it.
/// </para>
/// <para>
/// The two distances are cumulative over the round's DRAWN passes only (the march's running total grows only
/// when it files one), and both are taken over the round's whole range, which is what the colour and the
/// brightness run along.
/// </para>
/// </remarks>
public sealed class GunShotTracer
{
    /// <summary>Gets where the pass started, in world units (<c>+0</c>).</summary>
    public required Vector3d Start { get; init; }

    /// <summary>Gets where the pass ended, in world units (<c>+24</c>): the contact, or the end of the range.</summary>
    public required Vector3d End { get; init; }

    /// <summary>Gets how far along the round's drawn passes the start lies, in world units (<c>+56</c>).</summary>
    public float StartDistance { get; init; }

    /// <summary>Gets how far along the round's drawn passes the end lies, in world units (<c>+60</c>).</summary>
    public float EndDistance { get; init; }

    /// <summary>Gets the round's whole range, in world units (<c>+64</c>).</summary>
    public float Range { get; init; }

    /// <summary>Gets the ammunition, whose two colours the line runs between.</summary>
    public required AmmoType Ammunition { get; init; }

    /// <summary>Gets whoever fired the round, so a presentation can find the gun it left.</summary>
    public object? Shooter { get; init; }

    /// <summary>Gets or sets the ticks left (<c>+52</c>); the render sets it to zero once it has drawn the line.</summary>
    public int Life { get; set; }
}

/// <summary>The nearest contact one pass of a round's cast returned: <c>0x4CC2A0</c>'s record.</summary>
/// <param name="Position">The contact point (<c>+8</c>).</param>
/// <param name="Normal">The struck face's normal (<c>+56</c>).</param>
/// <param name="Distance">How far along this pass's segment it lies (<c>+96</c>).</param>
/// <param name="Entry">The material the round enters here (<c>+76</c>), or <see langword="null"/> for an exit.</param>
/// <param name="Exit">The material it leaves through (<c>+80</c>), or <see langword="null"/>.</param>
/// <param name="HasObject">Whether the face belongs to an object (<c>+0</c>); the bare level shell has none.</param>
/// <param name="StruckHuman">Whether the object is a human, which throws no dust and no chips.</param>
/// <param name="Payload">What the caster needs to act on the contact later: the face, the body, the object.</param>
public readonly record struct GunShotContact(
    Vector3d Position,
    Vector3f Normal,
    double Distance,
    GameMaterial? Entry,
    GameMaterial? Exit,
    bool HasObject,
    bool StruckHuman,
    object? Payload);

/// <summary>One impact a march recorded, for the effect pass after it (<c>0x47C262</c>).</summary>
/// <param name="Contact">The contact.</param>
/// <param name="Effect">The block the struck material names for the side the round crossed.</param>
/// <param name="ExitCrossing">Whether the round was coming out of the surface.</param>
/// <param name="Vector">
/// The direction the effect is thrown along: the incoming segment reflected about the face for an entry, the
/// segment itself for an exit. The chips fly along it.
/// </param>
/// <param name="IsEntry">The entry flag the dispatch carries, which picks the mark's texture.</param>
public readonly record struct GunShotRecord(
    GunShotContact Contact,
    GunShotEffect Effect,
    bool ExitCrossing,
    Vector3f Vector,
    bool IsEntry);

/// <summary>What a round's march asks of the world it marches through.</summary>
public interface IGunShotWorld
{
    /// <summary>Casts one pass: the nearest contact between two points, through the evaluate's filter.</summary>
    /// <param name="round">The round, whose shooter and probability the evaluate reads.</param>
    /// <param name="start">The pass's start.</param>
    /// <param name="limit">The end of the range left.</param>
    /// <param name="contact">The contact, when there is one.</param>
    /// <returns>Whether the cast met anything.</returns>
    bool Cast(GunShotRound round, Vector3d start, Vector3d limit, out GunShotContact contact);

    /// <summary>Hands an entry contact to the struck object's gunshot handler (<c>0x47BA54</c>).</summary>
    /// <param name="round">The round.</param>
    /// <param name="contact">The contact; only one with an object reaches a handler.</param>
    /// <param name="travel">The pass's segment, start to end, as the hit message carries it.</param>
    void Strike(GunShotRound round, in GunShotContact contact, Vector3f travel);

    /// <summary>Plays a sound definition at a point, as <c>0x4E6C30</c> does. Draws nothing from the stream.</summary>
    /// <param name="definition">The definition's name; an empty or unknown one plays nothing.</param>
    /// <param name="position">Where, in world units.</param>
    void PlaySound(string definition, Vector3d position);

    /// <summary>Dispatches one recorded impact to its effect class (<c>0x4083C0</c>).</summary>
    /// <param name="round">The round.</param>
    /// <param name="record">The impact.</param>
    void Effect(GunShotRound round, in GunShotRecord record);

    /// <summary>The ammunition a weapon fires -- the gun's <c>+300</c>, looked up by calibre (<c>0x4789C0</c>).</summary>
    /// <param name="weapon">The weapon.</param>
    /// <returns>The ammunition, or <see langword="null"/> when the table has none for its calibre.</returns>
    AmmoType? AmmunitionOf(WeaponType weapon);
}

/// <summary>
/// The two global windows that keep a burst from stacking impact sounds: the struck material's sample and the
/// ricochet whizz, each a timestamp and a <c>rand(6)+6</c>-tick quiet (<c>0x540838</c>/<c>0x5BE3D8</c> and
/// <c>0x540834</c>/<c>0x5BE3D4</c>).
/// </summary>
/// <remarks>
/// Every gun's march shares them, the player's and the soldiers'. The clock is the level's tick counter
/// (<c>0x4028B0</c>, <c>Level+52</c>), and a window reopens only once MORE than its quiet has passed.
/// </remarks>
public sealed class GunShotSoundWindows
{
    /// <summary>The four whizzes <c>off_54083C</c> holds, picked by <c>rand() % 4</c>.</summary>
    public static readonly IReadOnlyList<string> RicochetSounds = BulletImpactSounds.RicochetSounds;

    private int _impactStamp = -1;
    private int _impactQuiet;
    private int _ricochetStamp = -1;
    private int _ricochetQuiet;

    /// <summary>Clears both windows, as a fresh run of the game starts them.</summary>
    public void Reset()
    {
        _impactStamp = -1;
        _impactQuiet = 0;
        _ricochetStamp = -1;
        _ricochetQuiet = 0;
    }

    /// <summary>Whether the material window is open at this tick.</summary>
    /// <param name="now">The level tick.</param>
    /// <returns>Whether an impact may sound.</returns>
    public bool ImpactOpen(int now) => IsOpen(_impactStamp, _impactQuiet, now);

    /// <summary>Closes the material window for its next <c>rand(6)+6</c> ticks (<c>0x47BAE1</c>, <c>0x47BAFB</c>).</summary>
    /// <param name="now">The level tick.</param>
    /// <param name="random">The level stream.</param>
    public void CloseImpact(int now, IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _impactStamp = now;
        _impactQuiet = (int)random.NextBounded(6) + 6;
    }

    /// <summary>The whizz, if its window is open -- <c>0x47C330</c>.</summary>
    /// <param name="now">The level tick.</param>
    /// <param name="random">The level stream.</param>
    /// <returns>The definition to play, or <see langword="null"/> while the window is shut.</returns>
    public string? TryRicochet(int now, IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!IsOpen(_ricochetStamp, _ricochetQuiet, now))
        {
            return null;
        }

        string sound = RicochetSounds[(int)random.NextBounded(4)];
        _ricochetStamp = now;
        _ricochetQuiet = (int)random.NextBounded(6) + 6;
        return sound;
    }

    private static bool IsOpen(int stamp, int quiet, int now) =>
        stamp == -1 || Math.Abs(now - stamp) > quiet;
}

/// <summary>
/// A gun's <c>GunShot</c> task: the rounds its fire handler queued, marched through the world in the task's
/// own place in the level walk (<c>0x47B690</c> to <c>0x47B750</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every gun builds one when it is created (<c>0x478540</c> to <c>0x47B110</c>) under the level's dynamic
/// container, so the tasks tick after the level's own objects, in the order the guns were made. A round fired
/// in a soldier's update is therefore marched later the same tick, after every soldier has moved.
/// </para>
/// <para>
/// A pass casts from the round to the end of its range and takes the nearest contact. Coming out of a surface
/// only latches the round, so the next thing it enters stops it. Going in, the struck object takes the hit,
/// the material's sample sounds (inside its window), and then the surface decides: a glance (drawn whenever
/// the round has not yet glanced, whatever the chance), or a bite of the round's power against the surface's
/// resistance (drawn only on a tie). The bare level shell stops every round it does not turn.
/// </para>
/// </remarks>
public sealed class GunShotTask
{
    /// <summary>The rounds one task holds (<c>0x47B19E</c>); a sixteenth is counted and dropped.</summary>
    public const int Capacity = GunShotBallistics.MaximumLiveShots;

    /// <summary>The impacts one round records for the effect pass (<c>v53 &lt; 10</c>).</summary>
    public const int RecordCapacity = GunShotBallistics.MaximumRecordedImpacts;

    /// <summary>How far past a contact the next pass starts, in world units.</summary>
    public const float StepOffUnits = GunShotBallistics.PenetrationStepOffUnits;

    /// <summary>The tracer slots one task holds (<c>0x47C4C0</c>'s loop over five 200-byte slots).</summary>
    public const int TracerCapacity = 5;

    private readonly List<GunShotRound> _rounds = [];
    private readonly List<GunShotRecord> _records = [];
    private readonly List<GunShotTracer> _tracers = [];

    // The passes left before the next one draws a tracer (+152): ten at creation (0x47B560), ten again after each.
    private int _tracerCountdown = GunShotBallistics.TracerInterval;

    /// <summary>
    /// Diagnostics: told of every tracer a march files, before it takes a slot -- so also of one that finds all five
    /// taken and is dropped, as every call of <c>0x47C4C0</c> is.
    /// </summary>
    public static Action<GunShotTask, GunShotTracer>? TracerFiled { get; set; }

    /// <summary>Diagnostics: told of every pass's cast -- the round, the segment, and the contact or none.</summary>
    public static Action<GunShotRound, Vector3d, Vector3d, float, GunShotContact?>? PassCast { get; set; }

    /// <summary>Gets the rounds waiting for the next march.</summary>
    public IReadOnlyList<GunShotRound> Rounds => _rounds;

    /// <summary>Gets the tracers waiting to be drawn, or drawn and waiting for the next tick to free them.</summary>
    public IReadOnlyList<GunShotTracer> Tracers => _tracers;

    /// <summary>Gets how many rounds were ever offered, kept or not (<c>+144</c>).</summary>
    public int Fired { get; private set; }

    /// <summary>Queues a round, as the gun's create message does (<c>0x47B190</c>).</summary>
    /// <param name="weapon">The weapon.</param>
    /// <param name="origin">The muzzle, in world units.</param>
    /// <param name="direction">The unit direction, after the cone.</param>
    /// <param name="probability">The shooter's hit probability.</param>
    /// <param name="shooter">The shooter.</param>
    /// <returns>Whether there was room for it.</returns>
    public bool Queue(WeaponType weapon, Vector3d origin, Vector3f direction, float probability, object? shooter)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        Fired++;
        if (_rounds.Count >= Capacity)
        {
            return false;
        }

        _rounds.Add(new GunShotRound
        {
            Weapon = weapon,
            Position = origin,
            Origin = origin,
            Direction = direction,
            Remaining = (float)(weapon.EffectiveRange * 4096.0),
            DamageFactor = weapon.DamageFactor,
            PenetrationPower = weapon.PenetrationPower,
            Probability = probability,
            Shooter = shooter,
        });
        return true;
    }

    /// <summary>Marches every queued round, then frees them -- the task's per-tick.</summary>
    /// <param name="now">The level tick, for the sound windows.</param>
    /// <param name="random">The level stream.</param>
    /// <param name="world">The world the rounds march through.</param>
    /// <param name="sounds">The shared sound windows.</param>
    public void Tick(int now, IgiRandom random, IGunShotWorld world, GunShotSoundWindows sounds)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sounds);

        // The table is walked in slot order; the rounds this task's gun fired since the last march.
        foreach (GunShotRound round in _rounds)
        {
            March(round, now, random, world, sounds);
        }

        _rounds.Clear();
        AgeTracers();
    }

    /// <summary>Frees the tracers the render has drawn and counts the rest down -- <c>0x47C560</c>.</summary>
    /// <remarks>
    /// The task's per-tick (<c>0x47B690</c>) runs this after the march whether or not there was anything to march,
    /// so a caller that skips an empty task's march still calls this. A drawn tracer's life is zero; one nothing
    /// drew is freed when its thirty ticks run out.
    /// </remarks>
    public void AgeTracers()
    {
        _tracers.RemoveAll(static tracer => tracer.Life == 0);
        foreach (GunShotTracer tracer in _tracers)
        {
            tracer.Life--;
        }
    }

    /// <summary>One round's march and its effect pass -- <c>0x47B750</c> for one slot.</summary>
    private void March(GunShotRound round, int now, IgiRandom random, IGunShotWorld world, GunShotSoundWindows sounds)
    {
        _records.Clear();
        bool latched = false;
        float remaining = round.Remaining;
        Vector3f direction = round.Direction;
        Vector3d position = round.Position;

        // The tracer reads the gun's ammunition (0x47C14D: its +300's flag at +32), and the drawn passes' running
        // distance starts at zero for each round.
        AmmoType? tracerAmmunition = world.AmmunitionOf(round.Weapon) is { DrawsTracer: true } drawn ? drawn : null;
        float drawnDistance = 0f;

        while (remaining > 0f)
        {
            bool penetrated = false;

            // The segment's end is the direction scaled by the range left, each product rounded to a float.
            float stepX = direction.X * remaining;
            float stepY = direction.Y * remaining;
            float stepZ = direction.Z * remaining;
            Vector3d start = position;
            Vector3d end = new(stepX + start.X, stepY + start.Y, stepZ + start.Z);

            bool met = world.Cast(round, start, end, out GunShotContact contact);
            PassCast?.Invoke(round, start, end, remaining, met ? contact : null);
            if (!met)
            {
                remaining = 0f;
                round.Penetrated = penetrated;
                FileTracer(tracerAmmunition, round, start, end, penetrated, ref drawnDistance);
                break;
            }

            Vector3f travel = new((float)(end.X - start.X), (float)(end.Y - start.Y), (float)(end.Z - start.Z));
            if (contact.Entry is null)
            {
                // Coming out of something: the far side is recorded and the round latched, with no handler, no
                // sound and no test of the surface.
                if (contact.Exit is { } leaving)
                {
                    if (_records.Count < RecordCapacity)
                    {
                        _records.Add(new GunShotRecord(contact, leaving.Exit, ExitCrossing: true, travel, IsEntry: false));
                    }

                    latched = true;
                }
            }
            else
            {
                GameMaterial entered = contact.Entry;
                if (contact.HasObject)
                {
                    world.Strike(round, contact, travel);
                }

                // 0x47BA62: the material's own sample, one of its six slots, then the whizz inside the same window.
                if (sounds.ImpactOpen(now))
                {
                    int slot = (int)random.NextBounded(6);
                    string sample = slot < entered.BulletSounds.Count ? entered.BulletSounds[slot] : string.Empty;
                    world.PlaySound(sample, contact.Position);
                    if (sounds.TryRicochet(now, random) is { } whizz)
                    {
                        world.PlaySound(whizz, contact.Position);
                    }

                    sounds.CloseImpact(now, random);
                }

                bool stops;
                if (latched)
                {
                    // A round that has come out of something stops in the next thing it enters.
                    stops = true;
                    latched = false;
                }
                else if (TryGlance(round, entered, contact, remaining, ref direction, stepX, stepY, stepZ, random))
                {
                    if (sounds.TryRicochet(now, random) is { } whizz)
                    {
                        world.PlaySound(whizz, contact.Position);
                    }

                    stops = false;
                }
                else
                {
                    // 0x47BD88: the bite. A tie is a 60% chance; a margin of a whole point passes; anything less
                    // stops it -- and only an object's face lets a passing round through.
                    double margin = round.PenetrationPower - (double)entered.BulletResistance;
                    bool passes = margin == 0.0
                        ? random.NextDouble() < 0.60000002
                        : margin >= 1.0;
                    penetrated = passes;
                    stops = !(passes && contact.HasObject);
                }

                if (stops)
                {
                    remaining = 0f;
                }

                if (_records.Count < RecordCapacity && !string.IsNullOrEmpty(entered.Entry.EntryTexture))
                {
                    _records.Add(new GunShotRecord(
                        contact,
                        entered.Entry,
                        ExitCrossing: false,
                        ReflectedSegment(contact.Normal, stepX, stepY, stepZ),
                        IsEntry: false));
                }
            }

            // 0x47C079: the range the contact spent, and the next pass from just past it.
            remaining = (float)(remaining - contact.Distance);
            float stepOffY = direction.Y * StepOffUnits;
            float stepOffZ = direction.Z * StepOffUnits;
            position = new Vector3d(
                (direction.X * (double)StepOffUnits) + contact.Position.X,
                stepOffY + contact.Position.Y,
                stepOffZ + contact.Position.Z);
            round.Penetrated = penetrated;
            FileTracer(tracerAmmunition, round, start, contact.Position, penetrated, ref drawnDistance);
        }

        round.Position = position;
        round.Direction = direction;
        round.Remaining = remaining;

        // 0x47C262: each record's entry flag, then its effect. A glanced round's are all exits; otherwise every
        // record but the last is an entry, and the last is one unless its face looks along its vector.
        for (int i = 0; i < _records.Count; i++)
        {
            GunShotRecord record = _records[i];
            bool isEntry;
            if (round.Deflected)
            {
                isEntry = false;
            }
            else if (i < _records.Count - 1)
            {
                isEntry = true;
            }
            else
            {
                Vector3f n = record.Contact.Normal;
                Vector3f v = record.Vector;
                isEntry = !((n.X * v.X) + (n.Y * v.Y) + (n.Z * v.Z) > 0f);
            }

            world.Effect(round, record with { IsEntry = isEntry });
        }

        _records.Clear();
    }

    // 0x47C13A: after every pass, whether it is drawn. A deflected round, or a pass the surface's bite let through,
    // draws every time; otherwise one pass in eleven -- the counter draws at zero, and filing one puts it back at
    // ten. A bite that passes the bare level shell still stops the round, and still draws.
    private void FileTracer(
        AmmoType? ammunition,
        GunShotRound round,
        Vector3d start,
        Vector3d end,
        bool penetrated,
        ref float drawnDistance)
    {
        if (ammunition is null)
        {
            return;
        }

        if (_tracerCountdown > 0 && !round.Deflected && !penetrated)
        {
            _tracerCountdown--;
            return;
        }

        // 0x47C17E: the pass's length, the X and Y differences narrowed to floats first.
        float dx = (float)(start.X - end.X);
        float dy = (float)(start.Y - end.Y);
        double dz = start.Z - end.Z;
        double length = Math.Sqrt((dx * (double)dx) + (dy * (double)dy) + (dz * dz));
        if (!(length > 0.0))
        {
            return;
        }

        _tracerCountdown = GunShotBallistics.TracerInterval;
        float total = (float)(length + drawnDistance);

        // 0x47C4C0: the first free slot of five; with none free the pass is not drawn, though the total still runs.
        GunShotTracer tracer = new()
        {
            Start = start,
            End = end,
            StartDistance = drawnDistance,
            EndDistance = total,
            Range = round.Remaining,
            Ammunition = ammunition,
            Shooter = round.Shooter,
            Life = GunShotBallistics.TracerLifetimeTicks,
        };
        TracerFiled?.Invoke(this, tracer);
        if (_tracers.Count < TracerCapacity)
        {
            _tracers.Add(tracer);
        }

        drawnDistance = total;
    }

    // 0x47BB36: the graze. The chance is how far inside the material's ricochet angle the round arrived; the
    // draw is made whenever the round has not glanced yet, even when the chance is nothing.
    private static bool TryGlance(
        GunShotRound round,
        GameMaterial entered,
        in GunShotContact contact,
        float remaining,
        ref Vector3f direction,
        float stepX,
        float stepY,
        float stepZ,
        IgiRandom random)
    {
        Vector3f back = new(-stepX, -stepY, -stepZ);
        float incidence = AngleBetween(back, contact.Normal);
        float chance = entered.RicochetAngleDegrees == 0f
            ? 0f
            : (float)(((entered.RicochetAngleDegrees * 0.017453292) - (1.5707964 - incidence))
                / (entered.RicochetAngleDegrees * 0.017453292));

        if (round.Deflected || !(random.NextDouble() < chance))
        {
            return false;
        }

        Vector3f normal = Normalized(contact.Normal);
        float along = (float)(Math.Cos(incidence) * remaining);
        float px = along * normal.X;
        float py = along * normal.Y;
        float pz = along * normal.Z;
        direction = Normalized(new Vector3f(px - back.X + px, py - back.Y + py, pz - back.Z + pz));
        round.Deflected = true;
        return true;
    }

    // 0x47BEA4: the incoming segment (start minus end) reflected about the face's unit normal.
    private static Vector3f ReflectedSegment(Vector3f faceNormal, float stepX, float stepY, float stepZ)
    {
        Vector3f n = Normalized(faceNormal);
        float bx = -stepX;
        float by = -stepY;
        float bz = -stepZ;
        float dot = (bz * n.Z) + (by * n.Y) + (bx * n.X);
        return new Vector3f(
            (dot * n.X) + (dot * n.X) - bx,
            (dot * n.Y) + (dot * n.Y) - by,
            (dot * n.Z) + (dot * n.Z) - bz);
    }

    // 0x4B2B30: the angle between two vectors.
    private static float AngleBetween(Vector3f a, Vector3f b)
    {
        double la = Math.Sqrt((a.X * (double)a.X) + (a.Y * (double)a.Y) + (a.Z * (double)a.Z));
        double lb = Math.Sqrt((b.X * (double)b.X) + (b.Y * (double)b.Y) + (b.Z * (double)b.Z));
        if (la <= 0.0 || lb <= 0.0)
        {
            return 0f;
        }

        double dot = ((a.X * (double)b.X) + (a.Y * (double)b.Y) + (a.Z * (double)b.Z)) / (la * lb);
        return (float)Math.Acos(Math.Clamp(dot, -1.0, 1.0));
    }

    private static Vector3f Normalized(Vector3f v)
    {
        if (v.X == 0f && v.Y == 0f && v.Z == 0f)
        {
            return v;
        }

        float inverse = (float)(1.0 / Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z)));
        return new Vector3f(v.X * inverse, v.Y * inverse, v.Z * inverse);
    }
}
