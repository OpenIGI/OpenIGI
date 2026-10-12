using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The soldier script's natives, <c>AIAction_*</c> and <c>AIFunction_*</c> as <c>HumanAI_RegisterScriptFunctions</c> registers them:
/// each works on the mind whose script is running (<c>HumanAI_ptCurrentHumanAI</c>) and the event it runs for.
/// </summary>
/// <remarks>
/// <para>
/// An <c>AIAction_*</c> asks for an action: it takes a state for it, fills the state from its arguments and the event, and lists the
/// action for the mind to push once the script is done (<c>HumanAI_AddActionToLocalList</c>), at the rank the script runs at. Its last
/// argument is always the action's flags, bit 1 making it pushable. With no state free the action is not asked for.
/// </para>
/// <para>
/// An <c>AIFunction_Set*</c> tuning value is scaled as the original stores it: seconds to ticks (30), metres to world units (4096),
/// per cent to a share, degrees to radians. The accuracy setters recompute the factors the accuracy curve is drawn with.
/// </para>
/// </remarks>
public sealed class Igi2HumanAiNatives
{
    /// <summary>Degrees to radians, as the single-precision constant (<c>0.017453293</c>).</summary>
    private const double DegreesToRadiansSingle = 0.017453293005625408;

    /// <summary>Degrees to radians, as the double-precision constant the investigating cones use.</summary>
    private const double DegreesToRadiansDouble = 0.01745329238474369;

    /// <summary>The widest view cone a tuning allows, a quarter turn as a single.</summary>
    private const double QuarterTurn = 1.5707963705062866;

    /// <summary>How many animation entries a mind holds.</summary>
    private const int AnimationEntryCapacity = 5;

    private readonly Igi2AiWorld _world;

    /// <summary>Initializes a new instance of the <see cref="Igi2HumanAiNatives"/> class.</summary>
    /// <param name="world">The AI module.</param>
    public Igi2HumanAiNatives(Igi2AiWorld world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    private static QvmValue Done => QvmValue.FromInt32(1);

    private Igi2HumanAi Ai => _world.Current ?? throw new InvalidOperationException("No soldier script is running.");

    /// <summary>Registers the natives.</summary>
    /// <param name="natives">The namespace.</param>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        RegisterTunings(natives);
        natives.RegisterFunction("AIFunction_UseCombatView", call => Run(() => Ai.UseCombatView()));
        natives.RegisterFunction("AIFunction_UseIdleView", call => Run(() => Ai.UseIdleView()));
        natives.RegisterFunction("AIFunction_SetStandActionAnimation", SetStandActionAnimation);
        natives.RegisterFunction("AIFunction_HasTarget", call => Bool(Ai.GetTarget(visibleOnly: true) is not null));
        natives.RegisterFunction("AIFunction_LimitViewRange", call => SetFlag(call, 0x20, whenTrue: false));
        natives.RegisterFunction("AIFunction_SetSideKick", call => SetFlag(call, 1, whenTrue: true));
        natives.RegisterFunction("AIFunction_HasSeenDeadFriendlyBefore", call => QvmValue.FromInt32(Ai.Flags & 2));
        natives.RegisterFunction("AIFunction_DefaultHandler", DefaultHandler);
        natives.RegisterFunction("AIFunction_RemoveAlarmActions", RemoveAlarmActions);
        natives.RegisterFunction("AIFunction_SetEventPriority", call => WithInt(call, 1, kind => Ai.Events.Prioritize(kind)));
        natives.RegisterFunction("AIFunction_SetInvulnerability", call => WithInt(call, 1, value => Ai.IsInvulnerable = (byte)value != 0));
        natives.RegisterFunction("AIFunction_SetInstantDeath", call => WithInt(call, 1, value => Ai.IsInstantDeathDisabled = (byte)value == 0));
        natives.RegisterFunction("AIFunction_SetDeathAnimation", call => WithInt(call, 1, value => Ai.DeathAnimation = value));
        natives.RegisterFunction("AIFunction_SetAlarmAccess", call => WithInt(call, 1, value => Ai.AlarmAccessMode = value));
        natives.RegisterFunction("AIFunction_SetGunnerID", call => WithInt(call, 1, value => Ai.GunnerId = value));
        natives.RegisterFunction("AIFunction_GetGunnerID", call => QvmValue.FromInt32(Ai.GunnerId));
        natives.RegisterFunction("AIFunction_GetGunnerStatus", GetGunnerStatus);
        natives.RegisterFunction("AIFunction_SetScriptIntegerValue", SetScriptIntegerValue);
        natives.RegisterFunction("AIFunction_SetScriptRealValue", SetScriptRealValue);
        natives.RegisterFunction("AIFunction_GetScriptIntegerValue", GetScriptIntegerValue);
        natives.RegisterFunction("AIFunction_GetScriptRealValue", GetScriptRealValue, returnType: QvmValueKind.Float);
        natives.RegisterFunction("AIFunction_GetCurrentEventType", call => QvmValue.FromInt32(CurrentEvent("EventStack empty").Event));
        natives.RegisterFunction("AIFunction_IsEventBehind", IsEventBehind);
        natives.RegisterFunction("AIFunction_GetRandomValue", GetRandomValue, returnType: QvmValueKind.Float);
        natives.RegisterFunction("AIFunction_GetEventDistance", GetEventDistance, returnType: QvmValueKind.Float);
        natives.RegisterFunction("AIFunction_SetAnimationInterval", SetAnimationInterval);
        natives.RegisterFunction("AIFunction_AddAnimationEntry", AddAnimationEntry);
        natives.RegisterFunction("AIFunction_GetAnimationToPlay", GetAnimationToPlay);
        natives.RegisterFunction("AIFunction_SendResponse", SendResponse);
        natives.RegisterFunction("AIFunction_PassEventOnToSquad", PassEventOnToSquad);
        RegisterActions(natives);

        // HumanAI_Open binds HumanAI_nScriptEvent, which nothing in the client writes: nought unless a script sets it.
        natives.RegisterVariable("HumanAI_nEvent", new QvmIntegerVariable(() => _world.ScriptEvent, value => _world.ScriptEvent = value));
    }

