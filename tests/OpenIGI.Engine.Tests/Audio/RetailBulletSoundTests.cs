using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Audio;

/// <summary>Checks the shared retail bullet and human-hit definitions through Level 1–5 sound mounts.</summary>
/// <remarks>Skipped when <c>IGI_GAME_PATH</c> is unset.</remarks>
public sealed class RetailBulletSoundTests
{
    private static readonly string[] SurfaceBulletSounds =
    [
        "bul_asphalt_1",
        "bul_carton_1",
        "bul_concrete_1",
        "bul_glass_1",
        "bul_gravel_1",
        "bul_metal_fnc_1",
        "bul_metal_hrd_1",
        "bul_metal_nrm_1",
        "bul_metal_sft_1",
        "bul_plastic_1",
        "bul_porc_1",
        "bul_rubber_1",
        "bul_rug_1",
        "bul_textile_1",
        "bul_wood_1",
        "bul_flesh_1",
    ];

    private static readonly string[] HumanHitSounds =
    [
        "ai_hit_1",
        "ai_hit_2",
        "ai_hit_3",
        "player_hit_1",
        "player_hit_2",
        "player_hit_3",
        "player_hit_4",
    ];

    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LevelsOneThroughFiveResolveSharedBulletAndHumanHitSounds(int level)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        fileSystem.SetAlias(
            VirtualFileSystem.MissionAlias,
            $"LOCAL:missions/location0/level{level}/");

        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        SoundDefinitionRegistry definitions = new();
        definitions.Register(loop.Natives);
        definitions.LoadForMission(resources, loop.Natives);

        SoundBank bank = new(fileSystem, null);
        Assert.True(bank.MountDirectory("LOCAL:common/sounds") > 0);
        Assert.True(bank.MountDirectory("MISSION:sounds") > 0);

        foreach (string name in SurfaceBulletSounds.Concat(HumanHitSounds))
        {
            Assert.True(definitions.TryResolve(name, out SoundDefinition? definition),
                $"Level {level}: retail definition '{name}' is absent.");
            Assert.NotNull(definition);
            Assert.True(bank.TryGet(definition!.BankKey, out _),
                $"Level {level}: definition '{name}' points at missing bank key '{definition.BankKey}'.");
        }
    }
}
