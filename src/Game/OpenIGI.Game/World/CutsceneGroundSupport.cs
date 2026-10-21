using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>Finds what a vehicle stands on, and rests and keeps the collision-enabled helicopters on it.</summary>
/// <remarks>
/// The terrain half mirrors retail's <c>0x4CBE40</c> and the object sweep its placed-model sweep (<c>0x4CDB20</c> -&gt;
/// <c>0x4C7770</c>); the higher of the two is the surface. The airframe notes and the <c>Airframe.Path</c> samples are
/// written from here because they report the same support query.
/// </remarks>
internal sealed class CutsceneGroundSupport(CutsceneContext context)
{
    /// <summary>The narrow vertical band in which an airframe has touched supporting geometry.</summary>
    /// <remarks>
    /// Contact is resolved after the native-style integration step. This small band catches the exact
    /// rest case as well as the following tick's shallow penetration without affecting a genuinely
    /// airborne path.
    /// </remarks>
    private const double GroundContactToleranceUnits = 64.0;

    private readonly CutsceneContext _context = context;

    /// <summary>Measures a parked helicopter's rest height above the ground under its authored pose.</summary>
    /// <remarks>
    /// The authored pose of a landed helicopter sits its skids on the surface, so the distance from
    /// the pose down to the terrain is the model's own rest height. Returns <see cref="double.NaN"/>
    /// when the pose is not within the near-ground query's reach, meaning the helicopter is airborne
    /// and no rest height applies.
    /// </remarks>
    internal double MeasureGroundRestOffset(Vector3d authoredPosition, GameTask? ignoredOwner = null)
    {
        // Near-body only. A 2 km column would treat an airborne spawn as parked and
        // clamp it to the dirt for the whole flight (356 lift-off regression).
        if (!TryFindSupportHeight(
                authoredPosition,
                ignoredOwner,
                allowDeepRecovery: false,
                out double surfaceHeight))
        {
            return double.NaN;
        }

        // Never record a negative rest: the first successful query can happen after the body has
        // already crossed below the surface. A negative offset would clamp it even deeper and the
        // plane/heli would keep sinking. Belly-on-surface (0) is the safe floor.
        return Math.Max(0.0, authoredPosition.Z - surfaceHeight);
    }

    internal static double DefaultGroundRestOffset(HelicopterPhysicsDefinition physics) =>
        Math.Max(0.0, physics.Dimensions.Z * 0.5f * WorldScene.UnitsPerMetre);

    internal static bool IsPlausibleGroundRestOffset(
        double measured,
        HelicopterPhysicsDefinition physics) =>
        !double.IsNaN(measured)
        && measured <= Math.Max(
            4.0 * WorldScene.UnitsPerMetre,
            physics.Dimensions.Z * 2.0f * WorldScene.UnitsPerMetre);

    private static string FormatMetres(double units) =>
        double.IsNaN(units)
            ? "NaN"
            : (units / WorldScene.UnitsPerMetre).ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>
    /// Finds the highest supporting surface height under a point, preferring an elevated placed object (a
    /// pad, crate or rooftop) over lower terrain.
    /// </summary>
    /// <param name="position">The body position, in world units.</param>
    /// <param name="surfaceHeight">Receives the highest supporting surface height found.</param>
    /// <returns>
    /// <see langword="true"/> when at least one surface answers; <see langword="false"/> when neither the
    /// terrain nor the collision index reports a surface under the point.
    /// </returns>
    /// <remarks>
    /// Retail's terrain half (<c>0x4CBE40</c>) and placed-model sweep (<c>0x4CDB20</c> -&gt; <c>0x4C7770</c>)
    /// are two answers for the same question. The higher of the two is the surface the body actually rests
    /// on: a heli parked on a pad must rest on the pad's top, not the terrain below it. Both queries run and
    /// the higher answer wins, so an elevated collision surface takes precedence over terrain without losing
    /// the terrain answer when no object is present.
    /// </remarks>
    private bool TryFindSupportHeight(Vector3d position, out double surfaceHeight) =>
        TryFindSupportHeight(position, ignoredOwner: null, allowDeepRecovery: true, out surfaceHeight);

