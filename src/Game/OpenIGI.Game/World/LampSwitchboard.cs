using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>What one of a level's lamps has been told to do.</summary>
public enum LampMode
{
    /// <summary>Lit, as the level ships it.</summary>
    On,

    /// <summary>Switched off at the wall.</summary>
    Off,

    /// <summary>Failing: the tube strikes, runs, stutters and drops out on its own.</summary>
    Broken,
}

/// <summary>
/// The switch on the wall for every lamp a level has, and the filament behind it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LightFixture"/> says where a level's lamps are and how bright they would be; this says
/// which of them are <em>currently</em> giving that out. Everything downstream — the punctual lights in
/// the deferred pass, the emission on the fixture geometry, the emitters the surface cache bounces
/// off — multiplies by <see cref="LevelOf"/>, so the three can never disagree about whether a lamp is
/// on. A lamp that lights a floor without glowing reads as a room lit by nothing; a lamp that glows
/// without lighting reads as a decal. The same holds while it is switching.
/// </para>
/// <para>
/// <b>Nothing here happens instantly.</b> A light switch is a step function and a light is not: a real
/// fitting takes a couple of hundred milliseconds to die and rather longer to come back, and it is the
/// SHAPE of those two curves that makes the difference read as a lamp rather than as a texture being
/// swapped. The curves are in <see cref="Cooling"/> and <see cref="Warming"/> and each is documented
/// with what it is modelling.
/// </para>
/// <para>
/// Everything is driven from a seconds delta and a per-lamp deterministic generator — never from the
/// frame counter — so a lamp behaves the same at 30 fps and at 300, and so a row of ceiling tubes does
/// not strike, stutter and fail in unison. That last part matters more than it sounds: identical
/// flicker across six fixtures reads as one shader effect, not as six broken tubes.
/// </para>
/// </remarks>
public sealed class LampSwitchboard
{
    /// <summary>How far off the crosshair's axis a lamp may sit and still be the one aimed at.</summary>
    /// <remarks>
    /// A tangent rather than a distance, because the crosshair is an angle. A fixed radius would make a
    /// tube across the room almost impossible to hit while making the one overhead impossible to miss;
    /// about a degree and a half is roughly the size of the reticle and behaves the same at every range.
    /// </remarks>
    private const double AimTangent = 0.026;

    /// <summary>The smallest radius any fixture is picked with, in metres.</summary>
    /// <remarks>
    /// The extractor measures some fixtures as a few centimetres across. Aiming at one of those with an
    /// angular tolerance alone means standing on top of it, so every lamp is at least a hand's width
    /// wide as far as aiming is concerned.
    /// </remarks>
    private const double MinimumAimRadius = 0.3;

    /// <summary>Below this the lamp is dark, and is dropped rather than submitted as a dim light.</summary>
    /// <remarks>
    /// A light at a thousandth of its output changes no pixel, but it still occupies one of the sixty-four
    /// slots the shader has — so a switched-off lamp that lingered at 0.0005 would switch a real one at the
    /// end of the corridor off instead. Snapped to zero rather than approached.
    /// </remarks>
    private const float DarkLevel = 0.002f;

    private readonly Lamp[] _lamps;
    private readonly IReadOnlyList<LightFixture> _fixtures;

    /// <summary>Initializes a new instance of the <see cref="LampSwitchboard"/> class.</summary>
    /// <param name="fixtures">The level's lamps, in the order everything else indexes them by.</param>
    /// <param name="seed">
    /// Mixed into each lamp's generator. Levels differ by it, so two missions loaded in one session do
    /// not flicker identically; a test pins it to get the same sequence twice.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="fixtures"/> is <see langword="null"/>.</exception>
    public LampSwitchboard(IReadOnlyList<LightFixture> fixtures, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(fixtures);

        _fixtures = fixtures;
        _lamps = new Lamp[fixtures.Count];

        for (int i = 0; i < _lamps.Length; i++)
        {
            // Every lamp gets its own stream, decorrelated by index as well as by the level's seed: two
            // fixtures a metre apart must not draw the same numbers, and multiplying the index by an odd
            // constant before mixing is what stops neighbouring seeds producing neighbouring sequences.
            _lamps[i] = new Lamp
            {
                Mode = LampMode.On,
                Level = 1f,
                Rng = (uint)((seed * 0x9E3779B1u) + ((i + 1) * 0x85EBCA6Bu)) | 1u,
            };
        }
    }

