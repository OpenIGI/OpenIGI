namespace OpenIGI.Game.Player;

/// <summary>
/// What clip each of a soldier's thirty-eight locomotion states plays — the table at <c>0x53ED10</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>HumanSoldier</c>'s constructor (<c>0x45CC50</c>) builds a state machine at <c>Human+748</c> over
/// the pointer table <c>off_53EC10</c> and installs <b>this</b> table at <c>Human+824</c> — the same
/// pair of fields the player has, with the player's own tables at <c>0x538870</c>. Every state entry
/// function then calls <c>0x489E70(human, stateId, 0.15f)</c>, which indexes it.
/// </para>
/// <para>
/// <b>Animation id 0 means "this state names its own clip"</b> (<see cref="HumanBodyStateTable.SelfDriven"/>),
/// and it is what the three death states carry (<see cref="SoldierDeath"/>).
/// </para>
/// <para>
/// <b>These ids are a soldier's, not the player's.</b> The same number in the player's animation set is
/// a different clip: playing the weapon table's ids on a soldier's rig made guards fall flat on the
/// floor, get up and do it again. A body clip for a soldier comes from here.
/// </para>
/// </remarks>
public static class HumanSoldierStates
{
    /// <summary>Gets the soldier's <c>{animation, mode}</c> table, transcribed from <c>0x53ED10</c>.</summary>
    public static HumanBodyStateTable Table { get; } = new(
        (2, 1), (34, 1), (3, 1), (57, 1), (10, 0), (11, 0), (56, 1), (14, 0), (58, 0), (70, 0),
        (54, 0), (53, 0), (55, 0), (7, 0), (19, 0), (12, 0), (13, 0), (48, 1), (52, 1), (18, 0),
        (33, 0), (32, 0), (9, 1), (15, 0), (8, 0), (20, 0), (16, 0), (17, 0), (21, 0), (2, 1),
        (0, 0), (26, 0), (0, 0), (0, 0), (0, 0), (0, 0), (31, 1), (2, 0));

    /// <summary>Gets the Spetsnaz soldiers' table, <c>0x53EE40</c> — their own stand, gaits and combat cycle.</summary>
    public static HumanBodyStateTable SpetsnazTable { get; } = Table.WithAnimations(
        (StandState, 62), (WalkState, 60), (ForwardState, 60), (FastForwardState, 81), (FiringState, 63),
        (ReadyState, 80), (RackState, 78), (ReloadState, 79));

    /// <summary>Gets Priboi's table, <c>0x53EF70</c>.</summary>
    public static HumanBodyStateTable PriboiTable { get; } = Table.WithAnimations(
        (StandState, 64), (WalkState, 65), (ForwardState, 65), (FastForwardState, 66), (FiringState, 69),
        (ReadyState, 5), (RackState, 67), (ReloadState, 68));

    /// <summary>Gets the civilians' table, <c>0x53F0A0</c> — Priboi's combat clips, and their own walk and crouch.</summary>
    public static HumanBodyStateTable CivilianTable { get; } = Table.WithAnimations(
        (StandState, 77), (WalkState, 76), (ForwardState, 76), (FastForwardState, 75), (FiringState, 69),
        (ReadyState, 5), (RackState, 67), (ReloadState, 68), (CrouchDownState, 93), (StandUpState, 95),
        (RunState, 76), (KneelingState, 94));

    /// <summary>
    /// Gets the pistol soldier's table, <c>0x53E9B8</c> — the same 38 states over the pistol set's clips.
    /// </summary>
    public static HumanBodyStateTable PistolTable { get; } = new(
        (2, 1), (33, 1), (3, 1), (4, 1), (18, 0), (17, 0), (6, 1), (21, 0), (34, 0), (49, 0),
        (5, 0), (35, 0), (36, 0), (7, 0), (8, 0), (12, 0), (16, 0), (32, 1), (37, 1), (9, 0),
        (20, 0), (19, 0), (11, 1), (22, 0), (10, 0), (15, 0), (13, 0), (14, 0), (23, 0), (2, 1),
        (0, 0), (31, 0), (0, 0), (0, 0), (0, 0), (0, 0), (39, 1), (2, 0));

    /// <summary>The blend time a soldier's state entry passes, in seconds.</summary>
    /// <remarks>
    /// <b>Zero, measured.</b> Forty-four state lookups against this table in a live retail mission all
    /// passed <c>0.000</c>; the player's table <c>0x538870</c> is looked up with <c>0.200</c> and
    /// sometimes <c>0.000</c>. <c>player.md</c> §16 records <c>0.15f</c> for the player, read statically
    /// — whatever that literal is, it is not what reaches this argument, and a soldier blends nothing.
    /// </remarks>
    public const float BlendSeconds = 0f;

