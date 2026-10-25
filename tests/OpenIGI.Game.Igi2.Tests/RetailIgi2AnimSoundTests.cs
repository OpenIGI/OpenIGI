using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The sounds baked into the clips: <c>animtrigger.qvm</c> defines each reload click as its own <c>AnimSound</c> trigger, which
/// <c>AnimTrigger_Trigger</c> sends to that type's handler whatever human's clip fires it.
/// </summary>
public sealed class RetailIgi2AnimSoundTests
{
    /// <summary>
    /// A soldier's clip firing the SPAS-12's reload trigger plays its click: of the 36 reload sounds the triggers name, the two
    /// SPAS-12 ones are all I.G.I. 2's own bank defines (the rest are Project I.G.I.'s, borrowed where its bank is at hand).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_reload_clips_sound_trigger_plays_its_sound()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(m.Level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Igi2Soldiers soldiers = Igi2Gameplay.SpawnSoldiers(
            m.Tree[0], m.Services!, m.Collision!, clips, m.Level.ModelFor, m.Services!.Require<Igi2PlayerTuning>(), m.Sounds, new IgiRandom(),
            Game.Ai.AiDifficulty.Normal);
        Igi2HumanSoldier body = soldiers.Bodies[0];
        Igi2AnimTriggers triggers = m.Services!.Require<Igi2AnimTriggers>();
        Igi2AnimTriggerType spas = triggers.Types.First(type => type.Parameters.Count > 0 && type.Parameters[0].ToString().Trim('"') == "spas12_reload_1");
        Assert.True(triggers.IsAnsweredBy(spas, "AnimSound"));

        body.TriggerFromBody(new Igi2AnimationTrigger(0, 0, spas.Id, default));

        Assert.Contains(m.Sounds.Playing, sound => sound.Name == "spas12_reload_1");
    }
}
