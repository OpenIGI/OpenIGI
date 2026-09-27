using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Retail-backed checks for the exact identity of glass and explosive world objects.</summary>
public sealed class DestructibleObjectTests
{
    [RequiresGameDataFact]
    public void LevelOneLoadsGlassAttachmentsAndExplosiveTasks()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        Assert.Contains(registry.Objects, item => item.Kind == DestructibleObjectKind.Glass);
        Assert.Contains(registry.Objects, item => item.Kind == DestructibleObjectKind.Explosive);
        Assert.All(
            registry.Objects.Where(item => item.Kind == DestructibleObjectKind.Glass),
            item => Assert.Equal("TASKTYPE_GLASS", item.TypeName));

        Assert.All(
            registry.Objects.Where(item => item.Kind == DestructibleObjectKind.Glass),
            item => Assert.False(item.HasExplosionSmoke));
        Assert.Contains(
            registry.Objects,
            item => item.Kind == DestructibleObjectKind.Explosive && item.HasExplosionSmoke);
        Assert.Contains(
            registry.Objects,
            item => item.Kind == DestructibleObjectKind.Explosive
                && item.Placement is not null
                && item.Placement.IsAttached
                && !item.HasExplosionSmoke);
        Assert.Contains(
            registry.Objects,
            item => item.TypeName == "TASKTYPE_EXPLODEMAGICOBJ"
                && item.ExplosionSound.Length > 0);
    }

    [RequiresGameDataFact]
    public void RetailExplosionUsesTheAuthoredCommonFireballModel()
    {
        LoadedMission mission = RetailLevelOneGround.Mission();
        Assert.True(mission.CommonPack.TryGetModel("1009_01_1", out _));
        Assert.True(mission.CommonPack.TryGetModel("1006_01_1", out _));

        MtpInstance instance = mission.CommonPack.Instances.First(item =>
            string.Equals(item.Name, "1009_01_1", StringComparison.OrdinalIgnoreCase));
        Assert.True(mission.CommonPack.TryGetModel(instance, out _));
        Assert.True(mission.CommonPack.TryGetTextureName(instance, 0, out string texture));
        Assert.Equal("1009_01_1", texture);
    }

    [RequiresGameDataFact]
    public void DestroyingOneAttachedPaneDoesNotDestroyItsBuildingSiblings()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        DestructibleObject first = registry.Objects
            .Where(item => item.Kind == DestructibleObjectKind.Glass)
            .GroupBy(item => item.Owner)
            .Where(group => group.Count() > 1)
            .Select(group => group.First())
            .First();
        DestructibleObject sibling = registry.Objects
            .Where(item => item.Kind == DestructibleObjectKind.Glass && ReferenceEquals(item.Owner, first.Owner))
            .Skip(1)
            .First();

        Assert.NotNull(first.Placement);
        Assert.NotNull(sibling.Placement);
        Assert.Same(first, registry.TryDestroy(first.Owner, first.Placement, first.ExplosionDamageThreshold));
        Assert.True(registry.IsDestroyed(first.Placement!));
        Assert.False(registry.IsDestroyed(sibling.Placement!));
    }

    [RequiresGameDataFact]
    public void ARoundOnTheBuildingWallBreaksNoneOfItsPanes()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        // The round names the Building and its wall; the panes the Building owns are other objects.
        DestructibleObject pane = registry.Objects.First(item =>
            item.Kind == DestructibleObjectKind.Glass
            && scene.Placements.Any(placement =>
                ReferenceEquals(placement.Owner, item.Owner) && placement.MagicTaskTypeName.Length == 0));
        WorldPlacement wall = scene.Placements.First(placement =>
            ReferenceEquals(placement.Owner, pane.Owner) && placement.MagicTaskTypeName.Length == 0);

        Assert.Null(registry.TryDestroy(pane.Owner, wall));
        Assert.False(pane.IsDestroyed);
    }

    [RequiresGameDataFact]
    public void ARoundThatNamesNoObjectBreaksNothing()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        // A round into a guard or the terrain carries no identity. Retail's trace (0x47B750) only
        // messages the struck task, so the pane beside that contact stays whole.
        DestructibleObject pane = registry.Objects.First(item => item.Kind == DestructibleObjectKind.Glass);

        Assert.Null(registry.TryDestroy(owner: null, placement: null));
        Assert.False(pane.IsDestroyed);
    }

    [RequiresGameDataFact]
    public void APlacementUnderAnotherOwnerIsNotThatPane()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        DestructibleObject pane = registry.Objects.First(item => item.Kind == DestructibleObjectKind.Glass);
        GameTask stranger = registry.Objects.First(item => !ReferenceEquals(item.Owner, pane.Owner)).Owner;

        Assert.Null(registry.TryDestroy(stranger, pane.Placement));
        Assert.False(pane.IsDestroyed);
        Assert.Same(pane, registry.TryDestroy(pane.Owner, pane.Placement, pane.ExplosionDamageThreshold));
    }

    [RequiresGameDataFact]
    public void EveryPaneDrawsItsOwnBreakThreshold()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        // Glass init (0x4497F0): 0.75 + rand * 0.75, rand in [0, 1).
        DestructibleObject[] panes = [.. registry.Objects.Where(item => item.Kind == DestructibleObjectKind.Glass)];
        Assert.All(panes, pane =>
        {
            Assert.InRange(pane.ExplosionDamageThreshold, 0.75f, 1.5f);
            Assert.True(pane.ExplosionDamageThreshold < 1.5f);
            Assert.Equal(0f, pane.ExplosionDamageAccumulated);
        });
        Assert.True(panes.Select(pane => pane.ExplosionDamageThreshold).Distinct().Count() > 1);
    }

    [RequiresGameDataFact]
    public void RoundsWearAPaneDownUntilTheSumReachesItsThreshold()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        // Glass_BulletHitHandler (0x449B00): +0x13C += the round's damage factor, and the pane only
        // shatters once that reaches +0x140. A Glock round (0.25) therefore takes three to six.
        const float glock = 0.25f;
        DestructibleObject pane = registry.Objects.First(item => item.Kind == DestructibleObjectKind.Glass);
        int hits = (int)MathF.Ceiling(pane.ExplosionDamageThreshold / glock);
        Assert.InRange(hits, 3, 6);

        for (int hit = 1; hit < hits; hit++)
        {
            Assert.Null(registry.TryDestroy(pane.Owner, pane.Placement, glock));
            Assert.False(pane.IsDestroyed);
        }

        Assert.Same(pane, registry.TryDestroy(pane.Owner, pane.Placement, glock));
        Assert.True(pane.IsDestroyed);
        Assert.Null(registry.TryDestroy(pane.Owner, pane.Placement, glock));
    }

    [RequiresGameDataFact]
    public void AnyBlastContactShattersAPaneWhateverItsThreshold()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        // Glass_ExplosionHitHandler (0x449BA0) tests the dose against zero, not the sum against the
        // threshold, so even a grazing contact breaks the pane.
        DestructibleObject pane = registry.Objects.First(item => item.Kind == DestructibleObjectKind.Glass);
        IReadOnlyList<DestructibleObject> affected = registry.ApplyExplosionHits(
            source: null,
            pane.PositionUnits,
            radiusUnits: 1f,
            damageFactor: 0.01f,
            canContact: candidate => ReferenceEquals(candidate, pane));

        Assert.Contains(pane, affected);
        Assert.True(pane.IsDestroyed);
    }

    [RequiresGameDataFact]
    public void ExplodeObjectKeepsRetailFireballFragmentAndSmokeAuthorship()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        DestructibleObject explosive = registry.Objects
            .First(item => item.Kind == DestructibleObjectKind.Explosive && item.Owner.Type.Name == "ExplodeObject");

        Assert.True(explosive.HasExplosionSmoke);
        Assert.Equal(1f, explosive.ExplosionFireballs);
        Assert.Equal(1, explosive.ExplosionFragments);
        Assert.Equal(15, explosive.ExplosionDelayTicks);
        Assert.Equal(0.5f, explosive.ExplosionRadiusUnits / WorldScene.UnitsPerMetre);
        Assert.Equal(0.5f, explosive.ExplosionFalloffUnits / WorldScene.UnitsPerMetre);
        Assert.Equal(0.5f, explosive.ExplosionDamageFactor);
        Assert.Contains(
            scene.Placements,
            placement => placement.MagicExplosionFireballs > 0 && placement.MagicExplosionFragments > 0);
        Assert.Contains(
            scene.Placements,
            placement => placement.MagicTaskTypeName == "TASKTYPE_EXPLODEMAGICOBJ"
                && placement.MagicExplosionSound == "explo_02_m"
                && placement.MagicExplosionFireballs == 1f
                && placement.MagicExplosionFragments == 2
                && placement.MagicExplosionDelayTicks == 5
                && placement.MagicExplosionFalloffMetres == 5f
                && placement.MagicExplosionRadiusMetres == 3f
                && placement.MagicExplosionDamageThreshold == 2f
                && placement.MagicDestroyedModel == "300_02_1"
                && placement.DestroyedReplacement is not null);
    }

    [RequiresGameDataFact]
    public void ExplosionHitVisitsEveryNearbyAuthoredDestructible()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        DestructibleObject source = registry.Objects
            .First(item => item.Kind == DestructibleObjectKind.Explosive);
        DestructibleObject target = registry.Objects
            .Where(item => !ReferenceEquals(item, source))
            .OrderBy(item => (item.PositionUnits - source.PositionUnits).LengthSquared)
            .First();
        float distance = (float)(target.PositionUnits - source.PositionUnits).Length;

        IReadOnlyList<DestructibleObject> affected = registry.ApplyExplosionHits(
            source,
            source.PositionUnits,
            distance + 1f);

        Assert.Contains(target, affected);
        Assert.True(target.IsDestroyed);
        Assert.DoesNotContain(source, affected);
    }

    [RequiresGameDataFact]
    public void ExplodeMagicObjectAccumulatesDamageUntilRetailThreshold()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        DestructibleObject target = registry.Objects.First(
            item => item.TypeName == "TASKTYPE_EXPLODEMAGICOBJ" && item.ExplosionDamageThreshold > 1f);
        float threshold = target.ExplosionDamageThreshold;
        int hitsRequired = (int)MathF.Ceiling(threshold);

        for (int hit = 0; hit < hitsRequired - 1; hit++)
        {
            IReadOnlyList<DestructibleObject> affected = registry.ApplyExplosionHits(
                source: null,
                target.PositionUnits,
                radiusUnits: 1f,
                damageFactor: 1f);

            Assert.DoesNotContain(target, affected);
            Assert.False(target.IsDestroyed);
        }

        IReadOnlyList<DestructibleObject> finalAffected = registry.ApplyExplosionHits(
            source: null,
            target.PositionUnits,
            radiusUnits: 1f,
            damageFactor: 1f);

        Assert.Contains(target, finalAffected);
        Assert.True(target.IsDestroyed);
        Assert.Equal(threshold, target.ExplosionDamageThreshold);
        Assert.True(target.ExplosionDamageAccumulated >= threshold);
    }

    [RequiresGameDataFact]
    public void ExplodeMagicObjectUsesItsNativeMeshContactBound()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        DestructibleObject target = registry.Objects.First(
            item => item.TypeName == "TASKTYPE_EXPLODEMAGICOBJ"
                && item.Placement is not null
                && item.ExplosionContactRadiusUnits > 0f
                && !item.IsDestroyed);

        Assert.Equal(target.Placement!.Model.NativeMarkerBound, target.ExplosionContactRadiusUnits);

        double offset = target.ExplosionContactRadiusUnits + 1d;
        registry.ApplyExplosionHits(
            source: null,
            new Vector3d(
                target.PositionUnits.X + offset,
                target.PositionUnits.Y,
                target.PositionUnits.Z),
            radiusUnits: 0f,
            falloffUnits: 1000f,
            damageFactor: 1f);

        // The centre is just outside the blast radius, but still inside the target's native bound.
        // FUN_00416c90 therefore contributes a positive, linearly-falling dose.
        Assert.True(target.ExplosionDamageAccumulated > 0f);
    }

    [RequiresGameDataFact]
    public void ExplosionContactGateCanRejectAWorldOccludedTarget()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        DestructibleObject target = registry.Objects.First(item => item.Kind == DestructibleObjectKind.Glass);
        IReadOnlyList<DestructibleObject> affected = registry.ApplyExplosionHits(
            source: null,
            target.PositionUnits,
            radiusUnits: 1f,
            canContact: _ => false);

        Assert.Empty(affected);
        Assert.False(target.IsDestroyed);
    }

    [RequiresGameDataFact]
    public void OrdnanceBlastDestroysEveryNearbyVisiblePaneOnlyOnce()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            RetailLevelOneGround.LevelRoot(),
            scene.Placements,
            new ScriptVarRegistry(new NativeRegistry()));

        DestructibleObject[] panes = [.. registry.Objects.Where(item => item.Kind == DestructibleObjectKind.Glass)];
        (DestructibleObject First, DestructibleObject Second)? pair = panes
            .SelectMany((first, index) => panes
                .Skip(index + 1)
                .Select(second => (First: first, Second: second)))
            .Where(candidate =>
                (candidate.Second.PositionUnits - candidate.First.PositionUnits).Length
                    <= M203Physics.DefaultExplosionRadiusMetres * M203Physics.UnitsPerMetre)
            .OrderBy(candidate =>
                (candidate.Second.PositionUnits - candidate.First.PositionUnits).Length)
            .Select(candidate => ((DestructibleObject First, DestructibleObject Second)?)candidate)
            .FirstOrDefault();

        Assert.NotNull(pair);
        DestructibleObject first = pair.Value.First;
        DestructibleObject second = pair.Value.Second;

        IReadOnlyList<DestructibleObject> affected = registry.ApplyExplosionHits(
            source: null,
            first.PositionUnits,
            radiusUnits: M203Physics.DefaultExplosionRadiusMetres * M203Physics.UnitsPerMetre,
            falloffUnits: 0f,
            damageFactor: M203Physics.DamageFactor,
            canContact: _ => true);

        Assert.Contains(first, affected);
        Assert.Contains(second, affected);
        Assert.Equal(affected.Count, affected.Distinct(ReferenceEqualityComparer.Instance).Count());

        IReadOnlyList<DestructibleObject> duplicate = registry.ApplyExplosionHits(
            source: null,
            first.PositionUnits,
            radiusUnits: M203Physics.DefaultExplosionRadiusMetres * M203Physics.UnitsPerMetre,
            damageFactor: M203Physics.DamageFactor);
        Assert.Empty(duplicate);
    }
}