    private static QvmValue Bool(bool value) => QvmValue.FromInt32(value ? 1 : 0);

    private static QvmValue Run(Action action)
    {
        action();
        return Done;
    }

    private static QvmValue WithInt(QvmCallContext call, int count, Action<int> set)
    {
        if (call.ArgumentCount == count)
        {
            set(call.GetInt32(0));
        }

        return Done;
    }

    private static QvmValue WithFloat(QvmCallContext call, Action<float> set)
    {
        if (call.ArgumentCount == 1)
        {
            set(call.GetSingle(0));
        }

        return Done;
    }

    /// <summary>The string a native stores, held to the 31 characters of its 32-byte buffer.</summary>
    private static string Buffer32(string value) => value.Length > 31 ? value[..31] : value;

    /// <summary>Ticks from seconds, <c>(__int64)(30.0 * v)</c>.</summary>
    private static int Ticks(float seconds) => Fpu.ToInt32((Fpu)30.0 * seconds);

    private Igi2AiEventRecord CurrentEvent(string message) => _world.CurrentEvent ?? throw new EngineFatalException(message);

    // --- Tunings (HumanAI_Set*CB) ------------------------------------------------------------------------------------------------

    private void RegisterTunings(NativeRegistry natives)
    {
        natives.RegisterFunction("AIFunction_SetMinRangeAccuracy", call => WithFloat(call, SetMinRangeAccuracy));
        natives.RegisterFunction("AIFunction_SetMedRangeAccuracy", call => WithFloat(call, SetMedRangeAccuracy));
        natives.RegisterFunction("AIFunction_SetMaxRangeAccuracy", call => WithFloat(call, SetMaxRangeAccuracy));
        natives.RegisterFunction("AIFunction_SetAccuracyMedRange", call => WithFloat(call, SetAccuracyMedRange));
        natives.RegisterFunction("AIFunction_SetAccuracyMaxRange", call => WithFloat(call, SetAccuracyMaxRange));
        Scaled(natives, "AIFunction_SetPercentageFiredPerBurst", Igi2AiTweakIndex.PercentageFiredPerBurst, 0.009999999776482582);
        Scaled(natives, "AIFunction_SetEvasiveActionProb", Igi2AiTweakIndex.EvasiveActionProb, 0.009999999776482582);
        Scaled(natives, "AIFunction_SetMinDelayBetweenFiring", Igi2AiTweakIndex.MinDelayBetweenFiring, 30.0);
        Scaled(natives, "AIFunction_SetTrackingTimeout", Igi2AiTweakIndex.TrackingTimeout, 30.0);
        Scaled(natives, "AIFunction_SetTrackingMaxDistance", Igi2AiTweakIndex.TrackingMaxDistance, 4096.0);
        Scaled(natives, "AIFunction_SetGrenadeThrowProb", Igi2AiTweakIndex.GrenadeThrowProb, 0.009999999776482582);
        Scaled(natives, "AIFunction_SetGrenadeDetonationTimer", Igi2AiTweakIndex.GrenadeDetonationTimer, 30.0);
        Scaled(natives, "AIFunction_SetGrenadeLandingReactionProb", Igi2AiTweakIndex.GrenadeLandingReactionProb, 0.009999999776482582);
        Scaled(natives, "AIFunction_SetCloseCombatDamage", Igi2AiTweakIndex.CloseCombatDamage, 1.0);
        natives.RegisterFunction("AIFunction_SetHitPoints", call => WithFloat(call, SetHitPoints));
        Scaled(natives, "AIFunction_SetHitRecoilThreshold", Igi2AiTweakIndex.HitRecoilThreshold, 1.0);
        Angle(natives, "AIFunction_SetCombatViewCone1Alpha", Igi2AiTweakIndex.CombatViewCone1Alpha);
        Scaled(natives, "AIFunction_SetCombatViewCone1Length", Igi2AiTweakIndex.CombatViewCone1Length, 4096.0);
        Angle(natives, "AIFunction_SetCombatViewCone2Alpha", Igi2AiTweakIndex.CombatViewCone2Alpha);
        Scaled(natives, "AIFunction_SetCombatViewCone2Length", Igi2AiTweakIndex.CombatViewCone2Length, 4096.0);
        Angle(natives, "AIFunction_SetIdleViewCone1Alpha", Igi2AiTweakIndex.IdleViewCone1Alpha);
        Scaled(natives, "AIFunction_SetIdleViewCone1Length", Igi2AiTweakIndex.IdleViewCone1Length, 4096.0);
        Angle(natives, "AIFunction_SetIdleViewCone2Alpha", Igi2AiTweakIndex.IdleViewCone2Alpha);
        Scaled(natives, "AIFunction_SetIdleViewCone2Length", Igi2AiTweakIndex.IdleViewCone2Length, 4096.0);
        Scaled(natives, "AIFunction_SetIdleAnimationFrequency", Igi2AiTweakIndex.IdleAnimationFrequency, 30.0);
        Scaled(natives, "AIFunction_SetInvestigateViewCone1Alpha", Igi2AiTweakIndex.InvestigateViewCone1Alpha, DegreesToRadiansDouble);
        Scaled(natives, "AIFunction_SetInvestigateViewCone1Length", Igi2AiTweakIndex.InvestigateViewCone1Length, 4096.0);
        Scaled(natives, "AIFunction_SetInvestigateViewCone2Alpha", Igi2AiTweakIndex.InvestigateViewCone2Alpha, DegreesToRadiansDouble);
        Scaled(natives, "AIFunction_SetInvestigateViewCone2Length", Igi2AiTweakIndex.InvestigateViewCone2Length, 4096.0);
        Scaled(natives, "AIFunction_SetInvestigateShoutFrequency", Igi2AiTweakIndex.InvestigateShoutFrequency, 30.0);
        Scaled(natives, "AIFunction_SetAnimSpeedFactor", Igi2AiTweakIndex.AnimSpeedFactor, 1.0);
        natives.RegisterFunction(
            "AIFunction_SetHearingRangeFactor",
            call => WithFloat(call, value => Ai.HearingRangeFactor = Math.Clamp(value, 0f, 1f)));
        natives.RegisterFunction(
            "AIFunction_SetViewLength",
            call => WithFloat(call, value => Ai.View.ConeLength = ((Fpu)value * 4096.0).F));
        natives.RegisterFunction(
            "AIFunction_SetViewAlpha",
            call => WithFloat(call, value => Ai.View.ConeAlpha = ((Fpu)value * DegreesToRadiansDouble).F));
        natives.RegisterFunction(
            "AIFunction_SetViewGamma",
            call => WithFloat(call, value => Ai.View.ConeGamma = ((Fpu)value * DegreesToRadiansDouble).F));
        natives.RegisterFunction(
            "AIFunction_SetSecondaryViewLength",
            call => WithFloat(call, value => Ai.View.SecondaryConeLength = ((Fpu)value * 4096.0).F));
        natives.RegisterFunction(
            "AIFunction_SetSecondaryViewAlpha",
            call => WithFloat(call, value => Ai.View.SecondaryConeAlpha = ((Fpu)value * DegreesToRadiansDouble).F));
        natives.RegisterFunction(
            "AIFunction_SetSecondaryViewGamma",
            call => WithFloat(call, value => Ai.View.SecondaryConeGamma = ((Fpu)value * DegreesToRadiansDouble).F));
    }

