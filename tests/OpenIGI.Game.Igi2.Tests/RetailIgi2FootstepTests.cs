using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// A human's footsteps: the walk clip's <c>HUMANANIM_TRIGGER_FEETSOUND</c> triggers answered by <c>Human_TriggerAnimTriggerHandler</c>
/// (<c>igi2 0x503320</c>), each step the ground material's next walking sound in <c>Human_anFeetSoundSequence</c>'s order.
/// </summary>
public sealed class RetailIgi2FootstepTests
{
    /// <summary>A soldier walking over mission 1's ground plays its material's walking sounds, step after step, in the sequence's order.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_walking_soldier_sounds_its_steps_on_the_ground()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(m.Level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Igi2Soldiers soldiers = Igi2Gameplay.SpawnSoldiers(
            m.Tree[0], m.Services!, m.Collision!, clips, m.Level.ModelFor, m.Services!.Require<Igi2PlayerTuning>(), m.Sounds, new IgiRandom(),
            Game.Ai.AiDifficulty.Normal);
        Igi2HumanSoldier body = soldiers.Bodies.First(b => b.Weapon.Inventory is not null);
        WalkingMind mind = new();
        body.Mind = mind;
        for (int tick = 0; tick < 30; tick++)
        {
            body.RunDelayed();
        }

        Assert.NotNull(body.GroundIntersection.HitObject);
        int material = (body.GroundIntersection.EnterMaterial ?? body.GroundIntersection.LeaveMaterial)!.Value;
        Igi2GameMaterial ground = m.Materials[material]!;
        Assert.NotEmpty(ground.WalkSounds);

        mind.Frame[(int)Igi2SoldierChannel.Forward] = 1f;
        HashSet<string> heard = [];
        for (int tick = 0; tick < 150; tick++)
        {
            body.RunDelayed();
            foreach (Audio.Igi2Sound sound in m.Sounds.Playing)
            {
                heard.Add(sound.Name);
            }
        }

        Assert.Equal(Igi2SoldierState.WalkForward, body.Locomotion.CurrentState);
        Assert.True(body.FeetSoundIndex > 0);
        Assert.Contains(heard, ground.WalkSounds.Contains);
    }

    /// <summary>A mind that sends a fixed frame each tick, started.</summary>
    private sealed class WalkingMind : IIgi2SoldierMind
    {
        public float[] Frame { get; } = new float[Igi2HumanChannels.Count];

        public bool HasStarted => true;

        public void Run(Igi2HumanSoldier soldier) => soldier.Update(Frame);
    }
}