    private bool TryFindSupportHeight(
        Vector3d position,
        GameTask? ignoredOwner,
        bool allowDeepRecovery,
        out double surfaceHeight) =>
        TryFindSupportHeight(position, ignoredOwner, allowDeepRecovery, out surfaceHeight, out _);

    internal bool TryFindSupportHeight(
        Vector3d position,
        GameTask? ignoredOwner,
        bool allowDeepRecovery,
        out double surfaceHeight,
        out string supportSource)
    {
        double best = double.NegativeInfinity;
        bool found = false;
        bool objectFound = false;
        supportSource = "none";

        if (_context.Terrain is { IsUsable: true } terrain
            && TerrainHeightQuery.TryGetGround(
                terrain,
                position.X,
                position.Y,
                position.Z,
                TerrainHeightQuery.AboveRecoveryUnits,
                out TerrainGround ground))
        {
            best = ground.Height;
            found = true;
            supportSource = "terrain";
        }

        if (_context.CollisionIndex is not null)
        {
            Vector3d top = new(position.X, position.Y, position.Z + 4096.0);
            Vector3d bottom = new(position.X, position.Y, position.Z - 262144.0);
            if (TryCastObjectsIgnoringOwner(top, bottom, ignoredOwner, out WorldRayHit hit)
                && hit.Position.Z <= position.Z + TerrainHeightQuery.AboveRecoveryUnits
                && hit.Position.Z > best)
            {
                best = hit.Position.Z;
                found = true;
                objectFound = true;
                supportSource = "object";
            }
        }

        // A fast sink can leave the body more than AboveRecoveryUnits below the mesh. The
        // near-ground query only sees ~20 m around its probe Z, so a single sample 2 km above
        // the body never hits the surface they just punched through. Walk the column down in
        // 4 m steps (same window as TerrainHeightQuery.SearchDepthUnits) from well above the
        // body to just under it, and take the first (highest) surface.
        if (allowDeepRecovery && !found && _context.Terrain is { IsUsable: true } deepTerrain)
        {
            const double Step = 4.0 * WorldScene.UnitsPerMetre;
            const double High = 2_000.0 * WorldScene.UnitsPerMetre;
            for (double probeZ = position.Z + High; probeZ >= position.Z - Step; probeZ -= Step)
            {
                if (!TerrainHeightQuery.TryGetGround(
                        deepTerrain, position.X, position.Y, probeZ, out TerrainGround deep)
                    || deep.Height <= best)
                {
                    continue;
                }

                best = deep.Height;
                found = true;
                supportSource = "terrain-deep";
                break;
            }
        }

        // The body can be below an elevated pad/roof while the terrain query still answers below it.
        // In that case !found is false, but the one-metre near ray above the body has already missed
        // the object. Recover the object independently, then retain whichever valid surface is higher.
        if (allowDeepRecovery && !objectFound && _context.CollisionIndex is not null)
        {
            Vector3d highTop = new(
                position.X, position.Y, position.Z + (2_000.0 * WorldScene.UnitsPerMetre));
            Vector3d deepBottom = new(position.X, position.Y, position.Z - 262144.0);
            if (TryCastObjectsIgnoringOwner(highTop, deepBottom, ignoredOwner, out WorldRayHit deepHit)
                && deepHit.Position.Z > best)
            {
                best = deepHit.Position.Z;
                found = true;
                supportSource = "object-deep";
            }
        }

        surfaceHeight = best;
        return found;
    }