    /// <summary>A tuning stored as its argument times a scale.</summary>
    private void Scaled(NativeRegistry natives, string name, int index, double scale) =>
        natives.RegisterFunction(name, call => WithFloat(call, value => Ai.Tweaks[index] = ((Fpu)value * scale).F));

    /// <summary>A cone's angle, from degrees held to between nought and a quarter turn.</summary>
    private void Angle(NativeRegistry natives, string name, int index) =>
        natives.RegisterFunction(name, call => WithFloat(call, value =>
        {
            double angle = ((Fpu)value * DegreesToRadiansSingle).Value;
            Ai.Tweaks[index] = (float)(angle >= 0.0 ? Math.Min(angle, QuarterTurn) : 0.0);
        }));

    /// <summary>A share held between a floor and one, from per cent.</summary>
    private static float Share(float value, float floor)
    {
        float share = ((Fpu)value * 0.009999999776482582).F;
        return share >= floor ? Math.Min(share, 1f) : floor;
    }

    /// <summary>The near factor, <c>(medium / close) ^ (1 / medium range)</c>.</summary>
    private static void NearFactor(float[] t) =>
        t[Igi2AiTweakIndex.MedRangeAccuracyFactor] = (float)Math.Pow(
            ((Fpu)t[Igi2AiTweakIndex.MedRangeAccuracy] / t[Igi2AiTweakIndex.MinRangeAccuracy]).Value,
            ((Fpu)1.0 / t[Igi2AiTweakIndex.AccuracyMedRange]).Value);

    /// <summary>The far factor, <c>(long / medium) ^ (1 / (long range - medium range))</c>.</summary>
    private static void FarFactor(float[] t) =>
        t[Igi2AiTweakIndex.MaxRangeAccuracyFactor] = (float)Math.Pow(
            ((Fpu)t[Igi2AiTweakIndex.MaxRangeAccuracy] / t[Igi2AiTweakIndex.MedRangeAccuracy]).Value,
            ((Fpu)1.0 / ((Fpu)t[Igi2AiTweakIndex.AccuracyMaxRange] - t[Igi2AiTweakIndex.AccuracyMedRange])).Value);