    /// <summary>The state a soldier fires from — <b>and the only state it can fire from</b>.</summary>
    /// <remarks>
    /// <para>
    /// Measured first, then read. A live capture showed a fighting guard sitting here on every tick its
    /// burst counter was draining; <c>0x45B060</c>, the state's per-tick, then said why. It is the only
    /// one of the four combat states that touches the weapon, and what it does is
    /// <c>if (*(float *)(human + 456) != 0) human[+1233] = 1</c> — <c>Human+456</c> is <b>input-frame
    /// slot 6</b> (the newest frame lives at <c>+432</c>) and <c>Human+1233</c> is what the weapon tick
    /// hands the gun. So the chain that starts with the AI writing <c>HumanAI+15500</c> ends here, and
    /// a soldier holding the Fire slot up in any other state fires nothing at all.
    /// </para>
    /// <para>
    /// <b>A firing soldier is crouched.</b> State 6 is the only one of the four that sets
    /// <c>Human+829</c>. It is not the AI's kneel — that is state 19.
    /// </para>
    /// </remarks>
    public const int FiringState = 6;

    /// <summary>The standing grenade-throw state, animation 58.</summary>
    /// <remarks>
    /// Measured in <c>mission1-parity.log</c>: <c>0x48DBF0</c> wrote logical channel 8 to input slot 29
    /// while the body was in state 0; the signal builder immediately selected state 8 and animation 58.
    /// The state held after the child callback completed and returned to standing only when the clip ended.
    /// </remarks>
    public const int GrenadeThrowState = 8;

    /// <summary>The standing close-combat state, animation 70.</summary>
    /// <remarks>
    /// Measured in the same retail capture: <c>0x48DBB0</c> wrote logical channel 9 to input slot 30,
    /// selecting state 9 and animation 70. Like the grenade state, it owns the body until its clip ends.
    /// </remarks>
    public const int CloseCombatState = 9;

    /// <summary>The state a soldier's body is in once its burst is spent.</summary>
    /// <remarks>
    /// <para>
    /// The combat cycle is <c>0 → 12 → 10 → 6</c>, and <b>12 is the only way back to 0</b> — its
    /// per-tick <c>0x45B820</c> leaves for state 0 and nothing else. So a guard between bursts is here,
    /// not standing. The live capture agreed: state 12 on the tick the burst counter reached zero, and
    /// state 0 only once the target was out of sight.
    /// </para>
    /// <para>
    /// Reading a spent burst as standing put a tick of the idle clip between every two bursts, and
    /// because bursts are back to back that was a visible flicker.
    /// </para>
    /// </remarks>
    public const int ReloadState = 12;

    /// <summary>The state a soldier stands in.</summary>
    public const int StandState = 0;

    /// <summary>Walking forward, animation 34 — slot 25 with forward (<c>0x457D30</c>).</summary>
    public const int WalkState = 1;

    /// <summary>The forward gait with the run byte clear, animation 3 (<c>0x45AD10</c>).</summary>
    public const int ForwardState = 2;

    /// <summary>The forward gait with the run byte set, animation 57 (<c>0x4582C0</c>).</summary>
    public const int FastForwardState = 3;

    /// <summary>The standing roll-left state, animation 10.</summary>
    /// <remarks>
    /// <c>0x48D830</c> writes raw input slot 2, whose <c>0x20</c> signal selects this state in
    /// <c>0x457A00</c>. Its per-tick, <c>0x45B900</c>, holds until the clip ends and then enters
    /// <see cref="KneelingState"/>.
    /// </remarks>
    public const int RollLeftState = 4;

    /// <summary>The standing roll-right state, animation 11.</summary>
    /// <remarks>The raw-slot-3/<c>0x40</c> twin of <see cref="RollLeftState"/>.</remarks>
    public const int RollRightState = 5;

    /// <summary>The displayed standing reload state, animation 14.</summary>
    /// <remarks>Raw input slot 8/signal <c>0x800</c> selects it; direct weapon reloads do not.</remarks>
    public const int DisplayReloadState = 7;

    /// <summary>The ready state between bursts, animation 54 — <c>0x45B6E0</c>.</summary>
    public const int ReadyState = 10;

    /// <summary>The weapon-raise state, animation 53 — <c>0x458810</c>.</summary>
    public const int RackState = 11;

    /// <summary>The standing-to-kneeling transition, animation 7.</summary>
    public const int CrouchDownState = 13;

    /// <summary>Kneeling back up to standing, animation 19 (<c>0x45B9C0</c>).</summary>
    /// <remarks>No per-tick enters it: no write of 14 to <c>Human+764</c> exists in the executable.</remarks>
    public const int StandUpState = 14;

    /// <summary>The transition from standing to prone, animation 12.</summary>
    /// <remarks>
    /// Logical combat channel 2 resolves to input slot 22 and signal <c>0x100000</c>. State 15 holds
    /// that transition until its clip completes, then enters <see cref="ProneState"/>.
    /// </remarks>
    public const int LieDownState = 15;

    /// <summary>The transition from prone back to standing, animation 13.</summary>
    /// <remarks><c>0x45BF90</c> selects it as soon as the lie-down signal is released.</remarks>
    public const int GetUpState = 16;

