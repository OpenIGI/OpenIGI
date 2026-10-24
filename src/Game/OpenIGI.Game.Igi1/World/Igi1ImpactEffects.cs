using OpenIGI.Core.Graphics;
using OpenIGI.Game.Hosting;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Particles;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Project I.G.I.'s own impact effects as they move: <c>concrete1.spr</c> dust and the material's chips where a round lands,
/// and the blood a wound throws, whose droplets mark what they land on.
/// </summary>
/// <remarks>The title's impact presenter draws <see cref="Runtime"/>; this steps it and feeds it on the fixed tick.</remarks>
/// <param name="mission">The mission, whose player's marks and walls the effects use.</param>
/// <param name="host">Whether the player wants blood.</param>
public sealed class Igi1ImpactEffects(MissionWorld mission, IImpactEffectsHost host) : IMissionImpacts
{
    /// <summary>The missile's scorch half-width: <c>0x476AC0</c> hands <c>Explosion0</c> 6144.</summary>
    public const float MissileScorchHalfWidthInUnits = 6144f;

    /// <summary>The M203 round's scorch half-width: <c>0x47647B</c> hands <c>Explosion0</c> 4096.</summary>
    public const float M203ScorchHalfWidthInUnits = 4096f;

    private Func<Camera3D> _eye = static () => default;

    /// <summary>Gets the effects as they move: the dust and chips, and the blood.</summary>
    public ImpactEffectRuntime Runtime { get; } = new();

    // What a droplet or a dust puff stops against.
    private WorldRayCast? Walls => mission.EffectWalls ?? mission.Player?.Body.Walls;

    /// <summary>Empties the effects, and tells them how many frames the blood mist has.</summary>
    /// <param name="mistFrames">The mist's frame count.</param>
    public void Reset(int mistFrames)
    {
        Runtime.Clear();
        Runtime.SetMistFrameCount(mistFrames);
    }

    /// <summary>Lets the level's soldiers bleed: a round or a blade that meets a body throws its blood here.</summary>
    public void ConnectSoldiers()
    {
        // A round -- or a blade -- that meets a body bleeds before it does anything else (0x45F94A),
        // whoever struck it. The splash it throws onto the wall behind (0x463AD0) is the only mark a
        // knife leaves: KnifeStab itself never reaches the decal message.
        if (mission.SoldierPresenter is { } soldiers)
        {
            soldiers.BloodSpray = (owner, at, direction) => SpawnBlood(owner, at, direction);
        }
    }

    /// <summary>Burns the retail <c>burn1.tex</c> scorch onto the object a blast struck.</summary>
    /// <param name="positionUnits">The impact point, in world units.</param>
    /// <param name="normal">The struck surface's normal; up when the impact recorded none.</param>
    /// <param name="halfWidthInUnits">The scorch's half-width, as the launcher passes it.</param>
    /// <remarks>
    /// <c>Explosion0</c> (<c>0x4811C9</c>) sends <c>burn1.tex</c> (<c>dword_5C1164</c>) to the struck object through the same
    /// decal message a bullet hole uses, so it lands only on an object that takes marks: never on bare terrain, never on a
    /// body. Here it joins the world's decals, drawn by the bullet-mark renderer, so it follows the same
    /// <c>r_bullet_marks</c> switch.
    /// </remarks>
    public void Scorch(Vector3d positionUnits, Vector3f normal, float halfWidthInUnits)
    {
        if (mission.Player?.Marks is not { } marks)
        {
            return;
        }

        Vector3f n = normal;
        float length = MathF.Sqrt((n.X * n.X) + (n.Y * n.Y) + (n.Z * n.Z));
        n = length < 1e-4f ? Vector3f.UnitZ : n * (1f / length);

        // Lift the scorch clear of the surface so it does not fight it for depth.
        const float liftUnits = 20.48f;
        Vector3d at = new(
            positionUnits.X + (n.X * liftUnits),
            positionUnits.Y + (n.Y * liftUnits),
            positionUnits.Z + (n.Z * liftUnits));

        if (MarkSurfaceAt(positionUnits, n, out WorldPlacement? placement) is not { } surface)
        {
            return;
        }

        // The name carries its .tex so the bullet-mark renderer's common-textures candidate matches the exact entry
        // burn1.tex is registered under (common textures, not the level's).
        marks.AddMark(at, n, "burn1.tex", halfWidthInUnits, surface, placement);
    }

    /// <summary>Runs one marched round's <c>GunShotEffect</c> where it met something, if the player wants impact effects drawn.</summary>
    /// <param name="position">Where it met, in world units.</param>
    /// <param name="normal">The surface's normal there.</param>
    /// <param name="vector">The round's travel.</param>
    /// <param name="effect">The material's effect block.</param>
    /// <param name="struckHuman">Whether it met a body.</param>
    public void RunGunShotEffect(Vector3d position, Vector3f normal, Vector3f vector, in GunShotEffect effect, bool struckHuman) =>
        Runtime.RunGunShotEffect(position, normal, vector, effect, struckHuman, host.ImpactEffectsWanted);