    /// <summary>Gets how many lamps are on the board.</summary>
    public int Count => _lamps.Length;

    /// <summary>Gets a number that changes whenever any lamp's output moved.</summary>
    /// <remarks>
    /// For consumers that cannot cheaply be updated every frame — the surface cache's emitter buffer is a
    /// GPU upload — so they can re-read only when there is something to re-read. It counts updates that
    /// changed something rather than updates, so a level with nothing switched never bumps it at all.
    /// </remarks>
    public int Revision { get; private set; }

    /// <summary>Gets a value indicating whether every lamp is simply on, as the level ships it.</summary>
    public bool IsUntouched
    {
        get
        {
            foreach (Lamp lamp in _lamps)
            {
                if (lamp.Mode != LampMode.On || lamp.Transition != Transition.Steady)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Returns what a lamp has been told to be.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns>The lamp's mode, or <see cref="LampMode.On"/> for an index that names no lamp.</returns>
    public LampMode ModeOf(int index) =>
        (uint)index < (uint)_lamps.Length ? _lamps[index].Mode : LampMode.On;

    /// <summary>Returns whether a lamp has a fault in it, whether or not it is switched on.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns><see langword="true"/> when the lamp flickers, or would if it were switched on.</returns>
    /// <remarks>
    /// A dying tube in a fitting nobody has switched on is still a dying tube; breaking a lamp that is
    /// off arms the fault rather than lighting it, and this is how anything asking can tell.
    /// </remarks>
    public bool IsFaulty(int index) =>
        (uint)index < (uint)_lamps.Length && (_lamps[index].Mode == LampMode.Broken || _lamps[index].WasBroken);

    /// <summary>Returns how much of its rated output a lamp is giving out, from nought to one.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns>The fraction, or one for an index that names no lamp.</returns>
    /// <remarks>
    /// One number for the punctual light, the geometry's emission and the bounce alike. They are three
    /// views of one lamp and must not be able to drift apart.
    /// </remarks>
    public float LevelOf(int index) =>
        (uint)index < (uint)_lamps.Length ? _lamps[index].Level : 1f;

    /// <summary>Returns what to multiply a lamp's colour by while it is cooling.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns>A multiplier per channel; white whenever the lamp is not cooling.</returns>
    /// <remarks>
    /// <para>
    /// A filament does not merely get dimmer when the current stops — it gets COLDER, and a colder
    /// black body is redder. The last thing anyone sees of a lamp being switched off is a brief orange
    /// ember, and leaving it out makes the fade read as an opacity ramp rather than as a light going out.
    /// </para>
    /// <para>
    /// Applied to the cast light only, because the geometry's emission is a scalar multiple of the
    /// surface's own albedo and has no channel to carry a tint. By the time the shift is strong the lamp
    /// is at a few per cent of its output and its panel contributes nothing to the frame, so the two
    /// disagreeing there is not visible; at the start of the fade, where the panel is still bright, the
    /// shift is nearly nothing and they agree.
    /// </para>
    /// </remarks>
    public Vector3f TintOf(int index)
    {
        if ((uint)index >= (uint)_lamps.Length || _lamps[index].Transition != Transition.Cooling)
        {
            return Vector3f.One;
        }

        // Toward a cooling filament rather than to it: the ember is orange, not red, and a channel taken
        // the whole way to zero turns the last frames of the fade into a colour no lamp has ever been.
        float cold = Math.Clamp(1f - _lamps[index].Level, 0f, 1f);
        float amount = cold * cold * 0.85f;

        return new Vector3f(1f, 1f - (0.38f * amount), 1f - (0.72f * amount));
    }

    /// <summary>Switches a lamp off, or a switched-off one back on.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns>The mode the lamp is now in.</returns>
    /// <remarks>
    /// A broken lamp switches off like any other, and comes back broken: the switch and the fault are
    /// different facts about the fitting, and pulling the breaker does not repair a tube.
    /// </remarks>
    public LampMode Switch(int index)
    {
        if ((uint)index >= (uint)_lamps.Length)
        {
            return LampMode.On;
        }

        ref Lamp lamp = ref _lamps[index];

        if (lamp.Mode == LampMode.Off)
        {
            lamp.Mode = lamp.WasBroken ? LampMode.Broken : LampMode.On;
            BeginWarming(ref lamp);
        }
        else
        {
            lamp.WasBroken = lamp.Mode == LampMode.Broken;
            lamp.Mode = LampMode.Off;
            BeginCooling(ref lamp);
        }

        Revision++;
        return lamp.Mode;
    }

    /// <summary>Breaks a lamp so it flickers, or repairs one that is already broken.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns>The mode the lamp is now in.</returns>
    /// <remarks>
    /// Breaking a lamp that is switched off arms the fault without switching it on — it will flicker when
    /// somebody turns it back on, which is how a failing tube actually announces itself.
    /// </remarks>
    public LampMode Break(int index)
    {
        if ((uint)index >= (uint)_lamps.Length)
        {
            return LampMode.On;
        }

        ref Lamp lamp = ref _lamps[index];

        if (lamp.Mode == LampMode.Off)
        {
            lamp.WasBroken = !lamp.WasBroken;
            Revision++;
            return lamp.Mode;
        }

        if (lamp.Mode == LampMode.Broken)
        {
            lamp.Mode = LampMode.On;
            lamp.WasBroken = false;

            // Back to full the way a lamp does, not the way a variable does: a tube that has been
            // stuttering at a tenth of its output does not reappear at full brightness in one frame.
            BeginWarming(ref lamp);
        }
        else
        {
            lamp.Mode = LampMode.Broken;
            lamp.WasBroken = true;

            // Failing tubes announce themselves by dropping out first and then trying to restrike, which
            // is the sequence a person recognises; starting mid-run reads as a light being dimmed. It
            // goes out on the same curve as any other lamp — the arc extinguishing is the arc
            // extinguishing, whether a switch or a fault stopped it — and the cooling's own completion
            // hands the lamp over to the fault.
            BeginCooling(ref lamp);
        }

        Revision++;
        return lamp.Mode;
    }

    /// <summary>Puts every lamp back the way the level ships it.</summary>
    public void Reset()
    {
        for (int i = 0; i < _lamps.Length; i++)
        {
            ref Lamp lamp = ref _lamps[i];
            lamp.Mode = LampMode.On;
            lamp.WasBroken = false;
            lamp.Transition = Transition.Steady;
            lamp.Level = 1f;
        }

        Revision++;
    }

    /// <summary>Advances every lamp by the time that has passed.</summary>
    /// <param name="seconds">Real elapsed time, in seconds. Clamped, so a stalled frame cannot skip a fade.</param>
    public void Update(float seconds)
    {
        // A load hitch or a dragged window hands this a whole second, which would step a 40 ms strike
        // clean over its flash and land on the far side looking like an instant switch. Clamped to about
        // three frames' worth: the fade then runs slow through a hitch rather than not running at all.
        float step = Math.Clamp(seconds, 0f, 0.05f);

        if (step <= 0f)
        {
            return;
        }

        bool moved = false;

        for (int i = 0; i < _lamps.Length; i++)
        {
            ref Lamp lamp = ref _lamps[i];
            float before = lamp.Level;

            switch (lamp.Transition)
            {
                case Transition.Cooling:
                    lamp.Elapsed += step;
                    lamp.Level = lamp.From * Cooling(lamp.Elapsed);

                    if (lamp.Level <= DarkLevel)
                    {
                        lamp.Level = 0f;
                        lamp.Transition = Transition.Steady;

                        // A tube switched off while broken resumes its fault the moment it is lit again,
                        // and the state machine has to be waiting for it rather than mid-run.
                        if (lamp.Mode == LampMode.Broken)
                        {
                            EnterBroken(ref lamp, BrokenPhase.Dark);
                        }
                    }

                    break;

                case Transition.Warming:
                    lamp.Elapsed += step;
                    lamp.Level = lamp.From + ((1f - lamp.From) * Warming(ref lamp));

                    if (lamp.Level >= 0.995f)
                    {
                        lamp.Level = 1f;
                        lamp.Transition = Transition.Steady;

                        // Struck, warmed up — and faulty. From here the fault owns the lamp.
                        if (lamp.Mode == LampMode.Broken)
                        {
                            EnterBroken(ref lamp, BrokenPhase.Run);
                        }
                    }

                    break;

                default:
                    if (lamp.Mode == LampMode.Broken)
                    {
                        StepBroken(ref lamp, step);
                    }

                    break;
            }

            if (!moved && MathF.Abs(lamp.Level - before) > 0.0005f)
            {
                moved = true;
            }
        }

        if (moved)
        {
            Revision++;
        }
    }

    /// <summary>Finds the lamp the crosshair is on.</summary>
    /// <param name="eye">Where the view is, in world metres.</param>
    /// <param name="forward">The way it looks. Need not be normalised.</param>
    /// <param name="reach">How far to look, in metres.</param>
    /// <param name="index">Receives the fixture's index.</param>
    /// <param name="distance">Receives how far along the ray it stands, in metres.</param>
    /// <returns><see langword="true"/> when a lamp is under the crosshair.</returns>
    /// <remarks>
    /// <para>
    /// Against the fixtures rather than against the geometry, because a fixture is what there is to
    /// switch: the extractor merges a lamp's triangles into one light wherever they stand, and a ray test
    /// against the housing's mesh would find the model the lamp is part of rather than the lamp.
    /// </para>
    /// <para>
    /// Nearest along the ray wins rather than closest to its axis. That is what a crosshair means — the
    /// thing in front — and it is also the only rule that stays stable as the view turns, since a
    /// tie-break on angle swaps between two lamps for a sub-pixel movement of the mouse.
    /// </para>
    /// <para>
    /// It answers nothing about what stands between: a wall is not in the fixture list. The caller casts
    /// against the world to check that, which it can do because it already has the level's collision.
    /// </para>
    /// </remarks>
    public bool TryPick(Vector3d eye, Vector3d forward, double reach, out int index, out double distance)
    {
        index = -1;
        distance = 0.0;

        double length = forward.Length;

        if (length <= 0.0 || _fixtures.Count == 0)
        {
            return false;
        }

        Vector3d axis = forward / length;
        double best = double.MaxValue;

        for (int i = 0; i < _fixtures.Count; i++)
        {
            Vector3d offset = _fixtures[i].Position - eye;
            double along = Vector3d.Dot(offset, axis);

            if (along <= 0.0 || along >= reach || along >= best)
            {
                continue;
            }

            // How far the fixture sits off the crosshair's axis, against how far off it is allowed to be
            // at that range. The fixture's own size counts: a two-metre batten seen end-on is a small
            // target by its centroid alone, and the player is aiming at the tube, not at its middle.
            double offAxis = (offset - (axis * along)).Length;
            double allowed = Math.Max(
                Math.Max(_fixtures[i].Extent, MinimumAimRadius),
                along * AimTangent);

            if (offAxis <= allowed)
            {
                best = along;
                index = i;
            }
        }

        distance = index >= 0 ? best : 0.0;
        return index >= 0;
    }

    /// <summary>Finds the lamp nearest a point, for binding a placement's glowing geometry to a light.</summary>
    /// <param name="position">Where to look from, in world metres.</param>
    /// <param name="within">How far to accept, in metres.</param>
    /// <returns>The fixture's index, or -1 when nothing is near enough.</returns>
    /// <remarks>
    /// The fixture list is the merged conclusion about a level's lamps and the placement list is the
    /// level's own; there is no key joining them, because a fixture can be the emissive faces of several
    /// placements at once and a placement can be nowhere near the fixture its texture contributed to.
    /// Proximity is what actually relates them, and it is exact enough: two lamps closer together than
    /// this were merged into one fixture in the first place.
    /// </remarks>
    public int NearestFixture(Vector3d position, double within)
    {
        int found = -1;
        double best = within * within;

        for (int i = 0; i < _fixtures.Count; i++)
        {
            Vector3d offset = _fixtures[i].Position - position;
            double squared = offset.LengthSquared;

            if (squared < best)
            {
                best = squared;
                found = i;
            }
        }

        return found;
    }

    /// <summary>How much of its output a lamp still gives out, this long after the current stopped.</summary>
    /// <remarks>
    /// <para>
    /// Two decays added, because a fitting going out has two things happening in it and they run at very
    /// different rates. Most of the light stops within a couple of mains cycles — an arc extinguishes,
    /// a filament's surface cools — and that is the fast term, around 35 ms. What is left is thermal
    /// mass and phosphor persistence, and it lingers for a fifth of a second as the ember or the
    /// afterglow everybody has seen and nobody would describe.
    /// </para>
    /// <para>
    /// A single exponential gets the timing right and the LOOK wrong: it is either fast enough to be a
    /// snap or slow enough to be a dissolve, and never both. The sum is what reads as a light going out.
    /// </para>
    /// </remarks>
    private static float Cooling(float seconds) =>
        (0.82f * MathF.Exp(-seconds / 0.035f)) + (0.18f * MathF.Exp(-seconds / 0.16f));

    /// <summary>How far into coming back on a lamp is, from nought to one.</summary>
    /// <remarks>
    /// <para>
    /// A tube does not fade up. It draws a dim glow across the cathodes, strikes — one hard flash, far
    /// brighter than the dim it came from — drops nearly dark again as the arc settles, and only then
    /// climbs to full over a few tenths of a second as the mercury warms. That sequence is the whole
    /// reason a switched-on lamp reads as a lamp; a linear ramp reads as a slider being dragged.
    /// </para>
    /// <para>
    /// Not every fitting does it, so not every lamp here does: a fixture whose generator says otherwise
    /// simply warms, the way a filament does. A corridor whose lamps all strike in the same way at the
    /// same moment is the artefact that says a single effect is being played on all of them.
    /// </para>
    /// </remarks>
    private static float Warming(ref Lamp lamp)
    {
        float t = lamp.Elapsed;

        if (lamp.Strikes)
        {
            float strike = lamp.StrikeAt;

            if (t < strike)
            {
                // The cathode glow: visible, dim, and not yet the lamp.
                return 0.12f * (t / MathF.Max(strike, 0.0001f));
            }

            if (t < strike + 0.045f)
            {
                return 0.95f;
            }

            if (t < strike + 0.085f)
            {
                return 0.06f;
            }

            t -= strike + 0.085f;
        }

        // The mercury warming, or the filament heating: fast at first and asymptotic, which is what both
        // of them actually do. Offset so it starts from where the strike left the lamp rather than from
        // black, and normalised so the curve reaches one rather than approaching it forever.
        return Math.Clamp(1f - MathF.Exp(-t / 0.19f), 0f, 1f);
    }

    private static void BeginCooling(ref Lamp lamp)
    {
        lamp.Transition = Transition.Cooling;
        lamp.Elapsed = 0f;
        lamp.From = lamp.Level;
    }

    private static void BeginWarming(ref Lamp lamp)
    {
        lamp.Transition = Transition.Warming;
        lamp.Elapsed = 0f;
        lamp.From = lamp.Level;

        // Rolled per switch-on rather than per lamp: the same fitting does not strike identically every
        // time, and a lamp that always flashes at exactly 90 ms is a canned animation.
        //
        // Only from dark, because a strike is an arc being established and a lamp that is already half
        // lit has one. Without that condition a lamp brought back up from nine tenths flashes on its way
        // to full, which is not something any fitting does.
        lamp.Strikes = lamp.From < 0.15f && NextFloat(ref lamp.Rng) < 0.6f;
        lamp.StrikeAt = 0.05f + (NextFloat(ref lamp.Rng) * 0.08f);
    }

    /// <summary>Advances a failing lamp, in the phase it is currently stuck in.</summary>
    /// <remarks>
    /// <para>
    /// A broken fluorescent is not noise. It is a loop of four recognisable states — a dim glow at the
    /// cathodes, a burst of rapid restrikes, a stretch of running unsteadily, and darkness — and what
    /// makes it read as broken rather than as a flicker shader is that the durations are wildly uneven
    /// and the transitions are not a cycle. Random per frame gives white noise, which looks like a fault
    /// in the renderer; this looks like a fault in the lamp.
    /// </para>
    /// <para>
    /// Every phase's brightness is a function of the phase's own clock, never of the frame, so the same
    /// fault plays out identically whatever the frame rate is.
    /// </para>
    /// </remarks>
    private static void StepBroken(ref Lamp lamp, float step)
    {
        lamp.BrokenElapsed += step;
        lamp.BrokenLeft -= step;

        if (lamp.BrokenLeft <= 0f)
        {
            float roll = NextFloat(ref lamp.Rng);

            EnterBroken(ref lamp, lamp.Broken switch
            {
                BrokenPhase.Run => roll < 0.5f ? BrokenPhase.Dark
                    : roll < 0.8f ? BrokenPhase.Glow : BrokenPhase.Strike,
                BrokenPhase.Dark => roll < 0.75f ? BrokenPhase.Strike : BrokenPhase.Glow,
                BrokenPhase.Strike => roll < 0.55f ? BrokenPhase.Run
                    : roll < 0.8f ? BrokenPhase.Dark : BrokenPhase.Glow,
                _ => roll < 0.6f ? BrokenPhase.Strike : BrokenPhase.Dark,
            });
        }

        float t = lamp.BrokenElapsed;

        lamp.Level = lamp.Broken switch
        {
            // The arc trying and failing to hold: a hard square wave, because that is what an arc does —
            // it is either struck or it is not, and there is nothing in between to ramp through.
            BrokenPhase.Strike => ((int)(t * lamp.BrokenRate) & 1) == 0 ? lamp.BrokenPeak : 0.04f,

            // Lit, but not well. A slow wobble a few per cent deep, at a rate low enough to be seen
            // rather than aliased into a shimmer by whatever the frame rate happens to be.
            BrokenPhase.Run => lamp.BrokenPeak
                * (1f - (0.07f * (0.5f + (0.5f * MathF.Sin(t * lamp.BrokenRate))))),

            // The cathodes glowing with no arc across them: dim, orange in a real tube, and unsteady.
            BrokenPhase.Glow => 0.06f + (0.05f * (0.5f + (0.5f * MathF.Sin(t * lamp.BrokenRate)))),

            _ => 0f,
        };
    }

    private static void EnterBroken(ref Lamp lamp, BrokenPhase phase)
    {
        lamp.Broken = phase;
        lamp.BrokenElapsed = 0f;

        switch (phase)
        {
            case BrokenPhase.Strike:
                // Twelve to thirty-five restrikes a second: fast enough to read as a fault rather than as
                // a signal, slow enough that a 60 Hz frame still resolves individual flashes.
                lamp.BrokenRate = 12f + (NextFloat(ref lamp.Rng) * 23f);
                lamp.BrokenPeak = 0.75f + (NextFloat(ref lamp.Rng) * 0.25f);
                lamp.BrokenLeft = 0.08f + (NextFloat(ref lamp.Rng) * 0.42f);
                break;

            case BrokenPhase.Run:
                lamp.BrokenRate = 5f + (NextFloat(ref lamp.Rng) * 9f);
                lamp.BrokenPeak = 0.8f + (NextFloat(ref lamp.Rng) * 0.2f);

                // The long phase, and deliberately the widest range of the four: a tube that fails every
                // second on the second is a metronome. Some of these hold for four seconds and the room
                // looks fine, which is what makes the next dropout land.
                lamp.BrokenLeft = 0.4f + (NextFloat(ref lamp.Rng) * 3.6f);
                break;

            case BrokenPhase.Glow:
                lamp.BrokenRate = 3f + (NextFloat(ref lamp.Rng) * 6f);
                lamp.BrokenLeft = 0.12f + (NextFloat(ref lamp.Rng) * 0.78f);
                break;

            default:
                lamp.BrokenLeft = 0.04f + (NextFloat(ref lamp.Rng) * 0.76f);
                break;
        }
    }

    /// <summary>A lamp's own generator: xorshift32, advanced in place.</summary>
    /// <remarks>
    /// Per lamp rather than shared, so the number of lamps on a level and the order they are updated in
    /// cannot change how any one of them behaves — and so a test can pin one lamp's whole sequence.
    /// </remarks>
    private static float NextFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        return (state >> 8) * (1f / 16777216f);
    }

    /// <summary>Which curve, if any, a lamp is currently on.</summary>
    private enum Transition
    {
        Steady,
        Cooling,
        Warming,
    }

    /// <summary>What a failing lamp is doing at the moment.</summary>
    private enum BrokenPhase
    {
        Run,
        Strike,
        Glow,
        Dark,
    }

    /// <summary>One lamp's switch, its filament and its fault.</summary>
    private struct Lamp
    {
        public LampMode Mode;

        /// <summary>Whether the fault is still there behind a lamp that is switched off.</summary>
        public bool WasBroken;

        public float Level;

        public Transition Transition;

        /// <summary>Seconds into the current curve.</summary>
        public float Elapsed;

        /// <summary>Where the curve started, so an interrupted fade resumes from where it got to.</summary>
        public float From;

        public bool Strikes;

        public float StrikeAt;

        public BrokenPhase Broken;

        public float BrokenElapsed;

        public float BrokenLeft;

        public float BrokenRate;

        public float BrokenPeak;

        public uint Rng;
    }
}
