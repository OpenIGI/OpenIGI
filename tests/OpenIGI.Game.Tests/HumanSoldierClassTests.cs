using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The three AI-driven <c>Human</c> classes — soldier, female soldier and RPG soldier — as their
/// constructors, tables and body machines define them.
/// </summary>
public sealed class HumanSoldierClassTests
{
    private const int FireSlot = 6;
    private const int ReloadSlot = 8;

    [Fact]
    public void EachClassCarriesItsConstructorDefaults()
    {
        // 0x45CC50, 0x454B10 and 0x456C70 write +3320 and +3324 before the level's parameters land.
        Assert.Equal((1, -1, 2), Defaults(HumanSoldierClass.Soldier));
        Assert.Equal((6, -1, 6), Defaults(HumanSoldierClass.FemaleSoldier));
        Assert.Equal((2, 2, 2), Defaults(HumanSoldierClass.RpgSoldier));

        HumanSoldierFemaleTask female = new();
        Assert.Same(HumanSoldierClass.FemaleSoldier, female.Class);
        Assert.Equal(6, female.BoneHierarchy);
        Assert.Equal(-1, female.StandAnimation);

        HumanSoldierRpgTask rpg = new();
        Assert.Equal(2, rpg.BoneHierarchy);
        Assert.Equal(2, rpg.StandAnimation);

        static (int, int, int) Defaults(HumanSoldierClass c) =>
            (c.DefaultBoneHierarchy, c.DefaultStandAnimation, c.FallbackStandAnimation);
    }

    [Fact]
    public void TheVariantTablesKeepTheSoldiersMultipliersWithTheirOwnClips()
    {
        // 0x53E508 and 0x53E798, read byte for byte.
        for (int zone = 0; zone < HumanHitZoneTable.Soldier.Count; zone++)
        {
            float multiplier = HumanHitZoneTable.Soldier[zone].Multiplier;
            Assert.Equal(new HumanHitZone(multiplier, 6, 13, 13), HumanHitZoneTable.FemaleSoldier[zone]);

            int reaction = zone switch { 14 => 16, 15 => 18, 16 => 12, _ => 15 };
            Assert.Equal(new HumanHitZone(multiplier, 13, reaction, reaction), HumanHitZoneTable.RpgSoldier[zone]);
        }
    }

    [Fact]
    public void TheVariantClipTablesAreTheirOwn()
    {
        // 0x53E4A0 and 0x53E720: thirteen and fifteen states, walking in 3 and running in 2 or 4.
        Assert.Equal(13, HumanSoldierFemaleStates.Table.StateCount);
        Assert.Equal(15, HumanSoldierRpgStates.Table.StateCount);
        Assert.Equal((3, 2), (HumanSoldierClass.FemaleSoldier.WalkAnimation, HumanSoldierClass.FemaleSoldier.RunAnimation));
        Assert.Equal((3, 4), (HumanSoldierClass.RpgSoldier.WalkAnimation, HumanSoldierClass.RpgSoldier.RunAnimation));
        Assert.Equal(9, HumanSoldierFemaleStates.Table.AnimationOf(HumanSoldierFemaleStates.FiringState));
        Assert.True(HumanSoldierFemaleStates.Table.IsSelfDriven(HumanSoldierFemaleStates.DeathState));
        Assert.Equal(11, HumanSoldierRpgStates.Table.AnimationOf(HumanSoldierRpgStates.AimedFiringState));
    }

    [Fact]
    public void AFemaleSoldierFallsOnHerZonesFirstReactionAndDoesNotTurn()
    {
        HumanSoldierClass female = HumanSoldierClass.FemaleSoldier;

        // 0x4546C0: no family, no coin flip, no turn -- the zone row's first reaction, or the script's.
        Assert.Equal(13, female.ChooseDeathAnimation(SoldierDeathMode.Thrown, zone: 15, shotFromTheFront: true, coinFlip: 1));
        Assert.Equal(40, female.ChooseDeathAnimation(SoldierDeathMode.Crumple, zone: 3, true, 0, scriptOverride: 40));
        Assert.False(female.TurnsToTheShot(SoldierDeathMode.Thrown));
    }

