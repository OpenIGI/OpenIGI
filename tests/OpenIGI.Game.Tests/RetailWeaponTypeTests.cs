using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Reads the runtime weapon table the way the game does — by walking <c>weapons/</c> and running each
/// weapon's own script — and checks it against the parallel declaration that ships beside it.
/// </summary>
public sealed class RetailWeaponTypeTests
{
    [RequiresGameDataFact]
    public void TheRosterIsTwentyFourWeaponsWithUniqueIds()
    {
        WeaponTypeRegistry weapons = Load(out _, out _, out _);

        Assert.Equal(24, weapons.Weapons.Count);
        Assert.Equal(24, weapons.ById.Count);
        Assert.All(weapons.Weapons, w => Assert.StartsWith("WEAPON_ID_", w.ScriptId, StringComparison.Ordinal));

        // 0x413BC0 hands this out; nothing may claim an id above it.
        Assert.Equal(44, weapons.MaximumWeaponId);
        Assert.All(weapons.Weapons, w => Assert.True(w.WeaponId <= weapons.MaximumWeaponId));
    }

    [RequiresGameDataFact]
    public void TheEnumeratedConstantsAreTheExecutablesOwnValues()
    {
        // 0x413DF0 registers all sixteen members by name, so these are read values rather than a
        // numbering this port invented.
        WeaponTypeRegistry weapons = Load(out _, out _, out _);

        WeaponType glock = weapons.ById[1];
        Assert.Equal(WeaponCategory.Pistol, glock.Category);
        Assert.Equal(SightDisplayType.Crosshair, glock.SightDisplay);
        Assert.Equal(AmmoDisplayType.Clip, glock.AmmoDisplay);

        // And every weapon lands on a defined member rather than a stray number.
        Assert.All(weapons.Weapons, w => Assert.True(Enum.IsDefined(w.Category), $"{w.ScriptId}: {w.Category}"));
        Assert.All(weapons.Weapons, w => Assert.True(Enum.IsDefined(w.SightDisplay), $"{w.ScriptId}"));
        Assert.All(weapons.Weapons, w => Assert.True(Enum.IsDefined(w.AmmoDisplay), $"{w.ScriptId}"));
    }

    [RequiresGameDataFact]
    public void TheMilUsesItsRetailLoopPair()
    {
        WeaponType mil = Load(out _, out _, out _).Weapons
            .Single(w => w.ScriptId == "WEAPON_ID_MIL");

        Assert.Equal("mil_loop", mil.FireSound);
        Assert.Equal("mil_loop_e", mil.FireLoopEndSound);
        Assert.True(mil.MaximumRoundsPerBurst > 1);
    }

    [RequiresGameDataFact]
    public void TheDualUziUsesTheRetailMultipartViewModel()
    {
        WeaponType uzix2 = Load(out _, out _, out _).Weapons
            .Single(w => w.ScriptId == "WEAPON_ID_UZIX2");

        Assert.Equal("111_04_1", uzix2.GunModel);
        Assert.Equal(WeaponCategory.Gun, uzix2.Category);
        Assert.Equal(1, uzix2.BulletsPerRound);
        Assert.Equal("TASKTYPE_GUN", uzix2.Weapon.TypeName);
    }