    /// <summary>Runs the struck material's own <c>GunShotEffect</c> for whatever the player's rounds landed on this tick.</summary>
    public void SpawnPlayerImpacts()
    {
        if (mission.Player is { } player)
        {
            SpawnSurfaceImpacts(player.Impacts);
        }
    }

    /// <summary>Steps the original's impact effects, and lets its blood mark what it lands on.</summary>
    /// <remarks>
    /// The droplet traces are the reason this needs the world: <c>0x4640B0</c> is a collision callback, so where a droplet
    /// stops is where the splash is stamped. The same <see cref="BulletMarks"/> the bullet holes go into takes them, which
    /// is what makes them fade out of the world together.
    /// </remarks>
    public void Tick()
    {
        if (mission.Player is null)
        {
            return;
        }

        Runtime.BloodEnabled = host.ImpactEffectsWanted && host.BloodWanted;
        Runtime.Tick(mission.DropletWalls ?? Walls, mission.Player?.Marks, mission.Materials);
    }


    /// <summary>Runs the struck material's own <c>GunShotEffect</c> for whatever landed this tick.</summary>
    /// <param name="landed">This tick's impacts: the instant rays and the ballistic arrivals alike.</param>
    /// <remarks>
    /// <para>
    /// A contact that met a human is deliberately skipped: <c>0x438458</c> asks the struck object whether it is a Human
    /// first and drops both the dust and the chips when it is -- a body throws blood instead, which the soldier and player
    /// damage paths raise directly.
    /// </para>
    /// <para>
    /// The <b>entry</b> block is the one consulted, because that is the block the shot march (<c>0x47B750</c>) dispatches
    /// for a hit on world geometry; the second block, which every retail row leaves flagless, is for the far side and
    /// produces only its mark.
    /// </para>
    /// </remarks>
    public void SpawnSurfaceImpacts(IEnumerable<GunImpact> landed)
    {
        ArgumentNullException.ThrowIfNull(landed);
        if (mission.Materials is not { } materials || !host.ImpactEffectsWanted)
        {
            return;
        }

        foreach (GunImpact impact in landed)
        {
            // A knife records no impact (0x46DFB0), so it never reaches GunShotEffect's dust.
            if (!impact.HitSomething || impact.StruckHuman || impact.Melee)
            {
                continue;
            }

            if (!materials.TryGet(impact.Wall.Material, out GameMaterial? material) || material is null || !material.RecordsImpacts)
            {
                continue;
            }

            Runtime.SpawnSurfaceImpact(impact.Wall.Position, impact.Wall.Normal, impact.Direction, material.Entry, struckHuman: false);
        }
    }

    /// <summary>Throws one wound's blood, as <c>Human</c>'s damage handler does before anything else.</summary>
    /// <param name="owner">The body -- its two systems are kept against this and reused.</param>
    /// <param name="position">Where the round entered, in world units.</param>
    /// <param name="direction">The round's travel direction.</param>
    /// <returns>What the blood met behind the wound.</returns>
    /// <remarks>
    /// <c>0x45F94A</c> calls the blood <b>first</b>, before the invulnerability and instant-death gates and before any damage
    /// is applied, so a round that does nothing to a body still makes it bleed. Callers therefore raise this on contact,
    /// not on damage.
    /// </remarks>
    public BloodReport SpawnBlood(object owner, Vector3d position, Vector3f direction)
    {
        if (mission.Player is null || !host.ImpactEffectsWanted)
        {
            return default;
        }

        Runtime.BloodEnabled = host.BloodWanted;
        return Runtime.SpawnBlood(owner, position, direction, Walls, mission.Player?.Marks, mission.Materials);
    }