    [Theory]
    [InlineData(SoldierDeathMode.Thrown, true, 15)]
    [InlineData(SoldierDeathMode.Thrown, false, 16)]
    [InlineData(SoldierDeathMode.Untouched, true, 15)]
    public void AnRpgSoldierIsThrownInItsOwnClips(SoldierDeathMode mode, bool front, int expected)
    {
        // 0x4567C0: neg al / sbb / add 10h -- a front hit plays 15, a hit from behind 16.
        Assert.Equal(expected, HumanSoldierClass.RpgSoldier.ChooseDeathAnimation(mode, zone: 3, front, coinFlip: 0));
    }

    [Fact]
    public void AnRpgSoldierCrumplesInItsOwnPairAndOnlyTheThrowTurns()
    {
        HumanSoldierClass rpg = HumanSoldierClass.RpgSoldier;
        Assert.Equal(14, rpg.ChooseDeathAnimation(SoldierDeathMode.Crumple, zone: 3, false, coinFlip: 0));
        Assert.Equal(12, rpg.ChooseDeathAnimation(SoldierDeathMode.Crumple, zone: 3, false, coinFlip: 1));
        Assert.Equal(18, rpg.ChooseDeathAnimation(SoldierDeathMode.Crumple, zone: 15, false, coinFlip: 0));

        // 0x45C440 and its RPG copy call 0x45DAA0 only in the thrown branch.
        Assert.True(rpg.TurnsToTheShot(SoldierDeathMode.Thrown));
        Assert.False(rpg.TurnsToTheShot(SoldierDeathMode.Crumple));
        Assert.False(HumanSoldierClass.Soldier.TurnsToTheShot(SoldierDeathMode.Crumple));
    }

    [Fact]
    public void AFemaleSoldierRaisesAimsFiresAndLowersHerWeapon()
    {
        HumanBodyMachine body = HumanSoldierClass.FemaleSoldier.CreateBody();
        const int weaponDrawn = 23;

        // The Anya/Ekk channel row puts the firing pose on slot 23: raise, then aim once the raise ends.
        Assert.Equal(HumanSoldierFemaleStates.RaiseWeaponState, body.Tick(Input(weaponDrawn)));
        Assert.Equal(HumanSoldierFemaleStates.RaiseWeaponState, body.Tick(Input(weaponDrawn)));
        Assert.Equal(HumanSoldierFemaleStates.AimState, body.Tick(Input(weaponDrawn, animationDone: true)));

        // The trigger fires; the fire holds until its clip ends with the trigger released.
        Assert.Equal(HumanSoldierFemaleStates.FiringState, body.Tick(Input(weaponDrawn, FireSlot)));
        Assert.True(body.IsFiring);
        Assert.Equal(HumanSoldierFemaleStates.FiringState, body.Tick(Input(weaponDrawn, animationDone: true, slot2: FireSlot)));
        Assert.Equal(HumanSoldierFemaleStates.AimState, body.Tick(Input(weaponDrawn, animationDone: true)));

        // Letting the pose go lowers the weapon, and the lower returns to standing on its clip.
        Assert.Equal(HumanSoldierFemaleStates.LowerWeaponState, body.Tick(Input()));
        Assert.Equal(HumanSoldierFemaleStates.StandState, body.Tick(Input(animationDone: true)));
    }

    [Fact]
    public void AFemaleSoldierWalksRunsAndPlaysTheClipTheAiNames()
    {
        HumanBodyMachine body = HumanSoldierClass.FemaleSoldier.CreateBody();

        Assert.Equal(HumanSoldierFemaleStates.WalkState, body.Tick(Input(movingForward: true)));
        Assert.Equal(HumanSoldierFemaleStates.RunState, body.Tick(Input(movingForward: true, running: true)));
        Assert.Equal(HumanSoldierFemaleStates.StandState, body.Tick(Input(running: true)));

        // Slot 21 carries the clip id; the state plays it and ends on any action or the clip's end.
        PlayerInputFrame frame = default;
        frame[21] = 40f;
        Assert.Equal(HumanSoldierFemaleStates.ScriptedAnimationState, body.Tick(new HumanBodyInput(frame, AnimationDone: false)));
        Assert.Equal(40, body.CurrentAnimation);
        Assert.Equal(HumanSoldierFemaleStates.StandState, body.Tick(Input(animationDone: true)));
    }