    private bool TryCastObjectsIgnoringOwner(
        Vector3d origin,
        Vector3d end,
        GameTask? ignoredOwner,
        out WorldRayHit hit)
    {
        hit = default;
        if (_context.CollisionIndex is null)
        {
            return false;
        }

        WorldTraceRequest request = new(
            origin,
            end,
            WorldTraceMask.StaticObjects | WorldTraceMask.DynamicObjects,
            IgnoredOwner: ignoredOwner);
        if (!_context.CollisionIndex.TryTrace(request, out WorldTraceHit traced))
        {
            return false;
        }

        hit = traced.RayHit;
        return true;
    }

    /// <summary>Keeps a helicopter from sinking through the terrain during contact recovery.</summary>
    /// <remarks>
    /// Retail's Heli collision flag prevents a descending body from penetrating a supporting surface;
    /// it does not replace the scripted collective or cyclic controls with a pad lock. Airborne
    /// helicopters are untouched while the query finds no nearby support. The landing-height search
    /// is widened a little above the body (see <see cref="TerrainHeightQuery.AboveRecoveryUnits"/>) so
    /// a body that dips under a ridge or a rising slope still finds the surface it just crossed and
    /// rides it back up, instead of falling forever once the downward-only search loses it.
    /// </remarks>
    internal void RestCollisionHelisOnSupport()
    {
        foreach ((GameTask task, CutsceneTransformState state) in _context.TaskTransforms)
        {
            if (state.CollisionEnabled && string.Equals(task.Type?.Name, "Heli", StringComparison.Ordinal))
            {
                RestHeliOnSupport(task, state);
            }
        }
    }

    private void RestHeliOnSupport(GameTask task, CutsceneTransformState state)
    {
        double measuredRest = MeasureGroundRestOffset(state.OriginPosition, task);
        if (state.HelicopterPhysics is { } physics)
        {
            bool plausible = IsPlausibleGroundRestOffset(measuredRest, physics);
            double bodyFloor = DefaultGroundRestOffset(physics);
            state.GroundRestOffset = plausible
                ? Math.Max(measuredRest, bodyFloor)
                : bodyFloor;
            if (!plausible)
            {
                _context.Note(
                    $"heli-rest-fallback id={task.Id} measured={measuredRest:R} "
                    + $"offset={state.GroundRestOffset:R}");
            }

            _context.Logger.Warning(
                "Airframe",
                $"rest id={task.Id} type=Heli "
                + $"measured={FormatMetres(measuredRest)} applied={FormatMetres(state.GroundRestOffset)} "
                + $"mode={(plausible && measuredRest >= bodyFloor ? "authored" : "physics-floor")}");
        }
        else
        {
            state.GroundRestOffset = measuredRest;
        }

        if (!TryGetLandingHeight(state.OriginPosition, state.GroundRestOffset, task, out double rest)
            || double.IsNaN(rest))
        {
            return;
        }

        state.Position = new Vector3d(state.Position.X, state.Position.Y, rest);
        _context.Note($"heli-park-rest id={task.Id} offset={state.GroundRestOffset:R}");
    }

