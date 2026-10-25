using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>The combat action: the arm the AI type selects, the input frame it writes, the shared target record, and the guard arm's tick.</content>
public sealed partial class Igi1HumanAi
{
    /// <summary>Reports what the soldier's held weapon looks like this tick.</summary>
    /// <returns>The clip, the weapon's own burst length and its category.</returns>
    public delegate AiCombatWeaponState WeaponQuery();

    /// <summary>Gives the soldier the combat behaviour its <c>AIType</c> selects.</summary>
    /// <param name="type">The soldier's AI type, which picks the behaviour and the channel rows.</param>
    /// <param name="parameters">Its settings row.</param>
    /// <param name="weapon">Where the held weapon's state is read from each tick.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Without this a soldier that engages only turns to face its target, which is what the port did
    /// before combat was read.
    /// </para>
    /// <para>
    /// <c>AIAction_Combat</c> dispatches by type and this now follows it: a <c>GUARD_*</c> type,
    /// the sniper and Priboi stand and fight, and every <c>PATROL_*</c> type — <b>most soldiers in the
    /// game</b> — gets the move-and-fight arm instead.
    /// </para>
    /// </remarks>
    public void SetCombat(AiType type, AiCombatParameters parameters, WeaponQuery weapon)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(weapon);

        _type = type;
        _weapon = weapon;
        if (SoldierClass.SwapsClipsByAiType && Body is HumanSoldierBodyMachine soldierBody)
        {
            soldierBody.UseClips(SoldierClipsFor(type));
        }

        Arm = AiCombatArms.For(type);

        // The patrol arm's own fire and reload sub-actions ARE the guard arm's - the original installs
        // 0x48D8B0 and 0x48DB10 from both - so the shared machinery is kept and only the DECISION
        // differs, which is how the original splits them.
        _combat = Arm == AiCombatArm.None
            ? null
            : new AiGuardCombat(parameters, _random.NextDouble)
            {
                // Only GuardCombatStand (0x48D080) draws the dead-store shot delay; the patrol arm's Fire installs the
                // shared fire child with its burst and nothing else.
                DrawsStandDelay = Arm == AiCombatArm.Guard,
            };
        _patrol = Arm is AiCombatArm.Patrol or AiCombatArm.NamedCharacter
            ? new AiPatrolCombat(parameters, _random.NextDouble, closeQuarters: Arm == AiCombatArm.Patrol)
            : null;
        _gunnerCombat = Arm == AiCombatArm.Gunner ? new AiGunnerCombat(this) : null;