    [Fact]
    public void AnRpgSoldierFiresFromTheHipOrAimsFromTheShoulder()
    {
        HumanBodyMachine body = HumanSoldierClass.RpgSoldier.CreateBody();
        const int aim = 21;

        // A bare trigger is a hip shot: snap raise, fire, back to standing.
        Assert.Equal(HumanSoldierRpgStates.HipRaiseState, body.Tick(Input(FireSlot)));
        Assert.Equal(HumanSoldierRpgStates.HipFiringState, body.Tick(Input(FireSlot, animationDone: true)));
        Assert.True(body.IsFiring);
        Assert.Equal(HumanSoldierRpgStates.StandState, body.Tick(Input(animationDone: true)));

        // The kneel the RPG arm holds is the aim: shoulder raise, aim, lead-in, aimed fire, back to the aim.
        Assert.Equal(HumanSoldierRpgStates.ShoulderRaiseState, body.Tick(Input(aim)));
        Assert.Equal(HumanSoldierRpgStates.AimState, body.Tick(Input(aim, animationDone: true)));
        Assert.Equal(HumanSoldierRpgStates.AimToFireState, body.Tick(Input(aim, FireSlot)));
        Assert.Equal(HumanSoldierRpgStates.AimedFiringState, body.Tick(Input(aim, FireSlot, animationDone: true)));
        Assert.Equal(HumanSoldierRpgStates.AimState, body.Tick(Input(aim, animationDone: true)));

        // A reload from the shoulder completes back to it; letting the aim go stands the soldier up.
        Assert.Equal(HumanSoldierRpgStates.ReloadState, body.Tick(Input(aim, ReloadSlot)));
        Assert.Equal(HumanSoldierRpgStates.AimState, body.Tick(Input(aim, animationDone: true)));
        Assert.Equal(HumanSoldierRpgStates.StandState, body.Tick(Input()));
    }

    [Fact]
    public void AnRpgSoldierRaisesToTheShoulderToReload()
    {
        HumanBodyMachine body = HumanSoldierClass.RpgSoldier.CreateBody();

        // 0x455A40 sends a reload request through the shoulder raise (its 0x800 test jumps to state 5).
        Assert.Equal(HumanSoldierRpgStates.ShoulderRaiseState, body.Tick(Input(ReloadSlot)));
        Assert.Equal(HumanSoldierRpgStates.AimState, body.Tick(Input(ReloadSlot, animationDone: true)));
        Assert.Equal(HumanSoldierRpgStates.ReloadState, body.Tick(Input(ReloadSlot)));
    }

    [Fact]
    public void EveryClassDiesFromAnyOrdinaryState()
    {
        foreach (HumanSoldierClass soldierClass in HumanSoldierClass.All)
        {
            HumanBodyMachine body = soldierClass.CreateBody();
            int state = body.Tick(new HumanBodyInput(default, AnimationDone: false, Dead: true));
            Assert.True(
                soldierClass.States.IsSelfDriven(state),
                $"{soldierClass} died into state {state}, which is not a death state.");
            Assert.Equal(state, body.Tick(new HumanBodyInput(default, AnimationDone: true, Dead: true)));
        }
    }

    private static HumanBodyInput Input(
        int slot1 = -1,
        int slot2 = -1,
        bool animationDone = false,
        bool movingForward = false,
        bool running = false)
    {
        PlayerInputFrame frame = default;
        foreach (int slot in (ReadOnlySpan<int>)[slot1, slot2])
        {
            if (slot >= 0)
            {
                frame[slot] = 1f;
            }
        }

        return new HumanBodyInput(frame, animationDone, MovingForward: movingForward, Running: running);
    }
}