    /// <summary>Holds a collision-enabled helicopter on the surface it has reached, and notes its contact and landing.</summary>
    internal void KeepHelicopterAboveGround(GameTask? ignoredOwner, CutsceneTransformState transform)
    {
        Vector3d position = transform.Position;

        // The rest offset is a body/pivot property, not the current distance to whatever surface a
        // ray happens to find. If the tick-zero authored pose was not near support, use the physics
        // body's stable half-height and never calibrate from an in-flight pose.
        if (double.IsNaN(transform.GroundRestOffset))
        {
            transform.GroundRestOffset = transform.HelicopterPhysics is { } physics
                ? DefaultGroundRestOffset(physics)
                : 0.0;
        }

        if (!TryGetLandingHeight(
                position,
                transform.GroundRestOffset,
                ignoredOwner,
                out double rest,
                out string supportSource)
            || double.IsNaN(rest))
        {
            transform.GroundContact = false;
            transform.GroundContactLogged = false;
            transform.GroundContactEventLogged = false;
            return;
        }

        if (position.Z > rest + (0.5 * WorldScene.UnitsPerMetre))
        {
            transform.GroundContact = false;
            transform.GroundContactLogged = false;
            transform.GroundContactEventLogged = false;
            return;
        }

        bool belowSupport = position.Z < rest;
        bool continuingContact = transform.GroundContact
            && position.Z <= rest + GroundContactToleranceUnits;
        if (!belowSupport && !continuingContact)
        {
            return;
        }

        bool firstContact = transform.CollisionEnabled && !transform.GroundContact;
        Vector3d incomingVelocity = transform.Velocity;
        float incomingSpeed = transform.Speed;
        transform.Position = new Vector3d(position.X, position.Y, rest);
        if (transform.CollisionEnabled)
        {
            transform.GroundContact = true;

            // A mission may explicitly mark a one-shot airframe. Its first terrain contact is terminal;
            // ordinary collision-enabled helicopters retain their authored controls after contact.
            if (transform.TerminalLandingOnTerrain
                && supportSource.StartsWith("terrain", StringComparison.Ordinal))
            {
                transform.TerminalLandingLatched = true;
                // Mission 5 replaces its incoming Heli with a parked Heli at the QVM-authored
                // touchdown pose once AnimTask 3333 crosses its terminal gate. Preserve the flown
                // approach, then use that authored replacement position for the terminal contact.
                transform.TerminalLandingPosition = transform.TerminalLandingTarget ?? transform.Position;
                transform.Position = transform.TerminalLandingPosition;
                transform.Thrust = 0f;
                transform.TargetThrust = 0f;
                transform.Velocity = default;
                transform.Speed = 0f;
                transform.AngularMomentum = default;
            }
            else if (belowSupport && transform.Velocity.Z < 0d)
            {
                transform.Velocity = new Vector3d(transform.Velocity.X, transform.Velocity.Y, 0d);
            }
        }

        _context.Note($"ground-clamp id={ignoredOwner?.Id ?? -1} z={position.Z:R} rest={rest:R}");

        if (firstContact && !transform.GroundContactEventLogged)
        {
            string type = ignoredOwner?.Type.Name ?? "Airframe";
            double offset = double.IsNaN(transform.GroundRestOffset)
                ? 0.0
                : transform.GroundRestOffset / WorldScene.UnitsPerMetre;
            _context.Logger.Warning(
                "Airframe",
                $"contact id={ignoredOwner?.Id ?? -1} type={type} tick={_context.SimulationTick} "
                + $"x={transform.Position.X / WorldScene.UnitsPerMetre:F2} "
                + $"y={transform.Position.Y / WorldScene.UnitsPerMetre:F2} "
                + $"z={transform.Position.Z / WorldScene.UnitsPerMetre:F2} "
                + $"support={(rest / WorldScene.UnitsPerMetre) - offset:F2} "
                + $"rest={transform.Position.Z / WorldScene.UnitsPerMetre:F2} offset={offset:F2} "
                + $"incomingV=({incomingVelocity.X / WorldScene.UnitsPerMetre:F3},"
                + $"{incomingVelocity.Y / WorldScene.UnitsPerMetre:F3},"
                + $"{incomingVelocity.Z / WorldScene.UnitsPerMetre:F3}) "
                + $"incomingSpeed={incomingSpeed:F3} target={transform.TargetThrust:F3} "
                + $"terminal={(transform.TargetThrust <= 0f && transform.Thrust <= 0f)} "
                + $"thrust={transform.Thrust:F3} target={transform.TargetThrust:F3}");
            transform.GroundContactEventLogged = true;
        }

        if (transform.TerminalLandingLatched && !transform.TerminalLandingEventLogged)
        {
            string type = ignoredOwner?.Type.Name ?? "Airframe";
            _context.Logger.Warning(
                "Airframe",
                $"landing-latch id={ignoredOwner?.Id ?? -1} type={type} tick={_context.SimulationTick} "
                + $"x={transform.Position.X / WorldScene.UnitsPerMetre:F2} "
                + $"y={transform.Position.Y / WorldScene.UnitsPerMetre:F2} "
                + $"z={transform.Position.Z / WorldScene.UnitsPerMetre:F2} "
                + $"source={supportSource} stoppedSpeed=0.000");
            _context.Note(
                $"heli-terminal-latch id={ignoredOwner?.Id ?? -1} source={supportSource} "
                + $"position={transform.TerminalLandingPosition}");
            transform.TerminalLandingEventLogged = true;
        }

        if (transform.GroundContact
            && transform.TargetThrust <= 0f
            && transform.Thrust <= 0f
            && !transform.GroundContactLogged)
        {
            string type = ignoredOwner?.Type.Name ?? "Airframe";
            double offset = double.IsNaN(transform.GroundRestOffset)
                ? 0.0
                : transform.GroundRestOffset / WorldScene.UnitsPerMetre;
            _context.Logger.Warning(
                "Airframe",
                $"landing id={ignoredOwner?.Id ?? -1} type={type} tick={_context.SimulationTick} "
                + $"x={transform.Position.X / WorldScene.UnitsPerMetre:F2} "
                + $"y={transform.Position.Y / WorldScene.UnitsPerMetre:F2} "
                + $"z={transform.Position.Z / WorldScene.UnitsPerMetre:F2} "
                + $"support={(rest / WorldScene.UnitsPerMetre) - offset:F2} "
                + $"rest={transform.Position.Z / WorldScene.UnitsPerMetre:F2} offset={offset:F2} "
                + $"incomingV=({incomingVelocity.X / WorldScene.UnitsPerMetre:F3},"
                + $"{incomingVelocity.Y / WorldScene.UnitsPerMetre:F3},"
                + $"{incomingVelocity.Z / WorldScene.UnitsPerMetre:F3}) "
                + $"incomingSpeed={incomingSpeed:F3} stoppedV=(0.000,0.000,0.000) stoppedSpeed=0.000 "
                + $"thrust={transform.Thrust:F3} target={transform.TargetThrust:F3}");
            transform.GroundContactLogged = true;
        }
        else if (!transform.GroundContact)
        {
            transform.GroundContactLogged = false;
        }
    }