    [RequiresGameDataFact]
    public void TheArgumentListIsVariableLength()
    {
        // The reason a fixed-49 reader would be wrong. Six of the twenty-four weapons carry a tail,
        // because the projectile and weapon task classes read their own parameters from the same call.
        WeaponTypeRegistry weapons = Load(out _, out _, out _);

        Dictionary<string, int> tails = weapons.Weapons.ToDictionary(
            w => w.ScriptId,
            w => w.Projectile.ArgumentCount + w.Weapon.ArgumentCount,
            StringComparer.Ordinal);

        // No tail at all: the Glock names TASKTYPE_GUNSHOT and TASKTYPE_GUN, neither of which reads one.
        Assert.Equal(0, tails["WEAPON_ID_GLOCK"]);

        // A tail on the projectile: the rocket's model sits BETWEEN the two task types.
        Assert.Equal(1, weapons.ById[weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_RPG18").WeaponId]
            .Projectile.ArgumentCount);
        Assert.Equal("110_02_1", weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_RPG18").Projectile.Text);

        // The other two rocket carriers do the same as the RPG.
        Assert.Equal(1, tails["WEAPON_ID_APC"]);
        Assert.Equal(1, tails["WEAPON_ID_T80"]);

        // Tails on the weapon.
        Assert.Equal(1, tails["WEAPON_ID_MP5SD"]);
        Assert.Equal(1, tails["WEAPON_ID_DRAGUNOV"]);
        Assert.Equal(3, tails["WEAPON_ID_M16A2"]);
        Assert.Equal(3, tails["WEAPON_ID_BINOCULARS"]);
        Assert.Equal(4, tails["WEAPON_ID_PROXIMITYMINE"]);
        Assert.Equal(1, tails["WEAPON_ID_MEDIPACK"]);

        // Nine weapons carry a tail and fifteen do not, so the call is 49, 50, 52 or 53 arguments long.
        Assert.Equal(15, tails.Values.Count(t => t == 0));
        Assert.Equal(9, tails.Values.Count(t => t > 0));
    }

    [RequiresGameDataFact]
    public void AZoomAngleLosesItsHalfDegreeToIntegerDivision()
    {
        // 0x4732F0 halves the field of view with cdq/sub/sar - an integer divide - before converting to
        // a double. The Dragunov's 5 degrees therefore becomes tan(2), not tan(2.5).
        WeaponTypeRegistry weapons = Load(out _, out _, out _);

        WeaponType dragunov = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_DRAGUNOV");
        Assert.Equal(WeaponTaskParameters.HalfAngleTangent(5), dragunov.Weapon.Values[0]);
        Assert.Equal((float)Math.Tan(2 * (double)WeaponTaskParameters.DegreesToRadians), dragunov.Weapon.Values[0]);
        Assert.NotEqual((float)Math.Tan(2.5 * (double)WeaponTaskParameters.DegreesToRadians), dragunov.Weapon.Values[0]);

        // The MP5SD's 50 halves exactly, so it is unaffected.
        WeaponType mp5sd = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_MP5SD");
        Assert.Equal((float)Math.Tan(25 * (double)WeaponTaskParameters.DegreesToRadians), mp5sd.Weapon.Values[0]);
    }

    [RequiresGameDataFact]
    public void TheMp5SightTogglesAndEasesAtTheRetailTickRates()
    {
        WeaponTypeRegistry weapons = Load(out _, out _, out _);
        WeaponType mp5 = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_MP5SD");
        WeaponType glock = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_GLOCK");
        Mp5SightZoom zoom = new();

        SightZoomFrame frame = zoom.Tick(
            mp5,
            togglePressed: true,
            HumanView.DefaultTanX,
            HumanView.DefaultViewModelTanX);

        Assert.True(zoom.IsActive);
        Assert.Equal(HumanView.DefaultTanX * Mp5SightZoom.ZoomInFactor, frame.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX * Mp5SightZoom.ZoomInFactor, frame.ViewModelTanX);

        // 0x473AF0 tests before multiplying, so the fourth step deliberately overshoots the authored
        // tan(25 degrees) minimum and then stays there rather than clamping back to it.
        for (int i = 0; i < 4; i++)
        {
            frame = zoom.Tick(mp5, togglePressed: false, frame.TanX, frame.ViewModelTanX);
        }

        Assert.True(frame.TanX <= mp5.Weapon.Values[0]);
        SightZoomFrame settled = zoom.Tick(mp5, togglePressed: false, frame.TanX, frame.ViewModelTanX);
        Assert.Equal(frame, settled);

        SightZoomFrame reset = zoom.Tick(glock, togglePressed: false, frame.TanX, frame.ViewModelTanX);
        Assert.False(zoom.IsActive);
        Assert.Equal(HumanView.DefaultTanX, reset.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX, reset.ViewModelTanX);
    }

    /// <summary>The Dragunov's scope: hold to zoom, release to hold, press again to snap shut.</summary>
    /// <remarks>Against <c>0x472E50</c>'s own arithmetic; see <see cref="DragunovScope"/>.</remarks>
    [RequiresGameDataFact]
    public void TheDragunovScopeHoldsToZoomAndSnapsShut()
    {
        WeaponTypeRegistry weapons = Load(out _, out _, out _);
        WeaponType dragunov = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_DRAGUNOV");
        WeaponType glock = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_GLOCK");
        DragunovScope scope = new();

        // The opening edge sets the latch and moves nothing: the glide reads the PREVIOUS tick's
        // slot, which on the edge is still clear.
        SightZoomFrame frame = scope.Tick(
            dragunov,
            alternateHeld: true,
            alternateHeldLastTick: false,
            zoomInHeld: false,
            zoomOutHeld: false,
            HumanView.DefaultTanX,
            HumanView.DefaultViewModelTanX);
        Assert.True(scope.IsScoped);
        Assert.Equal(HumanView.DefaultTanX, frame.TanX);

        // Held, the view glides in at the retail 0.96 a tick.
        frame = scope.Tick(dragunov, true, true, false, false, frame.TanX, frame.ViewModelTanX);
        Assert.Equal(HumanView.DefaultTanX * DragunovScope.ZoomInFactor, frame.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX * DragunovScope.ZoomInFactor, frame.ViewModelTanX);

        // The release tick still glides -- the previous slot is what the selector reads -- and the
        // tick after that HOLDS the magnification. That is the whole hold-to-zoom feel.
        frame = scope.Tick(dragunov, false, true, false, false, frame.TanX, frame.ViewModelTanX);
        SightZoomFrame held = scope.Tick(dragunov, false, false, false, false, frame.TanX, frame.ViewModelTanX);
        Assert.Equal(frame, held);
        Assert.True(scope.IsScoped);

        // The zoom-out slot widens at 1.04 a tick, and never past the default.
        SightZoomFrame wider = scope.Tick(dragunov, false, false, false, true, held.TanX, held.ViewModelTanX);
        Assert.Equal(held.TanX * DragunovScope.ZoomOutFactor, wider.TanX);

        // A second press of alternate fire snaps the scope shut in one tick -- 0x4659E0 writes the
        // defaults outright, where the MP5 would glide back.
        SightZoomFrame shut = scope.Tick(dragunov, true, false, false, false, wider.TanX, wider.ViewModelTanX);
        Assert.False(scope.IsScoped);
        Assert.Equal(HumanView.DefaultTanX, shut.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX, shut.ViewModelTanX);

        // Changing weapons closes and resets, as the weapon-change states do through 0x4659E0.
        scope.Tick(dragunov, true, false, false, false, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        Assert.True(scope.IsScoped);
        SightZoomFrame reset2 = scope.Tick(glock, false, false, false, false, 0.1f, 0.1f);
        Assert.False(scope.IsScoped);
        Assert.Equal(HumanView.DefaultTanX, reset2.TanX);
    }

    [RequiresGameDataFact]
    public void TheBinocularsStoreTheirThreeAnglesOutOfOrder()
    {
        // 0x470CE0 writes slots 1, 2, 0 - so the script's "5, 90, 70" reads as minimum, maximum,
        // default rather than as the order it was written in.
        WeaponTypeRegistry weapons = Load(out _, out _, out _);

        WeaponType binoculars = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_BINOCULARS");
        Assert.Equal(WeaponTaskParameters.HalfAngleTangent(70), binoculars.Weapon.Values[0]);
        Assert.Equal(WeaponTaskParameters.HalfAngleTangent(5), binoculars.Weapon.Values[1]);
        Assert.Equal(WeaponTaskParameters.HalfAngleTangent(90), binoculars.Weapon.Values[2]);

        // Narrowest to widest, which is the only ordering that makes the trio a zoom range.
        Assert.True(binoculars.Weapon.Values[1] < binoculars.Weapon.Values[0]);
        Assert.True(binoculars.Weapon.Values[0] < binoculars.Weapon.Values[2]);
    }

    [RequiresGameDataFact]
    public void TheTailsMatchTheLevelTagDefaultsForTheSameQuantities()
    {
        // Independent confirmation that the tail is read correctly: each of these classes also reads the
        // same quantities from level tags, with defaults compiled into the executable. The shipped
        // script values equal those defaults exactly.
        WeaponTypeRegistry weapons = Load(out _, out _, out _);

        // 0x475960: trigger delay 2, closing radius 0.5, activation radius 1.5, explosion radius 3.
        WeaponType mine = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_PROXIMITYMINE");
        Assert.Equal<float[]>([2f, 0.5f, 1.5f, 3f], [.. mine.Weapon.Values]);

        // 0x470570: the fraction of maximum health one use restores.
        WeaponType medipack = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_MEDIPACK");
        Assert.Equal(0.01f, medipack.Weapon.Values[0]);
        // FUN_0040FBE0 reloads Human+0xD6C with the ordinary 30 Hz gun-clock expression. The
        // shipped medipack's 120 RPM therefore spaces held doses by 15 simulation updates.
        Assert.Equal(120, medipack.RoundsPerMinute);
        Assert.Equal(15, GunTiming.TicksPerRound(medipack.RoundsPerMinute));

        // 0x4737F0: M203 muzzle velocity, explosion radius, activate radius.
        WeaponType m16 = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_M16A2");
        Assert.Equal<float[]>([85f, 3f, 1f], [.. m16.Weapon.Values]);
    }

    [RequiresGameDataFact]
    public void LoadingTheRosterPublishesTheWeaponIdSymbols()
    {
        // The divergence the player config recorded: WEAPON_ID_* is pattern-registered, so the values
        // live in the weapon scripts. The loader publishes each as it reads it - no separate step, and
        // no placeholder invented.
        WeaponTypeRegistry weapons = Load(out ResourceRegistry resources, out NativeRegistry natives, out _);

        Assert.All(weapons.Weapons, w => Assert.True(natives.Contains(w.ScriptId), w.ScriptId));

        HumanPlayerConfig player = new();
        player.Register(natives);
        player.Load(resources, natives);

        Assert.All(
            player.PlaceholderIds.Keys,
            name => Assert.StartsWith("AMMO_ID_", name, StringComparison.Ordinal));

        // Every weapon the player can cycle to is a real weapon, and the roster is larger than the
        // wheel because it also holds the vehicle and emplacement guns.
        Assert.All(player.WeaponCycle, id => Assert.True(weapons.TryGet(id, out _), $"cycle names {id}"));
        Assert.True(weapons.Weapons.Count > player.WeaponCycle.Count);
    }

    [RequiresGameDataFact]
    public void TheGunModelLookupResolvesEveryWeapon()
    {
        // 0x413C00 searches on this field, so it has to identify a weapon on its own.
        WeaponTypeRegistry weapons = Load(out _, out _, out _);

        foreach (WeaponType weapon in weapons.Weapons)
        {
            Assert.True(weapons.TryGetByGunModel(weapon.GunModel, out WeaponType found), weapon.ScriptId);
            Assert.Equal(weapon.GunModel, found.GunModel);
        }
    }

    [RequiresGameDataFact]
    public void TheTwoDeclarationsAgreeOnEveryIdentifyingField()
    {
        // This is what proves the argument mapping. weaponconfig.qsc declares the same fifty fields in
        // the same order as a task tree, so it is an independent transcription of the same struct: if
        // this reader had an argument out of place, names, models, animations or sounds would land on
        // the wrong field and disagree. Across twenty-four weapons, none of them does.
        WeaponTypeRegistry live = Load(out ResourceRegistry resources, out NativeRegistry natives, out GameLoop loop);

        WeaponConfigRegistry.RegisterTypes(loop.Types);
        WeaponConfigRegistry config = new();
        config.Load(resources, natives, loop.Tasks);

        Assert.Equal(live.Weapons.Count, config.Weapons.Count);

        foreach (WeaponConfigTask declared in config.Weapons)
        {
            Assert.True(live.TryGet(declared.WeaponId, out WeaponType actual), $"#{declared.WeaponId}");

            Assert.Equal(declared.ScriptId, actual.ScriptId);
            Assert.Equal(declared.DisplayName, actual.DisplayName);
            Assert.Equal(declared.Manufacturer, actual.Manufacturer);
            Assert.Equal(declared.Description, actual.Description);
            Assert.Equal(declared.TypeOfWeapon, actual.TypeOfWeapon);
            Assert.Equal(declared.WeaponUsers, actual.WeaponUsers);

            Assert.Equal(declared.WeaponType, (int)actual.Category);
            Assert.Equal(declared.SightDisplayType, (int)actual.SightDisplay);
            Assert.Equal(declared.AmmoDisplayType, (int)actual.AmmoDisplay);

            Assert.Equal(declared.GunModel, actual.GunModel);
            Assert.Equal(declared.FireSound, actual.FireSound);
            Assert.Equal(declared.FireLoopEndSound, actual.FireLoopEndSound);

            Assert.Equal(declared.BulletsPerRound, actual.BulletsPerRound);

            // Typed Real32 in the declaration and Int32 in the live reader. Every shipped value is
            // whole, so the two still agree - which is itself worth pinning.
            Assert.Equal(declared.WeaponLength, actual.WeaponLength);
            Assert.Equal(declared.BarrelLength, actual.BarrelLength);

            // All thirteen animation ids, which are the least forgiving fields of the lot. The medipack
            // is the one weapon whose two revisions disagree structurally: the declaration gives it two
            // extra firing clips the shipped data does not have.
            if (declared.ScriptId == "WEAPON_ID_MEDIPACK")
            {
                continue;
            }

            Assert.Equal<int[]>(
                [
                    declared.StandAnimation, declared.MoveAnimation,
                    declared.FireAnimation1, declared.FireAnimation2, declared.FireAnimation3,
                    declared.ReloadAnimation,
                    declared.UpperBodyStandAnimation, declared.UpperBodyWalkAnimation,
                    declared.UpperBodyCrouchAnimation, declared.UpperBodyCrouchRunAnimation,
                    declared.UpperBodyRunAnimation, declared.UpperBodyFireAnimation,
                    declared.UpperBodyReloadAnimation,
                ],
                [
                    actual.StandAnimation, actual.MoveAnimation,
                    actual.FireAnimation1, actual.FireAnimation2, actual.FireAnimation3,
                    actual.ReloadAnimation,
                    actual.UpperBodyStandAnimation, actual.UpperBodyWalkAnimation,
                    actual.UpperBodyCrouchAnimation, actual.UpperBodyCrouchRunAnimation,
                    actual.UpperBodyRunAnimation, actual.UpperBodyFireAnimation,
                    actual.UpperBodyReloadAnimation,
                ]);
        }
    }

    [RequiresGameDataFact]
    public void TheDeclarationIsADifferentRevisionOfTheTuning()
    {
        // The two do NOT hold the same numbers. Every field that differs is one somebody would tune -
        // damage, spread, recoil, range, rate, magazine - plus four outright corrections. Since the
        // declaration is never read at runtime, none of it ever took effect, and a port that loaded it
        // instead would play differently from retail.
        WeaponTypeRegistry live = Load(out ResourceRegistry resources, out NativeRegistry natives, out GameLoop loop);

        WeaponConfigRegistry.RegisterTypes(loop.Types);
        WeaponConfigRegistry config = new();
        config.Load(resources, natives, loop.Tasks);

        WeaponType Live(WeaponConfigTask d)
        {
            live.TryGet(d.WeaponId, out WeaponType found);
            return found;
        }

        // The Glock hits harder and further in the shipped game than the declaration says.
        WeaponConfigTask glock = config.Weapons.First(w => w.ScriptId == "WEAPON_ID_GLOCK");
        Assert.Equal(0.2f, glock.DamageFactor);
        Assert.Equal(0.25f, Live(glock).DamageFactor);

        // The declaration lumps both revolvers into .357; the live data gives the Anaconda its .44 and
        // the Desert Eagle 9x19 - and both of those name a real ammo directory while one .357 does not.
        WeaponConfigTask colt = config.Weapons.First(w => w.ScriptId == "WEAPON_ID_COLT");
        Assert.Equal(357, colt.CalibreId);
        Assert.Equal(44, Live(colt).CalibreId);

        // And the declaration's mass for the two heaviest guns has been round-tripped through a signed
        // 16-bit field: 38000 grams comes back as 38000 - 65536. That is a tool artefact, and it is the
        // clearest sign the file is a generated export rather than the source of truth.
        WeaponConfigTask t80 = config.Weapons.First(w => w.ScriptId == "WEAPON_ID_T80");
        Assert.Equal(38000, Live(t80).MassInGrams);
        Assert.Equal(38000 - 65536, t80.MassInGrams);

        // The spreads use different conventions too: the live pair is a signed range about zero for
        // most weapons, the declaration a pair of magnitudes.
        WeaponConfigTask ak47 = config.Weapons.First(w => w.ScriptId == "WEAPON_ID_AK47");
        Assert.Equal(-3.5f, Live(ak47).MinimumSpread);
        Assert.Equal(3.5f, Live(ak47).MaximumSpread);
        Assert.Equal(5f, ak47.MinimumSpread);
        Assert.Equal(10f, ak47.MaximumSpread);

        // The medipack is the only weapon the two revisions disagree about structurally rather than
        // numerically: the declaration gives it a muzzle velocity, a penetration power and two extra
        // firing clips, none of which the shipped data has. It is a healing item, so the live reading
        // is the sane one.
        WeaponConfigTask medipack = config.Weapons.First(w => w.ScriptId == "WEAPON_ID_MEDIPACK");
        Assert.Equal(8f, medipack.MuzzleVelocity);
        Assert.Equal(0f, Live(medipack).MuzzleVelocity);
        Assert.Equal(79, medipack.FireAnimation2);
        Assert.Equal(-1, Live(medipack).FireAnimation2);

        // Everything else agrees, so the disagreements really are confined to what somebody would tune.
        Assert.Equal(122880f, Live(config.Weapons.First(w => w.ScriptId == "WEAPON_ID_DRAGUNOV")).AiDetectionRange);
    }

    [RequiresGameDataFact]
    public void OnlyTheDeclarationCanMakeAWeaponUnselectableWhenEmpty()
    {
        // The live loader stores a literal 1 at the offset the declaration's fiftieth parameter fills,
        // so on that path every weapon is selectable when empty. The declaration disagrees for some -
        // which is exactly the field it was added to express.
        WeaponTypeRegistry live = Load(out ResourceRegistry resources, out NativeRegistry natives, out GameLoop loop);

        WeaponConfigRegistry.RegisterTypes(loop.Types);
        WeaponConfigRegistry config = new();
        config.Load(resources, natives, loop.Tasks);

        Assert.All(live.Weapons, w => Assert.True(w.SelectableWhenEmpty));
        Assert.Contains(config.Weapons, w => !w.SelectableWhenEmpty);
    }

    [RequiresGameDataFact]
    public void EveryActionsUpperBodyAnimationResolvesInThePlayersSet()
    {
        // The weapon animation dispatch (0x4137E0) names a full-body and an upper-body clip for every
        // action. The player runs only the upper-body track, because its legs are already driven by a
        // locomotion state - so those ids, and not the full-body ones, must exist in 000.iff.
        WeaponTypeRegistry weapons = Load(out _, out _, out _);
        Formats.Animation.BoneAnimationSet player = Formats.Animation.BoneAnimationSet.Read(
            File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff")));

        WeaponAction[] actions =
        [
            WeaponAction.Stand, WeaponAction.Walk, WeaponAction.Run, WeaponAction.Crouch,
            WeaponAction.CrouchRun, WeaponAction.FireTimed, WeaponAction.Reload, WeaponAction.Fire,
            WeaponAction.StandAlternate, WeaponAction.StandImmediate,
        ];

        int checkedIds = 0;
        foreach (WeaponType weapon in weapons.Weapons)
        {
            foreach (WeaponAction action in actions)
            {
                WeaponAnimations pair = WeaponAnimationTable.Select(weapon, action);
                if (pair.UpperBody < 0)
                {
                    continue;
                }

                Assert.True(
                    player.TryGet(pair.UpperBody, out _),
                    $"{weapon.ScriptId}/{action} names upper-body {pair.UpperBody}, absent from the player's set");
                checkedIds++;
            }
        }

        Assert.True(checkedIds > 100, $"only {checkedIds} upper-body ids were reachable");

        // And the split is real: at least one full-body clip is missing from the player's set, because
        // those belong to the soldiers.
        Assert.Contains(
            weapons.Weapons,
            w => w.FireAnimation1 >= 0 && !player.TryGet(w.FireAnimation1, out _));
    }

    [RequiresGameDataFact]
    public void RunAndWalkShareAFullBodyClipButNotATorsoOne()
    {
        // A detail of 0x4137E0 that is easy to get wrong: case 3 reuses the MoveAnim of case 2 and only
        // changes the upper-body track.
        WeaponTypeRegistry weapons = Load(out _, out _, out _);
        WeaponType ak47 = weapons.Weapons.First(w => w.ScriptId == "WEAPON_ID_AK47");

        WeaponAnimations walk = WeaponAnimationTable.Select(ak47, WeaponAction.Walk);
        WeaponAnimations run = WeaponAnimationTable.Select(ak47, WeaponAction.Run);

        Assert.Equal(walk.FullBody, run.FullBody);
        Assert.NotEqual(walk.UpperBody, run.UpperBody);

        // Firing blends in immediately; standing eases.
        Assert.Equal(0f, WeaponAnimationTable.Select(ak47, WeaponAction.Fire).UpperBodyBlendSeconds);
        Assert.Equal(
            WeaponAnimationTable.DefaultBlendSeconds,
            WeaponAnimationTable.Select(ak47, WeaponAction.Stand).UpperBodyBlendSeconds);
    }

    [RequiresGameDataFact]
    public void ReloadingTheRosterAgainstAPersistentNamespaceKeepsEveryTaskBinding()
    {
        // A mission restart and a level change both build a FRESH WeaponTypeRegistry but reuse the
        // session's NativeRegistry (StartMission runs against the same GameLoop). The TASKTYPE_* task
        // classes register their id->name mapping on that persistent namespace, but the map that turns
        // a resolved id back into a class name lives on the registry instance. If the second load skips
        // re-recording it because the constant is already registered, ReadTaskBinding gets an empty class
        // name, WeaponTaskParameters.Read returns an empty binding, and every optical weapon loses its
        // task type and zoom parameters -- which is issue #79: the Dragunov scope (and MP5SD sight and
        // binoculars) go dead after the first mission. Reload here and prove the bindings survive.
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        NativeRegistry natives = loop.Natives;

        WeaponTypeRegistry first = new();
        first.Register(natives);
        first.Load(fileSystem, resources, natives);

        // The second mission: a new roster over the SAME, already-populated namespace.
        WeaponTypeRegistry second = new();
        second.Register(natives);
        second.Load(fileSystem, resources, natives);

        foreach (WeaponType reloaded in second.Weapons)
        {
            WeaponType original = first.Weapons.First(w => w.WeaponId == reloaded.WeaponId);

            // The task classes and every decoded parameter must read identically on the reload.
            Assert.Equal(original.Weapon.TypeName, reloaded.Weapon.TypeName);
            Assert.Equal<float[]>([.. original.Weapon.Values], [.. reloaded.Weapon.Values]);
            Assert.Equal(original.Weapon.Text, reloaded.Weapon.Text);
            Assert.Equal(original.Projectile.TypeName, reloaded.Projectile.TypeName);
            Assert.Equal(original.Projectile.Text, reloaded.Projectile.Text);
        }

        // And the symptom the issue tracks, stated outright: the reloaded Dragunov still scopes.
        WeaponType dragunov = second.Weapons.First(w => w.ScriptId == "WEAPON_ID_DRAGUNOV");
        Assert.Equal(DragunovScope.TaskType, dragunov.Weapon.TypeName);
        Assert.NotEmpty(dragunov.Weapon.Values);
        Assert.Equal(WeaponTaskParameters.HalfAngleTangent(5), dragunov.Weapon.Values[0]);

        DragunovScope scope = new();
        scope.Tick(
            dragunov,
            alternateHeld: true,
            alternateHeldLastTick: false,
            zoomInHeld: false,
            zoomOutHeld: false,
            HumanView.DefaultTanX,
            HumanView.DefaultViewModelTanX);
        Assert.True(scope.IsScoped);
    }

    private static WeaponTypeRegistry Load(
        out ResourceRegistry resources,
        out NativeRegistry natives,
        out GameLoop loop)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        resources = new ResourceRegistry(fileSystem);

        loop = new GameLoop(new ManualTimeSource());
        natives = loop.Natives;

        WeaponTypeRegistry weapons = new();
        weapons.Register(natives);
        weapons.Load(fileSystem, resources, natives);
        return weapons;
    }
}
