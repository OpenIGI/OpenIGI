using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>Which of a material's three dead-body sounds plays next, <c>iDeadBodySoundIndex</c>: one for the whole run of the game.</summary>
public sealed class Igi2DeadBodySoundCycle
{
    /// <summary>Gets or sets the next sound's place, nought to two.</summary>
    public int Next { get; set; }
}

/// <content>The sounds a human's own clips make: its footsteps and its body's fall.</content>
public abstract partial class Igi2Human
{
    /// <summary>The task type whose trigger handler answers footsteps and falls, <c>TASKTYPE_HUMAN</c>.</summary>
    private const string HumanTriggerTaskType = "Human";

    /// <summary>The share of the water's depth a wade sounds at, the float <c>0x3936DB6E</c>: full at about 5734 units deep.</summary>
    private const float WadeVolumePerUnit = 0.00017438616f;

    /// <summary>
    /// The order a human's steps go through a material's eight sounds, <c>Human_anFeetSoundSequence</c> (<c>igi2 0x63D1B0</c>): 31
    /// steps, then round again.
    /// </summary>
    private static readonly int[] FeetSoundSequence =
        [0, 1, 2, 3, 4, 5, 6, 7, 3, 6, 1, 4, 2, 0, 7, 5, 1, 3, 5, 7, 0, 2, 4, 6, 0, 2, 4, 7, 5, 1, 3];

    /// <summary>The steps in water, <c>azWaterFootStep</c> (<c>igi2 0x63D268</c>).</summary>
    private static readonly string[] WaterFootSteps =
        ["water_w_01", "water_w_02", "water_w_03", "water_w_04", "water_w_05", "water_w_06", "water_w_07", "water_w_08"];

    /// <summary>The wades, <c>azWaterWade</c> (<c>igi2 0x63D288</c>).</summary>
    private static readonly string[] WaterWades = ["wade_01", "wade_02"];

    /// <summary>Gets or sets the place in the step order the next step takes, <c>Human_s+0x7B8</c>.</summary>
    public int FeetSoundIndex { get; set; }

    /// <summary>Gets or sets the dead-body sound counter the game's humans share.</summary>
    public Igi2DeadBodySoundCycle DeadBodySounds { get; set; } = new();

    /// <summary>
    /// A trigger defined for every human, <c>Human_TriggerAnimTriggerHandler</c> (<c>igi2 0x503320</c>): a footstep, or the sound of
    /// the body landing; any other id defined for the type is fatal, as the original's is.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="triggers">The session's trigger types.</param>
    /// <returns>Whether the trigger's type is the human's, so this handler answered it.</returns>
    /// <exception cref="EngineFatalException">The id is defined for the human's type but not one it knows.</exception>
    protected bool TriggerHumanType(in Igi2AnimationTrigger trigger, Igi2AnimTriggers? triggers)
    {
        if (triggers is null || !triggers.TryGet(trigger.TriggerId, out Igi2AnimTriggerType type) || !triggers.IsAnsweredBy(type, HumanTriggerTaskType))
        {
            return false;
        }

        switch (trigger.TriggerId)
        {
            case Igi2HumanAnimTrigger.DeadBodySound:
                DeadBodySound();
                break;
            case Igi2HumanAnimTrigger.FeetSound:
                FeetSound();
                break;
            default:
                throw new EngineFatalException($"Unknown animtrigger for human: {trigger.TriggerId}");
        }

        return true;
    }

    /// <summary>
    /// A footstep: on the ground, alive, the next sound of the step order. In water, a splash and a wade as loud as the water is deep;
    /// on land the ground material's walking or running sound, quieter by half crouched. The player turning on the spot, standing
    /// or crouched, makes none.
    /// </summary>
    private void FeetSound()
    {
        if (GroundIntersection.HitObject is null || IsDead)
        {
            return;
        }

        int step = FeetSoundSequence[FeetSoundIndex];
        FeetSoundIndex++;
        if (FeetSoundIndex >= FeetSoundSequence.Length)
        {
            FeetSoundIndex = 0;
        }

        Vector3d at = GroundIntersection.Position;
        if (WaterDepth > 0f)
        {
            Surroundings.PlaySoundAt(WaterFootSteps[step], at);
            float volume = ((Fpu)WaterDepth * WadeVolumePerUnit).F;
            volume = volume < 0f ? 0f : volume > 1f ? 1f : volume;
            Surroundings.PlaySoundAt(WaterWades[step & 1], at)?.SetVolumes(-1f, volume);
            return;
        }

        if (IsTurningOnTheSpot || GroundMaterial() is not { } material)
        {
            return;
        }

        IReadOnlyList<string> sounds = IsRunning ? material.RunSounds : material.WalkSounds;
        if ((uint)step < (uint)sounds.Count && Surroundings.PlaySoundAt(sounds[step], at) is { } sound && Pose == Igi2HumanPose.Crouching)
        {
            sound.SetVolumes(-1f, 0.5f);
        }
    }

    /// <summary>The body landing as it falls dead: the ground material's next of its three dead-body sounds.</summary>
    private void DeadBodySound()
    {
        if (GroundIntersection.HitObject is null || GroundMaterial() is not { } material)
        {
            return;
        }

        int next = DeadBodySounds.Next;
        if ((uint)next < (uint)material.DeadBodyLandSounds.Count)
        {
            Surroundings.PlaySoundAt(material.DeadBodyLandSounds[next], GroundIntersection.Position);
        }

        DeadBodySounds.Next = (next + 1) % 3;
    }

    /// <summary>Gets a value indicating whether the player is turning on the spot, whose steps make no sound.</summary>
    protected virtual bool IsTurningOnTheSpot => false;

    /// <summary>The game material of the footing, the ground intersection's.</summary>
    private Igi2GameMaterial? GroundMaterial() =>
        (GroundIntersection.EnterMaterial ?? GroundIntersection.LeaveMaterial) is int row ? Surroundings.Material(row) : null;
}