    /// <summary>
    /// Samples the live Heli/Plane tick every fifteenth physics tick, at Trace in the <c>Airframe.Path</c>
    /// log category (<c>OPENIGI_LOG=Airframe.Path=trace</c>).
    /// </summary>
    internal void LogAirframePath(int id, string type, GameTask? target, CutsceneTransformState transform)
    {
        if ((transform.PhysicsTicks > 0 && transform.PhysicsTicks % 15 != 0)
            || !_context.Logger.IsEnabled(LogLevel.Trace, CutsceneRuntime.AirframePathLogCategory))
        {
            return;
        }

        double x = transform.Position.X / WorldScene.UnitsPerMetre;
        double y = transform.Position.Y / WorldScene.UnitsPerMetre;
        double z = transform.Position.Z / WorldScene.UnitsPerMetre;
        double ox = transform.OriginPosition.X / WorldScene.UnitsPerMetre;
        double oy = transform.OriginPosition.Y / WorldScene.UnitsPerMetre;
        double oz = transform.OriginPosition.Z / WorldScene.UnitsPerMetre;
        double dHoriz = Math.Sqrt(((x - ox) * (x - ox)) + ((y - oy) * (y - oy)));
        double dVert = z - oz;
        bool hasSupport = TryFindSupportHeight(
            transform.Position,
            target,
            allowDeepRecovery: true,
            out double supportHeight,
            out string supportSource);
        double support = hasSupport ? supportHeight / WorldScene.UnitsPerMetre : double.NaN;
        double offset = double.IsNaN(transform.GroundRestOffset)
            ? double.NaN
            : transform.GroundRestOffset / WorldScene.UnitsPerMetre;
        double rest = hasSupport && !double.IsNaN(offset) ? support + offset : double.NaN;
        double supportGap = hasSupport ? z - support : double.NaN;
        double restGap = !double.IsNaN(rest) ? z - rest : double.NaN;
        double vx = transform.Velocity.X / WorldScene.UnitsPerMetre;
        double vy = transform.Velocity.Y / WorldScene.UnitsPerMetre;
        double vz = transform.Velocity.Z / WorldScene.UnitsPerMetre;
        _context.Logger.Trace(
            CutsceneRuntime.AirframePathLogCategory,
            $"t={_context.SimulationTick} pt={transform.PhysicsTicks} id={id} type={type} "
            + $"x={x:F2} y={y:F2} z={z:F2} dHoriz={dHoriz:F2} dVert={dVert:F2} "
            + $"vx={vx:F3} vy={vy:F3} vz={vz:F3} "
            + $"thrust={transform.Thrust:F3} target={transform.TargetThrust:F3} col={transform.CollisionEnabled} "
            + $"offset={offset:F3} support={support:F3} source={supportSource} rest={rest:F3} "
            + $"supportGap={supportGap:F3} restGap={restGap:F3} "
            + $"latched={transform.TerminalLandingLatched} "
            + $"ctl=({transform.Control.X:F3},{transform.Control.Y:F3},{transform.Control.Z:F3})");
    }

