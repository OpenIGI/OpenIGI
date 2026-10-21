using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;
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
/// swapped. The curves are in <see cref="LampPhysics.Cooling"/> and <see cref="LampPhysics.Warming"/>
/// and each is documented with what it is modelling.
/// </para>
/// <para>
/// Everything is driven from a seconds delta and a per-lamp deterministic generator — never from the
/// frame counter — so a lamp behaves the same at 30 fps and at 300, and so a row of ceiling tubes does
/// not strike, stutter and fail in unison. That last part matters more than it sounds: identical
/// flicker across six fixtures reads as one shader effect, not as six broken tubes.
/// </para>
/// <para>
/// <b>Two things decide whether a lamp is fed, and this keeps them apart.</b> The wall switch is the
/// player's (<see cref="Switch"/>); the supply is the daylight's (<see cref="SetSupply"/>), which a
/// photocell closes at dusk and opens at dawn. They are in series, as in a real installation: a lamp
/// somebody switched off stays off when night falls, and comes on by itself at dusk if they switch it
/// back on during the day. A lamp the supply brings up starts COLD -- it has stood dark all day -- and
/// that start is longer and less certain than flicking a switch on a lamp that was lit a minute ago.
/// </para>
/// </remarks>
public sealed class LampSwitchboard
{
    /// <summary>How long one burst of a discharge lamp's ignitor lasts, in seconds.</summary>
    /// <remarks>Public because the ignitor's sound is made to this length, and the two must agree.</remarks>
    public const float IgnitorSeconds = 0.5f;