    /// <summary>Registers the commands that fire the original's impact effects by hand.</summary>
    /// <param name="console">The console to register with.</param>
    /// <param name="eye">The camera the frame is drawn from, which the commands aim with.</param>
    /// <param name="logger">Where the commands report.</param>
    /// <remarks>
    /// Diagnosis, and a capture handle: a headless run can put a dust puff, a spray of chips or a wound exactly where the
    /// crosshair points and photograph it, which firing a weapon on a synthetic clock cannot do reproducibly.
    /// </remarks>
    public void Register(ConsoleSystem console, Func<Camera3D> eye, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(eye);
        ArgumentNullException.ThrowIfNull(logger);
        _eye = eye;
        console.Commands.Add(
            "impact_test",
            "Runs the struck material's own GunShotEffect where the crosshair points.",
            context =>
            {
                if (!TryAimedSurface(out WorldRayHit hit, out Vector3f look))
                {
                    context.Warn("Nothing under the crosshair, or no level is loaded.");
                    logger.Warning("Impact", "impact_test: nothing under the crosshair.");
                    return;
                }

                if (mission.Materials is not { } materials
                    || !materials.TryGet(hit.Material, out GameMaterial? material)
                    || material is null)
                {
                    context.Warn($"Material row {hit.Material} is not in the table.");
                    logger.Warning("Impact", $"impact_test: material row {hit.Material} is not in the table.");
                    return;
                }

                if (!material.RecordsImpacts)
                {
                    context.Print($"{material.Name} records no impact at all, so nothing is drawn.");
                    logger.Information("Impact", $"impact_test: {material.Name} records no impact.");
                    return;
                }

                mission.Player?.Marks?.TryAdd(
                    hit,
                    MarkSurfaceAt(hit.Position, hit.Normal, out WorldPlacement? struck),
                    materials,
                    probe: mission.Player?.MarkProbe,
                    placement: struck);
                Runtime.SpawnSurfaceImpact(hit.Position, hit.Normal, look, material.Entry, struckHuman: false);
                string where = FormattableString.Invariant($"{hit.Distance / WorldScene.UnitsPerMetre:0.#} m");
                string report =
                    $"{material.Name} at {where}: {Runtime.PuffCount} dust quad(s), "
                        + $"{Runtime.Fragments.Count} chip(s) of {material.Entry.Model}.";
                context.Print(report);
                logger.Information("Impact", report);
            });

        console.Commands.Add(
            "blood_test",
            "Throws a wound's blood where the crosshair points, as a round meeting a body does.",
            context =>
            {
                Camera3D eye = _eye();
                Vector3f look = eye.Orientation.Row3;
                Vector3d wound;

                // An explicit range puts the wound that many metres down the line of sight instead of at whatever the
                // crosshair reaches, so a capture can hold the spray at arm's length.
                if (context.Count > 0
                    && float.TryParse(
                        context[0],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float metres))
                {
                    double reach = metres * WorldScene.UnitsPerMetre;
                    Vector3d origin = eye.Position * WorldScene.UnitsPerMetre;
                    wound = new Vector3d(
                        origin.X + (look.X * reach),
                        origin.Y + (look.Y * reach),
                        origin.Z + (look.Z * reach));
                }
                else
                {
                    if (!TryAimedSurface(out WorldRayHit hit, out look))
                    {
                        context.Warn("Nothing under the crosshair, or no level is loaded.");
                        logger.Warning("Impact", "blood_test: nothing under the crosshair.");
                        return;
                    }

                    // A metre back down the line of sight, so the spray has the three metres of room the backstop
                    // trace wants and lands on the surface being aimed at.
                    wound = new Vector3d(
                        hit.Position.X - (look.X * WorldScene.UnitsPerMetre),
                        hit.Position.Y - (look.Y * WorldScene.UnitsPerMetre),
                        hit.Position.Z - (look.Z * WorldScene.UnitsPerMetre));
                }

                BloodReport blood = SpawnBlood(context, wound, look);
                string backstop = blood.BackstopHit
                    ? string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"backstop {(mission.Materials is { } rows && rows.TryGet(blood.BackstopMaterial, out GameMaterial? row) && row is not null ? row.Name : blood.BackstopMaterial.ToString(System.Globalization.CultureInfo.InvariantCulture))}"
                            + $" at {blood.BackstopDistanceUnits / WorldScene.UnitsPerMetre:0.##} m, "
                            + $"{(blood.BackstopMarked ? "splashed" : "no splash -- that row marks nothing")}")
                    : "nothing within three metres behind the wound";
                int droplets = 0;
                foreach (Particle2DBleeding systems in Runtime.Blood.Values)
                {
                    droplets += systems.Drops?.Count ?? 0;
                }

                string report = Runtime.BloodEnabled
                    ? $"{droplets} droplet(s) in the air from {Runtime.Blood.Count} body(ies), "
                        + $"{mission.Player?.Marks?.TotalAdded ?? 0} mark(s) recorded; {backstop}."
                    : "Blood is switched off, in the game options or by r_impact_effects.";
                context.Print(report);
                logger.Information("Impact", report);
            });
    }


    // Casts the frame's own eye at the world, for the impact commands.
    private bool TryAimedSurface(out WorldRayHit hit, out Vector3f look)
    {
        hit = default;
        Camera3D eye = _eye();
        Vector3f forward = eye.Orientation.Row3;
        look = forward;

        if (Walls is not { } walls)
        {
            return false;
        }

        Vector3d origin = eye.Position * WorldScene.UnitsPerMetre;
        Vector3d end = origin + (new Vector3d(forward.X, forward.Y, forward.Z) * (60.0 * WorldScene.UnitsPerMetre));
        return walls(origin, end, out hit);
    }

    // Names the object whose surface lies at a point, for a mark to be stuck to: null for terrain, nothing, or a type that
    // takes no marks.
    private object? MarkSurfaceAt(Vector3d positionUnits, Vector3f normal, out WorldPlacement? placement)
    {
        placement = null;
        if (mission.Trace is not { } trace)
        {
            return null;
        }

        const double reach = 64.0;
        Vector3d from = new(
            positionUnits.X + (normal.X * reach),
            positionUnits.Y + (normal.Y * reach),
            positionUnits.Z + (normal.Z * reach));
        Vector3d to = new(
            positionUnits.X - (normal.X * reach),
            positionUnits.Y - (normal.Y * reach),
            positionUnits.Z - (normal.Z * reach));

        if (!trace(new WorldTraceRequest(from, to, WorldTraceMask.World), out WorldTraceHit hit))
        {
            return null;
        }

        placement = hit.Placement;
        return BulletMarks.SurfaceOf(hit);
    }
}