    /// <summary>Finds the height a helicopter should rest at under a body position.</summary>
    /// <param name="position">The body's current position, in world units.</param>
    /// <param name="offset">The model's measured rest height above a supporting surface.</param>
    /// <param name="rest">Receives the height the body should rest at when one is found.</param>
    /// <returns>
    /// <see langword="true"/> with <paramref name="rest"/> when a supporting surface is under the
    /// point; <see langword="false"/> when nothing holds the body there.
    /// </returns>
    /// <remarks>
    /// The terrain query mirrors retail's terrain half (<c>0x4CBE40</c>) but not the placed-model sweep
    /// (<c>0x4CDB20</c> -&gt; <c>0x4C7770</c>), so a heli landing on a pad, crate or rooftop finds nothing
    /// and would sink straight through. When the terrain misses, cast the collision index down from
    /// well above the body and rest on the solid object the ray hits instead. A NaN rest offset from
    /// the terrain measurement does not disable the object fallback: a heli resting on a pad measures
    /// its rest height from the pad's surface below the body.
    /// </remarks>
    private bool TryGetLandingHeight(Vector3d position, double offset, out double rest) =>
        TryGetLandingHeight(position, offset, ignoredOwner: null, out rest);

    private bool TryGetLandingHeight(
        Vector3d position,
        double offset,
        GameTask? ignoredOwner,
        out double rest) =>
        TryGetLandingHeight(position, offset, ignoredOwner, out rest, out _);

    private bool TryGetLandingHeight(
        Vector3d position,
        double offset,
        GameTask? ignoredOwner,
        out double rest,
        out string supportSource)
    {
        // A NaN rest offset from the terrain measurement does not disable the object fallback: a heli resting
        // on a pad measures its rest height from the pad's surface below the body.
        double restOffset = double.IsNaN(offset) ? 0.0 : offset;

        // The terrain and collision queries share one support-selection rule, so a body on an elevated pad
        // rests on the pad rather than the lower terrain underneath it.
        if (!TryFindSupportHeight(
                position,
                ignoredOwner,
                allowDeepRecovery: true,
                out double surfaceHeight,
                out supportSource))
        {
            rest = 0.0;
            supportSource = "none";
            return false;
        }

        rest = surfaceHeight + restOffset;
        return true;
    }
}