    /// <summary>The most sound events kept for a consumer that has not come for them.</summary>
    /// <remarks>
    /// A board nothing is listening to -- a test, a silent device -- must not grow without bound. A
    /// consumer drains every frame, and a frame never raises anything like this many.
    /// </remarks>
    private const int MaxPendingEvents = 256;

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
    private readonly List<LampEvent> _events = [];

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
                Supplied = true,
                Level = 1f,
                Rng = (uint)((seed * 0x9E3779B1u) + ((i + 1) * 0x85EBCA6Bu)) | 1u,
            };

            Classify(ref _lamps[i], fixtures[i], seed, i);
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
                if (lamp.Mode != LampMode.On || !lamp.Supplied || lamp.Transition != LampTransition.Steady)
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

    /// <summary>Returns whether the daylight's side of a lamp's circuit is closed.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns><see langword="true"/> when the lamp is fed, or for an index that names no lamp.</returns>
    public bool IsSupplied(int index) =>
        (uint)index >= (uint)_lamps.Length || _lamps[index].Supplied;

    /// <summary>Returns what sort of lamp a fitting was taken to be.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns>Its character, or <see cref="LampCharacter.Filament"/> for an index that names no lamp.</returns>
    public LampCharacter CharacterOf(int index) =>
        (uint)index < (uint)_lamps.Length ? _lamps[index].Character : LampCharacter.Filament;

    /// <summary>Moves the sounds raised since the last call into a list, and forgets them.</summary>
    /// <param name="into">The list to append to.</param>
    public void DrainEvents(List<LampEvent> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.AddRange(_events);
        _events.Clear();
    }

    /// <summary>Closes or opens the daylight's side of a lamp's circuit, the way a photocell does.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <param name="on">Whether the supply is to be on.</param>
    /// <remarks>
    /// <para>
    /// Nothing happens to the light of a lamp somebody has switched off at the wall -- the supply is on the
    /// far side of the switch -- but the supply is remembered, so switching it back on later finds it lit
    /// or dark as the daylight says.
    /// </para>
    /// <para>
    /// A lamp brought up this way starts cold: a tube may need several goes of its starter and then glows
    /// short of full for a few seconds while it warms, and a discharge lantern's ignitor may crackle more
    /// than once before the arc holds, after which it climbs to full over many seconds rather than a
    /// fraction of one. That is the start a lamp gets after standing dark all day, and it is most of what
    /// makes a dusk read as lamps rather than as a switch being thrown.
    /// </para>
    /// </remarks>
    public void SetSupply(int index, bool on)
    {
        if ((uint)index >= (uint)_lamps.Length || _lamps[index].Supplied == on)
        {
            return;
        }

        ref Lamp lamp = ref _lamps[index];
        lamp.Supplied = on;

        if (lamp.Mode != LampMode.Off)
        {
            if (on)
            {
                LampPhysics.BeginWarming(ref lamp, cold: true);
            }
            else
            {
                // The ballast letting go is only a sound when there was something running through it.
                if (lamp.Level > 0.02f && lamp.Character != LampCharacter.Filament)
                {
                    Raise(index, LampSound.Extinguish);
                }

                LampPhysics.BeginCooling(ref lamp);
            }
        }

        Revision++;
    }

    /// <summary>Sets the daylight's side of a lamp's circuit with no transition at all.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <param name="on">Whether the supply is to be on.</param>
    /// <remarks>
    /// For a level opening, which has to open with its lamps already in the state its hour puts them in:
    /// a night mission whose every lamp strikes as the first frame appears, or a noon whose outdoor lamps
    /// fade out over the loading screen, is the lamps visibly reacting to the level being loaded rather
    /// than to the time of day. Silent, for the same reason.
    /// </remarks>
    public void SetSupplyImmediately(int index, bool on)
    {
        if ((uint)index >= (uint)_lamps.Length)
        {
            return;
        }

        ref Lamp lamp = ref _lamps[index];
        lamp.Supplied = on;
        lamp.Transition = LampTransition.Steady;

        if (on && lamp.Mode != LampMode.Off)
        {
            lamp.Level = 1f;

            if (lamp.Mode == LampMode.Broken)
            {
                LampPhysics.EnterBroken(ref lamp, LampBrokenPhase.Run);
            }
        }
        else
        {
            lamp.Level = 0f;
        }

        Revision++;
    }

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
    /// <para>
    /// And a lamp coming on is not its own colour yet either. A tube's cathodes glow orange before the arc
    /// strikes; a discharge lantern's arc starts in the wrong gas -- the pink-red of a sodium lamp's neon
    /// starter fill, the blue-green of a metal-halide's mercury -- and only reaches its own colour as it
    /// reaches its own brightness. Under the same reasoning as the ember, the light carries it and the
    /// fixture does not.
    /// </para>
    /// </remarks>
    public Vector3f TintOf(int index)
    {
        if ((uint)index >= (uint)_lamps.Length)
        {
            return Vector3f.One;
        }

        ref Lamp lamp = ref _lamps[index];

        if (lamp.Transition == LampTransition.Warming)
        {
            return LampPhysics.WarmingTint(ref lamp);
        }

        if (lamp.Transition != LampTransition.Cooling)
        {
            return Vector3f.One;
        }

        // Toward a cooling filament rather than to it: the ember is orange, not red, and a channel taken
        // the whole way to zero turns the last frames of the fade into a colour no lamp has ever been.
        float cold = Math.Clamp(1f - lamp.Level, 0f, 1f);
        float amount = cold * cold * 0.85f;

        return new Vector3f(1f, 1f - (0.38f * amount), 1f - (0.72f * amount));
    }

    /// <summary>Switches a lamp off, or a switched-off one back on.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns>The mode the lamp is now in.</returns>
    /// <remarks>
    /// <para>
    /// A broken lamp switches off like any other, and comes back broken: the switch and the fault are
    /// different facts about the fitting, and pulling the breaker does not repair a tube.
    /// </para>
    /// <para>
    /// Switched on while the daylight has its supply off, the lamp stays dark and will light when the
    /// supply comes on; switched off, it stays off whatever the supply does.
    /// </para>
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

            if (lamp.Supplied)
            {
                LampPhysics.BeginWarming(ref lamp, cold: false);
            }
        }
        else
        {
            lamp.WasBroken = lamp.Mode == LampMode.Broken;
            lamp.Mode = LampMode.Off;

            if (lamp.Supplied)
            {
                LampPhysics.BeginCooling(ref lamp);
            }
        }

        Raise(index, LampSound.Switch);
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
            // Unless the daylight has its supply off, in which case it is repaired in the dark.
            if (lamp.Supplied)
            {
                LampPhysics.BeginWarming(ref lamp, cold: false);
            }
        }
        else
        {
            lamp.Mode = LampMode.Broken;
            lamp.WasBroken = true;

            // Failing tubes announce themselves by dropping out first and then trying to restrike, which
            // is the sequence a person recognises; starting mid-run reads as a light being dimmed. It
            // goes out on the same curve as any other lamp — the arc extinguishing is the arc
            // extinguishing, whether a switch or a fault stopped it — and the cooling's own completion
            // hands the lamp over to the fault. A lamp the daylight has put out is already dark; the
            // fault waits for the supply.
            if (lamp.Supplied)
            {
                LampPhysics.BeginCooling(ref lamp);
            }
        }

        Revision++;
        return lamp.Mode;
    }

    /// <summary>Puts every lamp's switch and fault back the way the level ships them.</summary>
    /// <remarks>
    /// The supply is the daylight's and is left where it is: a reset at noon puts the yard's lamps back
    /// on their switches, and they stay dark until dusk.
    /// </remarks>
    public void Reset()
    {
        for (int i = 0; i < _lamps.Length; i++)
        {
            ref Lamp lamp = ref _lamps[i];
            lamp.Mode = LampMode.On;
            lamp.WasBroken = false;
            lamp.Transition = LampTransition.Steady;
            lamp.Level = lamp.Supplied ? 1f : 0f;
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
                case LampTransition.Cooling:
                    lamp.Elapsed += step;
                    lamp.Level = lamp.From * LampPhysics.Cooling(lamp.Elapsed);

                    if (lamp.Level <= DarkLevel)
                    {
                        lamp.Level = 0f;
                        lamp.Transition = LampTransition.Steady;

                        // A tube switched off while broken resumes its fault the moment it is lit again,
                        // and the state machine has to be waiting for it rather than mid-run. One the
                        // daylight put out waits for the supply instead.
                        if (lamp.Mode == LampMode.Broken && lamp.Supplied)
                        {
                            LampPhysics.EnterBroken(ref lamp, LampBrokenPhase.Dark);
                        }
                    }

                    break;

                case LampTransition.Warming:
                    lamp.Elapsed += step;
                    lamp.Level = lamp.From + ((1f - lamp.From) * LampPhysics.Warming(ref lamp, i, _events));

                    if (lamp.Level >= 0.995f)
                    {
                        lamp.Level = 1f;
                        lamp.Transition = LampTransition.Steady;

                        // Struck, warmed up — and faulty. From here the fault owns the lamp.
                        if (lamp.Mode == LampMode.Broken)
                        {
                            LampPhysics.EnterBroken(ref lamp, LampBrokenPhase.Run);
                        }
                    }

                    break;

                default:
                    if (lamp.Mode == LampMode.Broken && lamp.Supplied && LampPhysics.StepBroken(ref lamp, step))
                    {
                        Raise(i, LampSound.Sputter);
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

    /// <summary>Decides what sort of lamp a fixture is, from its colour and a hash of where it is listed.</summary>
    /// <remarks>
    /// Its own hash rather than the lamp's generator, so giving lamps a character did not move any
    /// lamp's existing flicker. An amber fixture leans to the sodium lantern, a white one to the tube.
    /// </remarks>
    private static void Classify(ref Lamp lamp, in LightFixture fixture, int seed, int index)
    {
        uint h = ((uint)seed * 0x9E3779B1u) ^ (((uint)index + 0x632BE5ABu) * 0x85EBCA6Bu);
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        float roll = (h >> 8) * (1f / 16777216f);

        Vector3f colour = fixture.Colour;
        bool amber = colour.X > 0.05f && colour.Z < colour.X * 0.62f;

        lamp.Sodium = amber;
        lamp.Character = amber
            ? roll < 0.55f ? LampCharacter.Discharge : roll < 0.85f ? LampCharacter.Filament : LampCharacter.Tube
            : roll < 0.6f ? LampCharacter.Tube : roll < 0.82f ? LampCharacter.Filament : LampCharacter.Discharge;
    }

    /// <summary>Queues a sound for whoever plays them, unless nobody has come for the last few hundred.</summary>
    /// <param name="events">The board's pending sounds.</param>
    /// <param name="index">The fixture's index.</param>
    /// <param name="sound">What it did.</param>
    internal static void Raise(List<LampEvent> events, int index, LampSound sound)
    {
        if (events.Count < MaxPendingEvents)
        {
            events.Add(new LampEvent(index, sound));
        }
    }

    private void Raise(int index, LampSound sound) => Raise(_events, index, sound);
}