    private void SetMinRangeAccuracy(float value)
    {
        float[] t = Ai.Tweaks;
        t[Igi2AiTweakIndex.MinRangeAccuracy] = Share(value, 0.01f);
        NearFactor(t);
    }

    private void SetMedRangeAccuracy(float value)
    {
        float[] t = Ai.Tweaks;
        t[Igi2AiTweakIndex.MedRangeAccuracy] = Share(value, 0.01f);
        NearFactor(t);
        FarFactor(t);
    }

    private void SetMaxRangeAccuracy(float value)
    {
        float[] t = Ai.Tweaks;
        t[Igi2AiTweakIndex.MaxRangeAccuracy] = Share(value, 0f);
        FarFactor(t);
    }

    /// <summary>
    /// <c>HumanAI_SetAccuracyMedRangeCB</c>: as retail has it, the value goes to the long range, not the middle one; both factors are
    /// drawn again.
    /// </summary>
    private void SetAccuracyMedRange(float value)
    {
        float[] t = Ai.Tweaks;
        double range = ((Fpu)value * 4096.0).Value;
        t[Igi2AiTweakIndex.AccuracyMaxRange] = (float)(range <= 1.0 ? 1.0 : range);
        NearFactor(t);
        FarFactor(t);
    }

    private void SetAccuracyMaxRange(float value)
    {
        float[] t = Ai.Tweaks;
        float range = ((Fpu)value * 4096.0).F;
        t[Igi2AiTweakIndex.AccuracyMaxRange] = range <= 1.0 ? 1f : range;
        FarFactor(t);
    }

    /// <summary>The hit points, which also become the body's most damage.</summary>
    private void SetHitPoints(float value)
    {
        Igi2HumanAi ai = Ai;
        ai.Tweaks[Igi2AiTweakIndex.HitPoints] = value;
        ai.Soldier.Damage.MaxDamage = value;
    }

    // --- Settings and questions ----------------------------------------------------------------------------------------------

    private QvmValue SetFlag(QvmCallContext call, int bit, bool whenTrue) => WithInt(call, 1, value =>
    {
        Igi2HumanAi ai = Ai;
        ai.Flags = (value != 0) == whenTrue ? ai.Flags | bit : ai.Flags & ~bit;
    });

    private QvmValue SetStandActionAnimation(QvmCallContext call)
    {
        if (call.ArgumentCount == 1)
        {
            Ai.StandActionAnimation = Buffer32(call.GetString(0));
        }

        return Done;
    }

    private QvmValue DefaultHandler(QvmCallContext call)
    {
        if (_world.Default is { } script)
        {
            _world.Execute(script);
        }

        return Done;
    }

    /// <summary>
    /// Pops every action ranked at the value the eighth place of the event ranking holds, each with its Delete
    /// (<c>HumanAI_RemoveAlarmActionsCB</c>): the original compares the action's rank with that kind.
    /// </summary>
    private QvmValue RemoveAlarmActions(QvmCallContext call)
    {
        Igi2HumanAi ai = Ai;
        int alarmOn = ai.Events.KindAt(7);
        foreach (Igi2AiActionItem item in ai.Actions.Items.ToArray())
        {
            if (item.Priority == alarmOn)
            {
                ai.Actions.Pop(ai, item);
            }
        }

        return Done;
    }

    /// <summary>Whether its gun is manned; nought when it has no gun (<c>HumanAI_GetGunnerStatusCB</c>).</summary>
    private QvmValue GetGunnerStatus(QvmCallContext call)
    {
        int id = Ai.GunnerId;
        return (uint)id < 0x1000u && _world.StationaryGun(id) is { } gun ? Bool(gun.IsManned) : QvmValue.FromInt32(0);
    }

    private QvmValue SetScriptIntegerValue(QvmCallContext call)
    {
        if (call.ArgumentCount == 2 && (uint)call.GetInt32(0) < (uint)Ai.ScriptIntegers.Length)
        {
            Ai.ScriptIntegers[call.GetInt32(0)] = call.GetInt32(1);
        }

        return Done;
    }

    private QvmValue SetScriptRealValue(QvmCallContext call)
    {
        if (call.ArgumentCount == 2 && (uint)call.GetInt32(0) < (uint)Ai.ScriptReals.Length)
        {
            Ai.ScriptReals[call.GetInt32(0)] = call.GetSingle(1);
        }

        return Done;
    }

    private QvmValue GetScriptIntegerValue(QvmCallContext call)
    {
        int index = call.GetInt32(0);
        return QvmValue.FromInt32((uint)index < (uint)Ai.ScriptIntegers.Length ? Ai.ScriptIntegers[index] : 0);
    }

    private QvmValue GetScriptRealValue(QvmCallContext call)
    {
        int index = call.GetInt32(0);
        return QvmValue.FromDouble((uint)index < (uint)Ai.ScriptReals.Length ? Ai.ScriptReals[index] : 0.0);
    }