    /// <summary>The stable kneeling state, animation 18.</summary>
    /// <remarks>Both standing rolls finish here before returning to stand when no kneel input is held.</remarks>
    public const int KneelingState = 19;

    /// <summary>The kneeling roll-left state, animation 33.</summary>
    public const int KneelRollLeftState = 20;

    /// <summary>The kneeling roll-right state, animation 32.</summary>
    public const int KneelRollRightState = 21;

    /// <summary>The kneeling firing state, animation 9.</summary>
    public const int KneelFiringState = 22;

    /// <summary>The kneeling displayed-reload state, animation 15.</summary>
    public const int KneelReloadState = 23;

    /// <summary>The kneeling weapon-ready state, animation 8.</summary>
    public const int KneelReadyState = 24;

    /// <summary>The kneeling-to-prone transition, animation 20.</summary>
    public const int KneelToProneState = 25;

    /// <summary>The stable prone state, animation 16.</summary>
    /// <remarks><c>0x45BF90</c> holds it while logical channel 2 remains asserted.</remarks>
    public const int ProneState = 26;

    /// <summary>The prone firing state, animation 17.</summary>
    /// <remarks>
    /// State 26 selects it when raw fire slot 6 contributes signal <c>0x200</c>. Its per-tick
    /// <c>0x45C0C0</c> is the prone state that actually forwards the trigger to the weapon.
    /// </remarks>
    public const int ProneFiringState = 27;

    /// <summary>The prone displayed-reload state, animation 21.</summary>
    public const int ProneReloadState = 28;

    /// <summary>The state a soldier <b>runs</b> in — what the move-and-fight arm's Follow puts it in.</summary>
    /// <remarks>
    /// <para>
    /// Measured by hooking <c>"Combat - Follow"</c> (<c>0x48C860</c>) in the running game: a chasing guard
    /// sits in locomotion state 17 (its diagonal twin is 18), at <b>4.0–5.0 m/s</b> — about three and a half
    /// times the 1.28 m/s patrol walk. Through the table (<c>0x53ED10</c>) that reads as <b>animation 48</b>
    /// (state 18 is 52), whose own root motion carries the run; the walk it was drawn with instead is why a
    /// chasing guard looked like it was strolling.
    /// </para>
    /// <para>
    /// <b>Which of 17/18 is straight-ahead versus diagonal is not yet pinned</b> — both were seen at the same
    /// speed — so 17 is taken as the forward run and the distinction is recorded as open.
    /// </para>
    /// </remarks>
    public const int RunState = 17;

    /// <summary>The tense forward-run state which forwards a held trigger to the weapon.</summary>
    /// <remarks>
    /// IGI 1 state 17's per-tick (<c>0x457FC0</c>) enters this state on signal <c>0x200</c>. Its own
    /// per-tick (<c>0x45AB00</c>) calls the weapon-forwarding helper while slot 6 is non-zero and returns
    /// to state 17 when the trigger is released. Animation 52 is therefore run-and-fire, not a diagonal
    /// locomotion variant.
    /// </remarks>
    public const int RunFiringState = 18;

    /// <summary>The state a soldier falls into when it is not on the ground.</summary>
    /// <remarks>Every combat state's per-tick leaves for this the moment flag <c>0x4</c> clears.</remarks>
    public const int AirborneState = 29;

    /// <summary>The state a soldier flinches into.</summary>
    /// <remarks>
    /// Entered on flag <c>0x10000</c> — which is exactly the bit <c>0x45D3C0</c> raises when the running
    /// damage accumulator passes the settings row's <c>Recoil threshold</c>. The damage handler and the
    /// state machine meet on that bit.
    /// </remarks>
    public const int FlinchState = 30;

    /// <summary>The prone flinch, animation 26 (<c>0x45C370</c>).</summary>
    /// <remarks>After its first twenty ticks a fresh flinch restarts it.</remarks>
    public const int ProneFlinchState = 31;

    /// <summary>The state a soldier dies into.</summary>
    /// <remarks>
    /// Entered on flag <c>0x20000</c>, the other bit <c>0x45D3C0</c> raises, and its entry is
    /// <c>0x45C440</c> — the general death chooser <see cref="SoldierDeath"/> already read from the
    /// other direction.
    /// </remarks>
    public const int DeathState = 32;

    /// <summary>The kneeling death, animation 24 — rolls, kneels and kneel changes die here (<c>0x45C5A0</c>).</summary>
    public const int KneelDeathState = 33;

    /// <summary>The prone death, animation 25 — prone states and their changes die here (<c>0x45C650</c>).</summary>
    public const int ProneDeathState = 34;

    /// <summary>Playing the clip the AI named in slot 23 (<c>0x45C750</c>).</summary>
    public const int ScriptedAnimationState = 35;

    /// <summary>Using an object of type 6 — the mounted pose, animation 31 (<c>0x45C8B0</c>).</summary>
    public const int UseObjectState = 36;

    /// <summary>Using an object of type 3, animation 2 (<c>0x45CA50</c>).</summary>
    public const int UseObjectOnceState = 37;
}
