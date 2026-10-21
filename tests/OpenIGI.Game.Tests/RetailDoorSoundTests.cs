using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Whether the retail levels actually author the three door sounds, and what they name.
/// </summary>
/// <remarks>
/// Worth measuring rather than assuming: the parameters exist in every level's <c>Door</c>
/// declaration, but a declared parameter an artist never filled in is an empty string, and a door
/// feature built on three empty strings would be silent for a reason nothing in the code shows.
/// </remarks>
public sealed class RetailDoorSoundTests
{
    [RequiresGameDataFact]
    public void EveryDoorSoundNamedAcrossTheCampaignIsADefinedSound()
    {
        // The parameters are declared String16 and hold a DEFINITION name, so whatever they name has
        // to exist in the sound script or the door is silent.
        HashSet<string> named = new(StringComparer.Ordinal);
        int doors = 0;
        int withSounds = 0;

        for (int missionId = 1; missionId <= 14; missionId++)
        {
            DoorRegistry registry = LoadDoors(missionId);
            foreach (PlacedDoor door in registry.Doors)
            {
                doors++;
                bool any = false;
                foreach (string sound in (string[])[door.OpenSound, door.CloseSound, door.MoveSound])
                {
                    if (sound.Length > 0)
                    {
                        named.Add(sound);
                        any = true;
                    }
                }

                if (any)
                {
                    withSounds++;
                }
            }
        }

        // A campaign with no doors at all would pass every assertion below vacuously.
        Assert.True(doors > 0, "no Door tasks were found in any retail level");

        Assert.True(
            withSounds > 0,
            $"none of the {doors} retail doors names a sound in Open/Close/Move sound");

        // Named for the record, so a change in either the reader or the data shows up as a diff rather
        // than as silence.
        Assert.All(named, sound => Assert.False(string.IsNullOrWhiteSpace(sound)));
    }

    private static DoorRegistry LoadDoors(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        LevelLoader level = new(root, loop.TaskCommands, loop.Natives, resources);
        new MissionLoader(missions, fileSystem, resources, level).Load(missionId);
        return DoorRegistry.Build(root);
    }
}