    /// <summary>Whether any waiting event of the kind running lies behind the eyes (<c>HumanAI_IsEventBehindCB</c>).</summary>
    private QvmValue IsEventBehind(QvmCallContext call)
    {
        Igi2HumanAi ai = Ai;
        if (_world.CurrentEventKind < 0)
        {
            return Bool(false);
        }

        float[] o = ai.View.ViewOrientation;
        Vector3d eye = ai.View.ViewPosition;
        foreach (Igi2AiEventRecord record in ai.Events.Of(_world.CurrentEventKind))
        {
            Fpu ahead = ((Fpu)o[7] * ((Fpu)record.Position.Z - eye.Z)) + ((Fpu)o[1] * ((Fpu)record.Position.X - eye.X))
                + ((Fpu)o[4] * ((Fpu)record.Position.Y - eye.Y));
            if (ahead.Value < 0.0)
            {
                return Bool(true);
            }
        }

        return Bool(false);
    }

    /// <summary>A drawn value from nought up to its argument: a draw's low sixteen bits as a share (<c>HumanAI_GetRandomValueCB</c>).</summary>
    private QvmValue GetRandomValue(QvmCallContext call)
    {
        if (call.ArgumentCount != 1)
        {
            return QvmValue.FromDouble(1.0);
        }

        float top = call.GetSingle(0);
        uint draw = _world.Random.NextRaw();
        return QvmValue.FromDouble(((Fpu)(double)(ushort)draw * 0.0000152587890625 * top).Value);
    }

    /// <summary>How far the event is from the eyes (<c>HumanAI_GetEventDistanceCB</c>).</summary>
    private QvmValue GetEventDistance(QvmCallContext call)
    {
        Igi2AiEventRecord record = CurrentEvent("No EVENT to distance");
        Vector3d eye = Ai.View.ViewPosition;
        Fpu x = (Fpu)record.Position.X - eye.X, y = (Fpu)record.Position.Y - eye.Y, z = (Fpu)record.Position.Z - eye.Z;
        return QvmValue.FromDouble(Fpu.Sqrt((z * z) + (y * y) + (x * x)).Value);
    }

    /// <summary>
    /// Sets how often an idle animation may play (<c>HumanAI_SetAnimationIntervalCB</c>): at least the first argument's seconds, plus
    /// a drawn share of the second's; the delay is drawn even though nothing reads it.
    /// </summary>
    private QvmValue SetAnimationInterval(QvmCallContext call)
    {
        if (call.ArgumentCount != 2)
        {
            return Done;
        }

        Igi2HumanAi ai = Ai;
        ai.AnimationMinimumDelay = Ticks(call.GetSingle(0));
        int range = Ticks(call.GetSingle(1));
        ai.AnimationRandomDelay = range;
        uint draw = _world.Random.NextRaw();
        ai.AnimationDelay = ai.AnimationMinimumDelay + (int)(range != 0 ? draw % (uint)range : 0u);
        return Done;
    }

    /// <summary>Adds an animation and its weight, at most five (<c>HumanAI_AddAnimationEntryCB</c>).</summary>
    private QvmValue AddAnimationEntry(QvmCallContext call)
    {
        if (call.ArgumentCount == 2 && Ai.AnimationEntries.Count < AnimationEntryCapacity)
        {
            Ai.AnimationEntries.Add((call.GetInt32(0), call.GetSingle(1)));
        }

        return Done;
    }

    /// <summary>
    /// Draws one of the animations by weight (<c>HumanAI_GetAnimationToPlayCB</c>): with weights summing to less than a hundredth,
    /// any one alike; with none, -1.
    /// </summary>
    private QvmValue GetAnimationToPlay(QvmCallContext call)
    {
        List<(int Animation, double Weight)> entries = Ai.AnimationEntries;
        if (entries.Count <= 0)
        {
            return QvmValue.FromInt32(-1);
        }

        Fpu sum = 0.0;
        foreach ((int _, double weight) in entries)
        {
            sum += weight;
        }

        uint draw = _world.Random.NextRaw();
        if (sum.Value < 0.01)
        {
            return QvmValue.FromInt32(entries[(int)(draw % (uint)entries.Count)].Animation);
        }

        Fpu left = sum * ((Fpu)(double)(ushort)draw * 0.0000152587890625);
        for (int i = 0; i < entries.Count; i++)
        {
            left -= entries[i].Weight;
            if (left.Value <= 0.0)
            {
                return QvmValue.FromInt32(entries[i].Animation);
            }
        }

        return QvmValue.FromInt32(entries[^1].Animation);
    }

    /// <summary>Answers the soldier the event is about with event 6 (<c>HumanAI_SendResponseCB</c>).</summary>
    private QvmValue SendResponse(QvmCallContext call)
    {
        Igi2AiEventRecord record = CurrentEvent("No EVENT to respond");
        if (record.Reference is Soldiers.Igi2HumanSoldier { Mind: Igi2HumanAi other })
        {
            other.Receive(new Igi2AiEvent(6, -1, 0x20, 0f, null, default, null));
        }

        return Done;
    }

