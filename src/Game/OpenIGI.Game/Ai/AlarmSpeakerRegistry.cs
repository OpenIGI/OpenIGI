using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.Ai;

/// <summary>One placed alarm emitter: a <c>Siren</c>, <c>AlarmLight</c>, <c>SCamera</c>, or spatial <c>ConditionalSound</c>.</summary>
/// <param name="Task">The placed task.</param>
/// <param name="PositionUnits">Where it hangs, in world units.</param>
/// <param name="AlarmExpression">The level expression that turns it on — usually a control's <c>isAlarm</c>.</param>
/// <param name="SirenSound">The sound it loops while on — the first of its two sound parameters.</param>
/// <param name="DestructionSound">The report it dies with — the second, which <c>0x43A5F0</c> hands its explosion.</param>
/// <param name="FollowsAnyAlarm">Whether it sounds while any alarm control is on, instead of by expression.</param>
/// <param name="CanBeDestroyed">Whether world damage can permanently silence this emitter.</param>
/// <param name="FaultySound">The loop a shot-out siren sounds instead while its expression holds, or empty.</param>
public sealed class AlarmSpeaker(
    DeclaredTask Task,
    Vector3d PositionUnits,
    string AlarmExpression,
    string SirenSound,
    string DestructionSound,
    bool FollowsAnyAlarm = false,
    bool CanBeDestroyed = true,
    string FaultySound = "") : Saves.IExchangedState
{
    /// <summary>The damage that breaks a security camera's lens.</summary>
    /// <remarks>
    /// <c>0x4415C0</c> writes <c>0.5</c> into the lens's <c>+264</c>; its hit handlers (<c>0x4422B0</c> for a
    /// round, <c>0x4422F0</c> for a blast) add to <c>+260</c> and flag the explosion once the sum reaches it.
    /// Retail's rounds carry 0.2 in Mission 1, so a camera takes three.
    /// </remarks>
    public const float CameraDestroyThreshold = 0.5f;

    /// <summary>The damage that blows a siren up.</summary>
    /// <remarks>
    /// A siren is an <c>ExplodeObject</c> (<c>0x449360</c>, parent <c>0x481340</c>). Its constructor (<c>0x4492A0</c>) hands
    /// <c>0x481360</c> 1.0 for <c>+496</c>, which the object's setup (<c>0x481C40</c>) copies into its threshold <c>+492</c>;
    /// sirens declare no explosion parameters to change it. A round (<c>0x481E60</c>) and a blast (<c>0x481EA0</c>, the
    /// blast flag <c>+564</c> left set by <c>0x481780</c>) add to <c>+488</c>, and the siren goes off, with no delay, once
    /// the sum reaches it: five of Mission 1's rounds.
    /// </remarks>
    public const float SirenDestroyThreshold = 1.0f;

    /// <summary>Gets the placed task.</summary>
    public DeclaredTask Task { get; } = Task;

    /// <summary>Gets where it hangs, in world units.</summary>
    public Vector3d PositionUnits { get; set; } = PositionUnits;

    /// <summary>Gets the level expression that turns it on.</summary>
    public string AlarmExpression { get; } = AlarmExpression;

    /// <summary>Gets the sound it loops while on.</summary>
    public string SirenSound { get; } = SirenSound;

    /// <summary>Gets the report it dies with.</summary>
    public string DestructionSound { get; } = DestructionSound;

    /// <summary>Gets the loop a shot-out siren sounds instead of its working one while its expression holds.</summary>
    /// <remarks>
    /// Siren's update (<c>0x4494D0</c>) keeps evaluating its On expression after the ExplodeObject it derives from has
    /// exploded (<c>+400</c>): it stops the working sound (<c>+568</c>) and starts the faulty one (<c>+600</c>) instead.
    /// The shipped sirens name <c>alarmsystem_working</c> for both, so a siren that is shot out still wails.
    /// </remarks>
    public string FaultySound { get; } = FaultySound;

    /// <summary>Gets whether it sounds while any alarm control is on, instead of by its own expression.</summary>
    /// <remarks>
    /// The security camera's alarm loop (<c>0x441AE0</c>) follows its control's alarm byte rather than a
    /// declared expression — the port stands the mission-wide alarm state in for that byte.
    /// </remarks>
    public bool FollowsAnyAlarm { get; } = FollowsAnyAlarm;

    /// <summary>Gets whether world damage can permanently silence this emitter.</summary>
    public bool CanBeDestroyed { get; } = CanBeDestroyed;

    /// <summary>Gets or sets whether an alarm light was on at its last run.</summary>
    public bool WasOn { get; set; }

    /// <summary>Gets or sets whether an alarm light wears its off model: only once it has been on and gone off.</summary>
    /// <remarks><c>0x43A450</c> gives it its on model as it is made; <c>0x43A270</c> changes model only on a change of state.</remarks>
    public bool ShowsOff { get; set; }

    /// <summary>Gets or sets whether the speaker has been shot out — retail's <c>+417</c>.</summary>
    /// <remarks>Permanent: a destroyed speaker never sounds its working loop again; only a siren's faulty loop is left.</remarks>
    public bool IsDestroyed { get; set; }

    /// <summary>Gets whether damage adds up before it breaks, as a camera lens's and a siren's does.</summary>
    /// <remarks>
    /// A security camera's lens sums every round's and blast's damage (<c>+260</c>) and explodes once the sum reaches
    /// <see cref="CameraDestroyThreshold"/>. A siren is an <c>ExplodeObject</c> and does the same
    /// (<c>+488</c> against <c>+492</c>) at <see cref="SirenDestroyThreshold"/>. An alarm light breaks
    /// on its first hit.
    /// </remarks>
    public bool AccumulatesDamage => DestroyThreshold > 0f;

    /// <summary>Gets the damage that breaks it, or zero for an emitter the first hit breaks.</summary>
    public float DestroyThreshold =>
        string.Equals(Task.Type.Name, WorldScene.SecurityCameraType, StringComparison.Ordinal)
            ? CameraDestroyThreshold
            : string.Equals(Task.Type.Name, WorldScene.SirenType, StringComparison.Ordinal)
                ? SirenDestroyThreshold
                : 0f;

    /// <summary>Gets the damage taken so far — the lens's <c>+260</c>.</summary>
    public float Damage { get; set; }

    /// <summary>Gets the radius a blast's falloff is measured from, in engine units.</summary>
    /// <remarks>
    /// <c>0x4422F0</c> hands <c>0x416C90</c> the lens's largest extent (<c>1</c>) times its head model's bound
    /// (<c>0x4D0950</c>, <c>MESH+0x78</c>) — 2863.59 units for the intact <c>313_02_1</c>, as the live game
    /// reports.
    /// </remarks>
    public float ExplosionContactRadiusUnits { get; set; } = 1f;

    // As the match began: where it stood, whether it sounded, was shot out, and the damage it had taken.
    private (Vector3d Position, bool WasOn, bool ShowsOff, bool IsDestroyed, float Damage) _start;

    /// <summary>Marks the state the level starts in, to be put back by <see cref="ResetToLevelStart"/>.</summary>
    public void MarkLevelStart() => _start = (PositionUnits, WasOn, ShowsOff, IsDestroyed, Damage);

    /// <summary>Puts back the state the level started in.</summary>
    public void ResetToLevelStart() => (PositionUnits, WasOn, ShowsOff, IsDestroyed, Damage) = _start;

    // Not retail: Project I.G.I. kept no save games. What a level reset puts back is what a save keeps, through the same
    // record: the level's start is set aside, the record passed through, and put back when a save is read.
    void Saves.IExchangedState.Exchange(Saves.StateExchange exchange)
    {
        var levelStart = _start;
        MarkLevelStart();
        _start = (
            exchange.Position(_start.Position),
            exchange.Bool(_start.WasOn),
            exchange.Bool(_start.ShowsOff),
            exchange.Bool(_start.IsDestroyed),
            exchange.Real(_start.Damage));
        if (exchange.IsReading)
        {
            ResetToLevelStart();
        }

        _start = levelStart;
    }
}