        CombatParameters = parameters;
    }

    // 0x45CF90's switch over the brain's type: which Human+824 a HumanSoldier plays.
    private static HumanBodyStateTable SoldierClipsFor(AiType type) => type switch
    {
        AiType.Priboi => HumanSoldierStates.PriboiTable,
        AiType.Civilian => HumanSoldierStates.CivilianTable,
        AiType.SpetnazPatrolUzi or AiType.SpetnazPatrolAk or AiType.SpetnazPatrolSpas
            or AiType.SpetnazGuardUzi or AiType.SpetnazGuardAk or AiType.SpetnazGuardSpas
            => HumanSoldierStates.SpetsnazTable,
        _ => HumanSoldierStates.Table,
    };

    /// <summary>Gets whether the carried inventory contains a grenade weapon.</summary>
    /// <remarks>
    /// This is <c>HumanAI+15869</c>. IGI 1's <c>0x44FBA0</c> scans every carried weapon and sets the
    /// byte when any resolved weapon type has category 4; it is not derived from the weapon in slot 0.
    /// </remarks>
    public bool HasGrenade { get; private set; }

    /// <summary>Publishes the result of the Human inventory scan to the AI.</summary>
    /// <param name="available">Whether any carried weapon is <c>WEAPONTYPE_GRENADE</c>.</param>
    public void SetGrenadeAvailable(bool available) => HasGrenade = available;

    /// <summary>Gets which combat arm this soldier's type selects.</summary>
    public AiCombatArm Arm { get; private set; } = AiCombatArm.Guard;

    /// <summary>Gets the patrol arm's state, when the soldier has one.</summary>
    public AiPatrolCombat? Patrol => _patrol;

    /// <summary>Gets whether the gunner currently owns its configured stationary gun.</summary>
    public bool IsMounted => _gunnerCombat?.IsMounted == true;

    /// <summary>Gets the stationary gun currently owned by this soldier.</summary>
    public AiStationaryGun? MountedGun => _gunnerCombat?.MountedGun;

    /// <summary>Gets what the patrol arm chose on the last tick it ran.</summary>
    public AiPatrolAction PatrolAction { get; private set; } = AiPatrolAction.LookForTarget;

    /// <summary>
    /// The close-combat blow at its clip's hit frame (event 12) — <c>Soldier_MeleeUpdate</c> (<c>0x45B530</c>).
    /// </summary>
    /// <param name="damage">Receives the row's damage factor when the blow lands.</param>
    /// <returns><see langword="true"/> when it lands on the soldier's target.</returns>
    /// <remarks>
    /// The level's stream is drawn first, every time: a draw over the row's chance (<c>+15768</c>) misses. A hit needs a
    /// target, and does the row's damage (<c>+15772</c>) through the target's punch handler. A class whose entry latches
    /// the blow done (the pistol soldier's <c>+3353</c>) never reaches the draw.
    /// </remarks>
    public bool StrikeCloseCombat(out float damage)
    {
        damage = 0f;
        if (!SoldierClass.LandsCloseCombatBlow || CombatParameters is not { } parameters)
        {
            return false;
        }

        if (_random.NextDouble() > parameters.CloseCombatHitProbability || Target is null)
        {
            return false;
        }

        damage = parameters.CloseCombatDamage;
        return true;
    }

    /// <summary>Gets where the soldier aims at its target now — <c>0x451380</c>: the target's origin, raised.</summary>
    public Vector3d TargetAimPoint => TrueAimPoint();

    private bool _movingInCombat;
    private AiPatrolAction? _patrolOneShotChild;
    private AiEvasiveAction? _patrolRollChild;
    private bool _patrolProne;


    /// <summary>Gets a value indicating whether the soldier is walking a combat route this tick.</summary>
    /// <remarks>
    /// True whenever the move-and-fight arm stepped the soldier — advancing on a target, repositioning, or
    /// hunting a lost one while it watches. The renderer plays the WALK clip for it, because the walk clip's
    /// own root motion is what the next tick's <see cref="AdvanceInCombat"/> moves by; playing the fire pose
    /// instead (as the raw action would, since a hunting soldier's action is <c>LookForTarget</c> most
    /// ticks) leaves the clip with no motion and the chase never travels.
    /// </remarks>
    public bool IsMovingInCombat => _movingInCombat;

    /// <summary>Gets a value indicating whether a combat action is installed — <c>HumanAI+15016</c> is set.</summary>
    /// <remarks>
    /// What <c>sub_4528F0</c> answers. <c>KnifeStab</c> (<c>0x46DFB0</c>) reads it to decide a stab is a silent
    /// kill: a soldier with no fight on is stabbed at a damage factor of 1000.
    /// </remarks>
    public bool HasCombatRecord => _combatInstalled;

    /// <summary>Gets the soldier's combat row, once one has been given.</summary>
    public AiCombatParameters? CombatParameters { get; private set; }

    /// <summary>Gets the soldier's AI type.</summary>
    public AiType Type => _type;

    /// <summary>Gets what the combat behaviour asked for on the last tick it ran.</summary>
    public AiCombatCommand Combat { get; private set; }

    /// <summary>Gets the class of the body this brain drives.</summary>
    public HumanSoldierClass SoldierClass { get; }

    /// <summary>Gets the body machine — <c>Human+748</c> of the soldier this brain drives.</summary>
    public HumanBodyMachine Body { get; }

    /// <summary>Gets the body's state — the real machine, not the old inference.</summary>
    public int CombatState => Body.State;

    /// <summary>Gets whether this tick entered a new body state rather than running its per-tick callback.</summary>
    public bool CombatStateChangedThisTick => Body.ChangedThisTick;

    /// <summary>Runs one tick of the body machine and returns the state to animate.</summary>
    /// <param name="animationDone">Whether the state's clip finished a cycle this tick — the machine's <c>0x1</c> bit.</param>
    /// <returns>The body state, whose clip the renderer plays.</returns>
    /// <remarks>
    /// The body reads the input frame this tick's writes built — each channel on the slot its AI type
    /// resolves it to, every raw write on its own slot, and the trigger on slot 6 — so each class's signal
    /// handler finds its bits where the original's does. See <see cref="HumanBodyInput"/>.
    /// </remarks>
    public int TickCombatState(bool animationDone)
    {
        // The gun was taken this tick: the dispatch seats the body in state 36 and zeroes slot 24 before the
        // machine reads the frame.
        bool seated = _seatedOnGunThisTick && Body is HumanSoldierBodyMachine;
        if (seated)
        {
            ((HumanSoldierBodyMachine)Body).EnterUseObject();
        }

        _seatedOnGunThisTick = false;
        int state = Body.Tick(new HumanBodyInput(
            WrittenFrame(dropUseObject: seated),
            animationDone,
            Flinch: _flinchZone >= 0,
            Dead: Activity == AiSoldierActivity.Dead,
            MovingForward: IsMovingInCombat,
            Running: _runByte,
            HitZone: _flinchZone,
            TenseRun: _tenseRunThisTick,
            Forward: WalksForward));

        // 0x45D080 clears the flinch bit straight after the machine's tick: it is a one-tick pulse, and a
        // state that does not test it on that tick loses it.
        _flinchZone = -1;

        // ...and flips the run byte on a slot-20 press, after the machine has read it (see WalkGait).
        WalkGait = _runByte;
        if (_gaitPress)
        {
            _runByte = !_runByte;
            _gaitPress = false;
        }

        return state;
    }

    /// <summary>Raises the flinch bit a hit that passed the soldier's recoil rule sets.</summary>
    /// <param name="zone">The zone the hit struck — <c>Human+744</c>, which names the flinch clip.</param>
    public void Flinch(int zone) => _flinchZone = zone;

    // The zone of this tick's flinching hit, or -1: machine+8's 0x10000 bit with Human+744 beside it.
    private int _flinchZone = -1;

    // Whether the gunner took its gun this tick, for the body's object dispatch.
    private bool _seatedOnGunThisTick;

    /// <summary>Keeps a mounted gunner on the gun as it turns: behind the barrel, facing along it, at his own height.</summary>
    /// <remarks>
    /// The entry attaches the body to the gun (<c>0x463310</c> keeps its place in the gun's frame, <c>0x4635F0</c> sets
    /// the attached flag) and the human's frame is then the gun's times that place (<c>0x4610C0</c>), so the gunner
    /// swings round the stand with the traverse. The seat is the same point in the gun's turned frame either way.
    /// </remarks>
    public void RideMountedGun()
    {
        if (MountedGun is { } gun)
        {
            SeatOn(gun);
        }
    }

    // State 36's entry (0x45C8B0) stands the body behind the gun: it takes the gun's position and frame (0x444B30), moves
    // 2867.2 units along the frame's axis in X and Y only, and copies Z from the human's own Human+48 -- the mount
    // never changes the body's height. The gun's position is its pivot, 1.3 m over the deck on mission 8's M2HBs;
    // taking its Z stood the gunner in mid-air.
    private void SeatOn(AiStationaryGun gun)
    {
        Vector3d mount = gun.MountPosition;
        Position = new Vector3d(mount.X, mount.Y, Position.Z);
        Facing = gun.MountFacing;
    }

    // The frame slot the use-object signal reads -- channel 4's.
    private const int UseObjectSlot = 24;

    /// <summary>The input frame this tick's writes put together — <c>Human+432</c>.</summary>
    /// <param name="dropUseObject">Leave out slot 24, which the object dispatch has just consumed.</param>
    private PlayerInputFrame WrittenFrame(bool dropUseObject = false)
    {
        PlayerInputFrame frame = default;
        foreach (AiChannelWrite write in _channelWrites)
        {
            if (write.Slot != AiCombatChannels.None && !(dropUseObject && write.Slot == UseObjectSlot))
            {
                frame[write.Slot] = write.Value;
            }
        }

        foreach (AiRawInputWrite write in _rawInputWrites)
        {
            frame[write.Slot] = write.Value;
        }

        if (Combat.PullTrigger)
        {
            frame[(int)PlayerInputSlot.Fire] = 1f;
        }

        return frame;
    }

    /// <summary>Gets which sub-state the combat behaviour is in.</summary>
    public AiCombatPhase CombatPhase => _combat?.Phase ?? AiCombatPhase.Ended;

    /// <summary>Gets how many rounds are left in the current burst — <c>HumanAI+15956</c>.</summary>
    public int RoundsLeftInBurst => _combat?.RoundsLeftInBurst ?? 0;

    /// <summary>Gets the input channels this tick asked the body for, resolved to their frame slots.</summary>
    /// <remarks>
    /// The original writes these through <c>sub_48EF70(ai, channel, value)</c>, which resolves the slot
    /// out of <c>off_54151C[aiType][channel]</c>. The port has no forty-slot input frame on a soldier
    /// yet, so it records the same writes instead — which is what lets a run of the port be compared
    /// line for line against the probe's <c>[combat] channel</c> lines from the retail game.
    /// </remarks>
    public IReadOnlyList<AiChannelWrite> ChannelWrites => _channelWrites;

    private readonly List<AiChannelWrite> _channelWrites = [];

    private void WriteChannel(AiCombatChannel channel, float value) =>
        _channelWrites.Add(new AiChannelWrite(channel, AiCombatChannels.SlotOf(_type, channel), value));

    private bool WroteChannel(AiCombatChannel channel) =>
        _channelWrites.Any(write => write.Channel == channel && write.Value != 0f);

    /// <summary>Gets direct physical input-frame writes made this tick.</summary>
    /// <remarks>Unlike <see cref="ChannelWrites"/>, these bypass the AI-type channel map.</remarks>
    public IReadOnlyList<AiRawInputWrite> RawInputWrites => _rawInputWrites;

    private readonly List<AiRawInputWrite> _rawInputWrites = [];

    private void WriteRawInputSlot(int slot, float value) =>
        _rawInputWrites.Add(new AiRawInputWrite(slot, value));

    private bool WroteRawInputSlot(int slot) =>
        _rawInputWrites.Any(write => write.Slot == slot && write.Value != 0f);

    /// <summary>Gets the hit probability the last burst set — <c>Human+1236</c>.</summary>
    /// <remarks>The weapon reads it with every round; the AI never decides a hit itself.</remarks>
    public float HitProbability { get; private set; } = 1f;

    /// <summary>Gets the shared record of everybody fighting this soldier's target.</summary>
    public AiCombatTarget? Target { get; private set; }

    /// <summary>Puts the soldier on a shared target record — <c>sub_451080</c>.</summary>
    /// <param name="target">The record, or <see langword="null"/> to leave the one it is on.</param>
    public void SetTarget(AiCombatTarget? target)
    {
        if (ReferenceEquals(Target, target))
        {
            return;
        }

        Target?.Remove(this);
        Target = target;
        target?.Add(this);
    }

    /// <summary>Gives this AI the shared record for the living player it can classify as an enemy.</summary>
    /// <param name="target">The mission-wide player target record.</param>
    public void SetDefaultTarget(AiCombatTarget? target) => _defaultTarget = target;

    /// <summary>Stores one per-HumanAI integer script scratch slot.</summary>
    public void SetScriptInteger(int index, int value) => _scriptIntegers[index] = value;

    /// <summary>Reads one per-HumanAI integer script scratch slot.</summary>
    public int GetScriptInteger(int index) => _scriptIntegers.GetValueOrDefault(index);

    /// <summary>Stores one per-HumanAI real script scratch slot.</summary>
    public void SetScriptReal(int index, float value) => _scriptReals[index] = value;

    /// <summary>Reads one per-HumanAI real script scratch slot.</summary>
    public float GetScriptReal(int index) => _scriptReals.GetValueOrDefault(index);

    /// <summary>Returns the referenced AlarmControl's live on-state.</summary>
    public bool GetAlarmControlStatus() =>
        AlarmControlId != -1 && _alarmStatus?.Invoke(AlarmControlId) == true;

    /// <summary>Returns the distance to the referenced alarm trigger, or retail's -1 failure value.</summary>
    public double GetAlarmTriggerDistance()
    {
        if (AlarmTriggerId == -1 || _locateTask is null || !_locateTask(AlarmTriggerId, out Vector3d position))
        {
            return -1.0;
        }

        return Vector3d.Distance(Position, position);
    }

    private Func<int, bool>? _alarmStatus;
    private Func<int, bool?>? _gunnerStatus;
    private Func<int, AiStationaryGun?>? _stationaryGun;

    /// <summary>Connects the AI natives to the level's live AlarmControl registry.</summary>
    public void SetAlarmStatusResolver(Func<int, bool>? resolver) => _alarmStatus = resolver;

    /// <summary>Connects the gunner native to StationaryGun+540's occupied flag.</summary>
    public void SetGunnerStatusResolver(Func<int, bool?>? resolver) => _gunnerStatus = resolver;

    /// <summary>Connects the gunner combat arm to the level's live stationary-gun tasks.</summary>
    public void SetStationaryGunResolver(Func<int, AiStationaryGun?>? resolver) => _stationaryGun = resolver;

    /// <summary>Returns the referenced stationary gun's occupied status, or false on lookup failure.</summary>
    public bool GetGunnerStatus() => GunnerId != -1
        && (_stationaryGun?.Invoke(GunnerId)?.IsOccupied == true || _gunnerStatus?.Invoke(GunnerId) == true);

    /// <summary>
    /// The combat action's end function, <c>0x48D7D0</c>: release the combat record and the aim slot in
    /// it, and leave a mounted gun.
    /// </summary>
    /// <remarks>
    /// It runs however the fight ends — the arm giving up, the target dying, the action being ended to
    /// make way for another — and nothing else: the body is left as it is, for whatever runs next.
    /// </remarks>
    private void EndCombat()
    {
        _combatInstalled = false;
        _combat?.Cancel();
        _gunnerCombat?.End();
        _movingInCombat = false;
        _patrolOneShotChild = null;
        _patrolRollChild = null;
        _patrolProne = false;
        _patrolKneeling = false;
        _repositionLatched = false;
        _lookForTargetLatched = false;

        // The node claim outlives the fight: nothing in IGI.exe releases HumanAI+16032 when combat ends --
        // only death (0x44F410), a claiming walk (0x4518E0) or a search that claims elsewhere replaces it.
        SetTarget(null);
        if (Activity == AiSoldierActivity.Engaging)
        {
            Activity = AiSoldierActivity.Idle;
        }
    }

    /// <summary>Takes the combat action, re-arming the behaviour every time.</summary>
    /// <remarks>
    /// <b>Every</b> path into combat comes through here, and that matters: once the machine ends — a
    /// targeting timeout, or a target with no detection slot — its phase is
    /// <see cref="AiCombatPhase.Ended"/> and every later tick returns "ended" at once. Re-arming only on
    /// the scripted path left a soldier that engaged a second way permanently unable to fight, which
    /// reads exactly as a guard that sees you and does nothing. <c>0x48D000</c> arms it when the action
    /// is taken, which is what this is.
    /// </remarks>
    private void EnterCombat()
    {
        // 0x44D160's civilian case installs no combat callback. It must not inherit the guard arm.
        if (Arm == AiCombatArm.None)
        {
            Activity = AiSoldierActivity.Idle;
            SetTarget(null);
            return;
        }

        _patrolOneShotChild = null;

        // The record's human is the subject of the event being handled -- whoever SetCombat's triggering
        // event named. An event that names nobody the scan knows keeps the soldier's attention.
        if (_tracked.ContainsKey(_currentEventSubject))
        {
            _targetId = _currentEventSubject;
        }

        SetTarget(TargetRecord);

        if (!_combatInstalled || CombatPhase == AiCombatPhase.Ended)
        {
            // The installers leave the body as they find it and pick the sub-arm from it: crouched -> the
            // kneel arm (0x48ABA0 -> 0x48C250, the RPG's 0x48CB40 -> its kneel tick), prone -> the prone
            // arm (0x48C510). A patrol crouching on its route when combat installs fights kneeling.
            bool crouched = IsCrouching || AiCombatChannels.IsInStance(_type, AiStanceChannel.Kneeling, CombatState);
            bool prone = AiCombatChannels.IsInStance(_type, AiStanceChannel.Prone, CombatState);
            _combat?.Begin(crouched);
            _patrol?.Begin();
            _gunnerCombat?.Begin();
            _detectionShoutDone = false;
            _alertPingCounter = 0;
            _patrolProne = prone && Arm == AiCombatArm.Patrol;
            _repositionLatched = false;
            _lookForTargetLatched = false;

            // The run function is the stance's entry, so no movement behaviour of an earlier fight is installed.
            PatrolAction = default;
            _patrolKneeling = crouched && !_patrolProne && Arm == AiCombatArm.Patrol;

            // SetCombat's start (0x44D160) is 0x48ABA0 for the patrol types, which installs a stance ENTRY as the
            // run function -- 0x48BBD0, 0x48C250 or 0x48C510 -- and that entry's whole tick is to install the stance's
            // per-tick (CombatStand 0x48BC00, Kneel 0x48C280, LieDown 0x48C540) for the next. The guards' start
            // (0x48D000) installs GuardCombatStand itself, which runs at once. Probed on mission 4: patrol 411 took
            // combat on tick 4384 and drew its first evasion roll (0x48BE30, from CombatStand) on 4385.
            _combatEntryTick = (int)_type is >= 7 and <= 10 or >= 15 and <= 19 or >= 23 and <= 25;
        }

        _combatInstalled = true;
        Activity = AiSoldierActivity.Engaging;
        RequestedAnimation = -1;

        // The movement queue and its destination (+15920) outlive the action that planned them: SetCombat
        // (0x44D160) touches neither, so a patrol caught mid-leg that repositions to the node it claimed for the
        // leg (0x4518E0 sees the same destination) carries on along the queue it already had.
    }

    /// <summary>Runs one tick of the combat behaviour and applies what it asks for.</summary>
    /// <remarks>
    /// <para>
    /// The soldier turns with the same clamped steering everything else uses and reports back whether
    /// the facing reached the two-degree tolerance, which is what the firing tick holds fire on.
    /// </para>
    /// <para>
    /// Nothing here fires a round. The command carries the trigger and the host turns it into a shot
    /// through the body's own weapon path, which is what the original does.
    /// </para>
    /// </remarks>
    private void Fight(int tick, Vector3d lastSeenFeet)
    {
        Vector3d enemy = TrackEngagement(lastSeenFeet);

        if (_combat is null || _weapon is null)
        {
            // No combat row: the old behaviour, which is to stand and look at the target.
            TurnTowards(enemy, AiPatrolRoute.LookAtToleranceRadians);
            return;
        }

        bool mounted = false;
        if (_gunnerCombat is not null && RunGunnerMount(_gunnerCombat, enemy, out mounted))
        {
            return;
        }

        if (_patrol is not null)
        {
            FightAsPatrol(tick, enemy);
            return;
        }

        FightAsGuard(enemy, mounted);
    }

    /// <summary>
    /// The head of every combat tick, <c>sub_48A920</c>: the one detection bark, the squad ping and the shared
    /// record's refresh.
    /// </summary>
    /// <param name="lastSeenFeet">Where the target was last seen.</param>
    /// <returns>Where combat believes the target is: the shared record's position once anyone has seen it.</returns>
    private Vector3d TrackEngagement(Vector3d lastSeenFeet)
    {
        // Recomputed every combat tick: AdvanceInCombat sets it when it actually steps.
        _movingInCombat = false;
        _rawInputWrites.Clear();

        // sub_48A920: the bark needs a visible target whose class is HumanPlayer (word_5385B0), tested before
        // the per-action latch -- so a soldier fighting an NPC neither shouts nor spends its one bark, and
        // still challenges the player when it turns on him. The latch is set even when another AI holds
        // the shared 90-tick sound gate, so this engagement never retries.
        if (TargetSlot.IsVisible && !TargetIsSoldier && !_detectionShoutDone)
        {
            DetectionShoutRequestedThisTick = true;
            _detectionShoutDone = true;
        }

        // sub_48A920 pings the squad on the engagement's first tick with a valid target and every 20th
        // after, counting in the combat action's own block -- not on a mission-wide tick -- and only
        // while the detection scan still registers the target.
        if (TargetSlot.IsRegistered)
        {
            AlertPingDueThisTick = _alertPingCounter == 0;
            _alertPingCounter = (_alertPingCounter + 1) % AlertPingPeriodTicks;
        }

        // The shared record's position is refreshed only WHILE THE TARGET IS SEEN (sub_48A920 reads it
        // off the detection slot). Hunting and the accuracy distance read the record (+272), so a guard
        // searches where the squad last SAW the player; one that never saw it borrows the squad's record.
        // Turning to an unseen target and aiming a burst do not: see TrueAimPoint.
        if (TargetSlot.IsVisible)
        {
            Target?.Refresh(lastSeenFeet);
        }

        return Target is { HasBeenSeen: true } shared ? shared.Position : lastSeenFeet;
    }

    /// <summary>The gunner arm's mount and dismount, which own the whole tick while they run.</summary>
    /// <param name="gunnerCombat">The gunner arm.</param>
    /// <param name="enemy">Where combat believes the target is.</param>
    /// <param name="mounted">Receives whether the gunner fires the mounted weapon this tick.</param>
    /// <returns><see langword="true"/> when the gunner ended the fight or is mounting, and the tick is done.</returns>
    private bool RunGunnerMount(AiGunnerCombat gunnerCombat, Vector3d enemy, out bool mounted)
    {
        mounted = false;
        // 0x48D340 tests the gun's cone against 0x451380's point -- the target's origin raised, read live -- not the
        // shared record.
        AiStationaryGun? gun = GunnerId == -1 ? null : _stationaryGun?.Invoke(GunnerId);
        AiGunnerCommand gunner = gunnerCombat.Update(
            gun,
            TrueAimPoint(),
            TargetSlot.IsRegistered);

        if (gunner.Ended)
        {
            Combat = new AiCombatCommand { Ended = true };
            _channelWrites.Clear();
            if (gunner.MountChannelValue is { } cleanupValue)
            {
                WriteChannel(AiCombatChannel.PlayAnimationAlternate, cleanupValue);
            }

            EndCombat();
            return true;
        }

        if (gunner.MountChannelValue is { } mountValue)
        {
            Combat = new AiCombatCommand();
            _channelWrites.Clear();
            WriteChannel(AiCombatChannel.PlayAnimationAlternate, mountValue);
            if (gunner.MountedThisTick && gun is not null)
            {
                _seatedOnGunThisTick = true;
                SeatOn(gun);
            }
            return true;
        }

        mounted = gunner.RunMountedWeapon;
        return false;
    }

    /// <summary>The stand-and-fight arm's tick — <c>0x48D080</c>, the RPG's <c>0x48CBD0</c>, or the mounted gun's.</summary>
    /// <param name="enemy">Where combat believes the target is.</param>
    /// <param name="mounted">Whether the gunner fires the mounted weapon this tick.</param>
    private void FightAsGuard(Vector3d enemy, bool mounted)
    {
        AiGuardCombat combat = _combat!;

        // A knife gets no branch of its own. 0x48D080's close-combat test reads HumanAI+15836 == 5, a word
        // only the constructor ever writes, so it is dead; a guard holding a knife reaches the fire child,
        // sets up no burst (sub_48B9C0 has none for it) and stands.
        AiCombatWeaponState weapon = _weapon!();
        Vector3d aim = TrueAimPoint();

        AiCombatSense sense = new(
            HasDetectionSlot: TargetSlot.IsRegistered,
            InCone: TargetSlot.IsInCone,
            Visible: TargetSlot.IsVisible,
            TargetPosition: aim,
            DistanceToTargetInUnits: Vector3d.Distance(Position, aim),
            LocomotionState: AiCombatChannels.StateOf(_type, AiStanceChannel.Standing),
            AmmoInClip: weapon.AmmoInClip,
            Category: weapon.Category,
            ClipSize: weapon.ClipSize,
            BurstSize: weapon.BurstSize,
            AimersOnTarget: Target?.Count(AiAimMode.Aiming) ?? 0);

        // 0x48D080 decides nothing unless the body is in its type's stand state (sub_48EFE0(C, 0)): after a
        // burst the body racks down through ready and recover to standing before the next one.
        //
        // The accuracy lerp scales over the PLAIN primary view length: 0x48B9C0 reads [HumanAI+176],
        // the third double of the primary block {alpha, gamma, length, alarmLength} -- not the
        // alarm-extended range. Passing the alerted range stretched the ramp and made every alerted
        // guard more accurate at range than retail's.
        AiCombatCommand command;
        if (mounted)
        {
            command = combat.UpdateMounted(sense, enemy, Vision.Primary.Length);
        }
        else if (Arm == AiCombatArm.Rpg)
        {
            // 0x48CBD0 gates on the stand state, 0x48CDF0 (and the reload it installs) on the kneel one.
            AiStanceChannel stance = combat.RpgKneeling ? AiStanceChannel.Kneeling : AiStanceChannel.Standing;
            command = combat.UpdateRpg(
                sense, AiCombatChannels.IsInStance(_type, stance, CombatState), Vision.Primary.Length);
        }
        else
        {
            bool standing = AiCombatChannels.IsInStance(_type, AiStanceChannel.Standing, CombatState);
            command = combat.Update(sense, standing, Vision.Primary.Length);
        }

        Combat = command;

        // The same writes 0x48EF70 makes, recorded rather than applied -- see ChannelWrites. The RPG's
        // kneel tick and every child it installs hold channel 1 on every tick.
        _channelWrites.Clear();
        if (Arm == AiCombatArm.Rpg && combat.RpgKneeling)
        {
            WriteChannel(AiCombatChannel.Kneel, 1f);
        }
        if (command.FirePose)
        {
            WriteChannel(AiCombatChannel.Fire, 1f);
        }

        if (command.PressReload)
        {
            WriteRawInputSlot(8, 1f);
        }

        if (command.StartedBurst is { } burst)
        {
            HitProbability = burst.HitProbability;
        }

        if (command.Aim is { } mode && Target is not null)
        {
            Target.Aim(this, mode);
        }

        if (command.TurnTo is { } point)
        {
            if (MountedGun is { } emplacement)
            {
                // State 36 stays attached to the gun. The StationaryGun task, not the Human root, consumes the turn
                // inputs and clamps the barrel to its authored cone; the body rides its frame.
                SeatOn(emplacement);
                combat.ReportAimed(emplacement.Contains(point));
            }
            else
            {
                combat.ReportAimed(TurnTowards(point, (float)AiGuardCombat.AimToleranceRadians));
            }
        }

        if (command.Ended)
        {
            EndCombat();
        }
    }
}