    /// <summary>
    /// Hands the event to the squad at the script's rank, with this soldier as the one who heard it (<c>HumanAI_PassEventOnToSquadCB</c>):
    /// about whoever raised it, or else a human it concerns; not when the soldier is dead or has no squad.
    /// </summary>
    private QvmValue PassEventOnToSquad(QvmCallContext call)
    {
        Igi2HumanAi ai = Ai;
        if (ai.Squad is not { } squad || ai.Soldier.IsDead || _world.CurrentEvent is not { } record)
        {
            return QvmValue.FromInt32(0);
        }

        Igi2AiSquadEvent passed = new()
        {
            Kind = record.Event,
            Priority = _world.HandlerPriority,
            Position = record.Position,
            Direction = record.Direction,
            Reference = record.Originator ?? (record.Reference as Igi2Human),
            SpecificInt = record.Specific is int specific ? specific : 0,
        };
        passed.Receivers.Add(ai);
        squad.ReceiveEvent(passed);
        return QvmValue.FromInt32(0);
    }

    // --- Actions (HumanAI_*CB that list an action) -----------------------------------------------------------------------------

    private void RegisterActions(NativeRegistry natives)
    {
        natives.RegisterFunction("AIAction_DelayedEventPassOn", DelayedEventPassOn);
        natives.RegisterFunction("AIAction_Patrol", Patrol);
        natives.RegisterFunction("AIAction_Dead", call => Ask(call, 1, null, Igi2AiContent.DeadInit));
        natives.RegisterFunction("AIAction_FallFlat", FallFlat);
        natives.RegisterFunction("AIAction_Activate", Activate);
        natives.RegisterFunction(
            "AIAction_WalkToNode",
            call => Ask(call, 2, () => new Igi2AiContent.NodeContext { Node = call.GetInt32(0) }, Igi2AiContent.WalkToNodeInit));
        natives.RegisterFunction(
            "AIAction_RunToNode",
            call => Ask(call, 2, () => new Igi2AiContent.NodeContext { Node = call.GetInt32(0) }, Igi2AiContent.RunToNodeInit));
        natives.RegisterFunction("AIAction_FireAtNode", call => FireAt(call, Igi2AiContent.FireAtNodeInit));
        natives.RegisterFunction("AIAction_FireAtTask", call => FireAt(call, Igi2AiContent.FireAtTaskInit));
        natives.RegisterFunction(
            "AIAction_PlayAnimation",
            call => Ask(call, 2, () => new Igi2AiContent.AnimationContext { IsLoop = (byte)call.GetInt32(0) != 0 }, Igi2AiContent.PlayAnimationInit));
        natives.RegisterFunction(
            "AIAction_PlaySound",
            call => Ask(
                call,
                3,
                () => new Igi2AiContent.SoundContext { Sound = Buffer32(call.GetString(0)), IsAttached = (byte)call.GetInt32(1) != 0 },
                Igi2AiContent.PlaySoundInit));
        natives.RegisterFunction(
            "AIAction_MoveToEvent",
            call => Ask(
                call,
                2,
                () => new Igi2AiContent.EventMoveContext { Position = EventPosition(), IsRun = (byte)call.GetInt32(0) != 0 },
                Igi2AiContent.MoveToEventInit));
        natives.RegisterFunction(
            "AIAction_LookAtEvent",
            call => Ask(
                call,
                2,
                () => new Igi2AiContent.LookContext { Position = EventPosition(), SecondsToLook = call.GetSingle(0) },
                Igi2AiContent.LookAtEventInit));
        natives.RegisterFunction("AIAction_LookInApproximateEventDirection", LookInApproximateEventDirection);
        natives.RegisterFunction(
            "AIAction_Stunned",
            call => Ask(call, 2, () => new Igi2AiContent.TimedContext { Seconds = call.GetSingle(0) }, Igi2AiContent.StunnedInit));
        natives.RegisterFunction("AIAction_KickGrenade", KickGrenade);
        natives.RegisterFunction("AIAction_RunPanicking", RunPanicking);
        natives.RegisterFunction("AIAction_Idle", call => Ask(call, 1, static () => new Igi2AiContent.IdleContext(), Igi2AiContent.IdleInit));
        natives.RegisterFunction(
            "AIAction_CombatIdle",
            call => Ask(call, 1, static () => new Igi2AiContent.CombatIdleContext(), Igi2AiContent.CombatIdleInit));
        natives.RegisterFunction(
            "AIAction_LookAround",
            call => Ask(call, 2, static () => new Igi2AiContent.LookAroundContext(), Igi2AiContent.LookAroundInit));
        natives.RegisterFunction("AIAction_CallForBackup", CallForBackup);
        natives.RegisterFunction("AIAction_EnemyDetection", EnemyDetection);
        natives.RegisterFunction("AIAction_LookAtNode", LookAtNode);
        natives.RegisterFunction(
            "AIAction_FriendlyDetection",
            call => Ask(
                call,
                1,
                () => new Igi2AiContent.NodeContext { Position = EventPosition() },
                Igi2AiContent.DeadFriendlyDetectionInit));
        natives.RegisterFunction(
            "AIAction_RunToCover",
            call => Ask(
                call,
                2,
                () => new Igi2AiContent.NodeContext { Node = call.GetInt32(0), Position = EventPosition() },
                Igi2AiContent.RunToCoverInit));
        natives.RegisterFunction(
            "AIAction_Wait",
            call => Ask(call, 2, () => new Igi2AiContent.WaitContext { Timer = Ticks(call.GetSingle(0)) }, Igi2AiContent.WaitInit));
        natives.RegisterFunction(
            "AIAction_OffensiveCombat",
            call => Ask(call, 1, static () => new Igi2AiContent.NodeContext(), Igi2AiContent.OffensiveCombatInit));
        natives.RegisterFunction(
            "AIAction_DefensiveCombat",
            call => Ask(call, 1, static () => new Igi2AiContent.NodeContext(), Igi2AiContent.DefensiveCombatInit));
    }

