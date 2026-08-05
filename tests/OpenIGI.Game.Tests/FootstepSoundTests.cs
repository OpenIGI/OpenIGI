using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The footstep rules of <c>0x463920</c>, and whether the retail data joins up behind them.</summary>
public sealed class FootstepSoundTests
{
    [Fact]
    public void TheWalkCycleIsTheAuthoredTwelveStepsAndNotAPermutation()
    {
        // Transcribed from the twelve dwords at 0x53F318. Asserted literally because it is hand-authored
        // data, not a rule: the first six use each sound once, the tail repeats 3 and never says 0.
        Assert.Equal([0, 1, 2, 3, 4, 5, 3, 1, 4, 2, 3, 5], FootstepSounds.Cycle.ToArray());
    }

    [Fact]
    public void TheCursorWalksTheCycleAndWrapsAtTwelve()
    {
        GameMaterial material = new()
        {
            Index = 1,
            WalkSounds = ["a", "b", "c", "d", "e", "f"],
        };

        int cursor = 0;
        string[] taken = new string[13];
        for (int i = 0; i < taken.Length; i++)
        {
            taken[i] = FootstepSounds.Next(material, ref cursor);
        }

        Assert.Equal(["a", "b", "c", "d", "e", "f", "d", "b", "e", "c", "d", "f"], taken[..12]);

        // And round again, which is what makes the cursor a cursor rather than a counter.
        Assert.Equal("a", taken[12]);
        Assert.Equal(1, cursor);
    }

    [Fact]
    public void PitchStaysInsideTheOriginalsRange()
    {
        IgiRandom random = new();
        for (int i = 0; i < 500; i++)
        {
            float pitch = FootstepSounds.Pitch(random);
            Assert.InRange(pitch, 0.8f, 1.3f);
        }
    }

    [Fact]
    public void TheProbeReachesFromJustAboveTheGroundToHalfAMetreBelow()
    {
        (Vector3d from, Vector3d to) = FootstepSounds.Probe(new Vector3d(10.0, 20.0, 30.0));

        Assert.Equal(30.0 + 204.8, from.Z, 6);
        Assert.Equal(30.0 - 2048.0, to.Z, 6);
        Assert.Equal(10.0, from.X);
        Assert.Equal(20.0, to.Y);
    }

    [RequiresGameDataFact]
    public void EveryWalkSoundTheRetailMaterialsNameIsADefinedSound()
    {
        // The whole chain in one assertion: material.qvm names walk_wood_1, sounds.qvm defines it
        // against WALK_WOOD_1, and the bank holds that. A break anywhere is silent footsteps.
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location0/level1/");
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        GameMaterialRegistry materials = new();
        materials.Register(loop.Natives);
        materials.Load(resources, loop.Natives);

        SoundDefinitionRegistry sounds = new();
        sounds.Register(loop.Natives);
        sounds.LoadForMission(resources, loop.Natives);

        Assert.NotEmpty(materials.ByIndex);

        List<string> missing = [];
        int checkedSounds = 0;
        foreach (GameMaterial material in materials.ByIndex.Values)
        {
            foreach (string sound in material.WalkSounds)
            {
                if (sound.Length == 0)
                {
                    continue;
                }

                checkedSounds++;
                if (!sounds.TryResolve(sound, out SoundDefinition? definition) || definition is null)
                {
                    missing.Add($"{material.Name}:{sound}");
                }
            }
        }

        Assert.True(checkedSounds > 0, "no retail material names a walk sound");

        // Four materials resolve to nothing, and all four name the same placeholder: Air, Water, Flesh
        // and BloodyFlesh say `Walk0`, which the sound script never defines. That is the data saying
        // "this surface has no footstep" rather than a break in the chain -- and it is what the original
        // does too, since a name nothing answers to simply returns no task. Every real surface resolves.
        Assert.All(missing, entry => Assert.EndsWith(":Walk0", entry, StringComparison.Ordinal));
        Assert.Equal(4, missing.Count);
    }

    [RequiresGameDataFact]
    public void BareTerrainHasAMaterialOfItsOwnAndItsWalkSoundsAreDefined()
    {
        // A step on open ground hits no object, so it has no collision face to read a material index
        // off. The retail table answers by name: a row called Ground whose six walk sounds exist for
        // exactly this. If this row ever stops being named that, footsteps on terrain go silent again.
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location0/level1/");
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        GameMaterialRegistry materials = new();
        materials.Register(loop.Natives);
        materials.Load(resources, loop.Natives);

        Assert.True(
            materials.TryGetByName(FootstepSounds.TerrainMaterialName, out GameMaterial ground),
            $"the material table has no row named '{FootstepSounds.TerrainMaterialName}'");

        SoundDefinitionRegistry sounds = new();
        sounds.Register(loop.Natives);
        sounds.LoadForMission(resources, loop.Natives);

        Assert.Equal(GameMaterialRegistry.WalkSoundCount, ground.WalkSounds.Count);
        Assert.All(ground.WalkSounds, sound =>
        {
            Assert.NotEqual(string.Empty, sound);
            Assert.True(sounds.TryResolve(sound, out _), $"'{sound}' is not a defined sound");
        });

        // And it is a real surface rather than one of the four Walk0 placeholders.
        Assert.DoesNotContain("Walk0", ground.WalkSounds);
    }

}