    /// <summary>Where the event running happened; the origin when there is none.</summary>
    private Vector3d EventPosition() => _world.CurrentEvent?.Position ?? default;

    /// <summary>
    /// Lists an action when the arguments are as many as asked (<c>HumanAI_AddActionToLocalList</c>): its state made from the
    /// arguments, its flags the last argument.
    /// </summary>
    private QvmValue Ask(QvmCallContext call, int count, Func<object>? context, Igi2AiActionCallback init)
    {
        if (call.ArgumentCount == count)
        {
            List(context?.Invoke(), call.GetInt32(count - 1), init);
        }

        return Done;
    }

    /// <summary>Lists an action for the mind to push once its script is done, at the script's rank; nothing with no state free.</summary>
    private void List(object? context, int flags, Igi2AiActionCallback init)
    {
        Igi2HumanAi ai = Ai;
        if (context is not null && !ai.Actions.TakeContext())
        {
            return;
        }

        Igi2AiActionItem item = ai.Actions.Take();
        item.Context = context;
        item.Priority = _world.HandlerPriority;
        item.Flags = flags;
        item.Init = init;
        _world.LocalActions.Add(item);
    }

    /// <summary>Hands the event to the squad after a pause, <c>HumanAI_DelayedEventPassOnCB</c>.</summary>
    private QvmValue DelayedEventPassOn(QvmCallContext call)
    {
        if (call.ArgumentCount == 1 && _world.CurrentEvent is { } record)
        {
            List(
                new Igi2AiContent.DelayedPassContext { Event = record.Event, Position = record.Position, Direction = record.Direction },
                call.GetInt32(0),
                Igi2AiContent.DelayedEventPassOnInit);
        }

        return Done;
    }

    private QvmValue Patrol(QvmCallContext call) => Ask(
        call,
        3,
        () => new Igi2AiContent.PatrolContext { PatrolPathId = call.GetInt32(0), StartIndex = call.GetInt32(1) },
        Igi2AiContent.PatrolInit);

    /// <summary>
    /// Lies flat for the seconds asked (<c>HumanAI_FallFlatCB</c>): not on a stair node, nor when whoever raised the event is within
    /// thirty metres.
    /// </summary>
    private QvmValue FallFlat(QvmCallContext call)
    {
        Igi2HumanAi ai = Ai;
        if (call.ArgumentCount != 2 || ai.Graph?.NodeAt(ai.CurrentNode) is not { } node
            || (node.Criteria & Igi2AiGraph.StairCriteria) != 0)
        {
            return Done;
        }

        if (_world.CurrentEvent?.Originator is Igi2Human raiser && Igi2AiMath.Distance(raiser.Position, ai.Soldier.Position) < 122880.0)
        {
            return Done;
        }

        List(new Igi2AiContent.TimedContext { Seconds = call.GetSingle(0) }, call.GetInt32(1), Igi2AiContent.FallFlatInit);
        return Done;
    }

    /// <summary>
    /// Goes to use a task (<c>HumanAI_ActivateCB</c>): false for no such placed task, a manned gun, or one another soldier is on his
    /// way to use.
    /// </summary>
    private QvmValue Activate(QvmCallContext call)
    {
        if (call.ArgumentCount != 3)
        {
            return Done;
        }

        int id = call.GetInt32(0);
        if ((uint)id >= 0x1000u || _world.TaskById(id) is not { } task || task is IIgi2AiMountedGun { IsManned: true }
            || _world.ActivatingAi(id) is not null)
        {
            return Bool(false);
        }

        List(
            new Igi2AiContent.ActivateContext { TaskId = id, IsWalkRun = (byte)call.GetInt32(1) != 0 },
            call.GetInt32(2),
            Igi2AiContent.ActivateInit);
        return Bool(true);
    }

    /// <summary>Fires at a node or a task, for seconds, pausing between bursts, <c>HumanAI_FireAtNodeCB</c> and <c>…FireAtTaskCB</c>.</summary>
    private QvmValue FireAt(QvmCallContext call, Igi2AiActionCallback init) => Ask(
        call,
        5,
        () => new Igi2AiContent.FireAtContext
        {
            Target = call.GetInt32(0),
            Ticks = Ticks(call.GetSingle(1)),
            BurstDelay = Ticks(call.GetSingle(2)),
            ShowReload = (byte)call.GetInt32(3) != 0,
        },
        init);

    /// <summary>Looks roughly the way the event came, for seconds, the circle cut in at least one sector (<c>HumanAI_LookInApproxEventDirectionCB</c>).</summary>
    private QvmValue LookInApproximateEventDirection(QvmCallContext call) => Ask(
        call,
        3,
        () =>
        {
            int ticks = Ticks(call.GetSingle(0));
            return new Igi2AiContent.ApproxContext
            {
                Position = EventPosition(),
                TimeLeft = ticks,
                StartTime = ticks,
                SectorResolution = Math.Max(call.GetInt32(1), 1),
            };
        },
        Igi2AiContent.LookInApproxDirectionInit);

    /// <summary>Goes for the grenade the event is about, when it is about one (<c>HumanAI_KickGrenadeCB</c>).</summary>
    private QvmValue KickGrenade(QvmCallContext call)
    {
        if (call.ArgumentCount == 1 && _world.CurrentEvent is { Reference: { } grenade } record)
        {
            List(
                new Igi2AiContent.EventMoveContext { Position = record.Position, Grenade = grenade },
                call.GetInt32(0),
                Igi2AiContent.KickGrenadeInit);
        }

        return Done;
    }

    /// <summary>
    /// Flees the event (<c>HumanAI_RunPanickingCB</c>). A grenade landing (event 16) is fled only when the soldier lies within sixty
    /// degrees of the way it flies; then the soldier shouts it to his side (event 37, flags 1314, ten metres). The first argument is
    /// stored where the flight's own state overwrites it.
    /// </summary>
    private QvmValue RunPanicking(QvmCallContext call)
    {
        if (call.ArgumentCount != 2)
        {
            return Done;
        }

        Igi2HumanAi ai = Ai;
        Igi2AiEventRecord? record = _world.CurrentEvent;
        Vector3d at = record?.Position ?? default;
        int flags = call.GetInt32(1);
        if (record is { Event: 16, Specific: Vector3f velocity })
        {
            Vector3d me = ai.Soldier.Position;
            float vx = velocity.X, vy = velocity.Y, vz = velocity.Z;
            float ox = (float)(me.X - at.X), oy = (float)(me.Y - at.Y), oz = (float)(me.Z - at.Z);
            Fpu speed = Fpu.Sqrt(((Fpu)vx * vx) + ((Fpu)vy * vy) + ((Fpu)vz * vz));
            if (speed.Value > 0.0)
            {
                Fpu inverse = (Fpu)1.0 / speed;
                float nx = ((Fpu)vx * inverse).F, ny = ((Fpu)vy * inverse).F;
                Fpu reach = Fpu.Sqrt(((Fpu)ox * ox) + ((Fpu)oy * oy) + ((Fpu)oz * oz));
                if (reach.Value > 0.0)
                {
                    Fpu inverseReach = (Fpu)1.0 / reach;
                    Fpu cos = (inverse * vz * (inverseReach * oz)) + ((Fpu)ny * (inverseReach * oy)) + ((Fpu)nx * ((Fpu)ox * inverseReach));
                    if (cos.Value < 0.5)
                    {
                        return Done;
                    }
                }
            }

            _world.SendEvent(new Igi2AiEvent(37, ai.Soldier.Team, 1314, 40960f, at, default, ai.Soldier));
        }

        List(new Igi2AiContent.EventMoveContext { Position = at }, flags, Igi2AiContent.RunPanickingInit);
        return Done;
    }

    /// <summary>
    /// Calls the side to the first enemy listed while it lives (<c>HumanAI_CallForBackupCB</c>): event 25, flags 38, out to the
    /// metres asked.
    /// </summary>
    private QvmValue CallForBackup(QvmCallContext call)
    {
        Igi2HumanAi ai = Ai;
        if (call.ArgumentCount != 1 || ai.Detection.Enemies.FirstOrDefault() is not { } first)
        {
            return Done;
        }

        IIgi2Detectable target = first.Target;
        float radius = ((Fpu)call.GetSingle(0) * 4096.0).F;
        _world.SendEvent(new Igi2AiEvent(25, ai.Soldier.Team, 38, radius, target.DetectablePosition, default, ai.Soldier, target));
        return Done;
    }

    /// <summary>
    /// Turns on the first enemy listed while it lives, after the detection-to-attack ticks (<c>HumanAI_EnemyDetectionCB</c>).
    /// </summary>
    private QvmValue EnemyDetection(QvmCallContext call)
    {
        Igi2HumanAi ai = Ai;
        if (call.ArgumentCount == 1 && ai.Detection.Enemies.FirstOrDefault() is { } first
            && first.Target.DetectionInfo().IsAlive)
        {
            List(
                new Igi2AiContent.WaitContext { Timer = Fpu.ToInt32(ai.Tweaks[Igi2AiTweakIndex.DetectionToAttackTime]) },
                call.GetInt32(0),
                Igi2AiContent.EnemyDetectionInit);
        }

        return Done;
    }

    /// <summary>Looks at a node of its graph, <c>HumanAI_LookAtNodeCB</c>.</summary>
    private QvmValue LookAtNode(QvmCallContext call)
    {
        Igi2HumanAi ai = Ai;
        if (call.ArgumentCount == 2 && ai.Graph?.NodeAt(call.GetInt32(0)) is { } node)
        {
            List(new Igi2AiContent.LookContext { Position = node.Position }, call.GetInt32(1), Igi2AiContent.LookAtPositionInit);
        }

        return Done;
    }
}
