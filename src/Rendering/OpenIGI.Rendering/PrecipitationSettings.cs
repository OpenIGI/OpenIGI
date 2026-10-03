namespace OpenIGI.Rendering;

/// <summary>
/// Everything the remastered storm is tuned by: how much falls, how it moves, and how it reads.
/// </summary>
/// <remarks>
/// <para>
/// The vanilla path draws rain the way the original does — a fixed band of thin streaks around the
/// camera, alpha from the level's <c>RainEffect</c> task, and nothing else (see <c>RainRenderer</c>).
/// This is the model behind the remastered one, which is a different thing entirely: drops that are
/// spawned into a volume that travels with the eye, blown by a wind that gusts, stretched along the
/// direction they are actually moving, faded where they would otherwise clip through a wall, and
/// answered on the ground by splashes.
/// </para>
/// <para>
/// <b>Metres, seconds, degrees.</b> Every distance here is metres and every duration seconds, matching
/// the weather renderers rather than the 4096-units-per-metre world space — a storm is authored by eye
/// at human scale, and converting once at the point of use is cheaper to reason about than carrying
/// engine units through thirty fields.
/// </para>
/// <para>
/// <b>Why a record, and why defaults that already look right.</b> These are read every frame and
/// written rarely — from a preset, a console variable, or a level's own weather task — so the type is
/// an immutable record with <c>init</c> setters and a <see cref="Default"/> that is not a neutral zero
/// but a working drizzle. A field left alone must produce something plausible; a settings object where
/// half the defaults render nothing is a trap that costs an afternoon before anyone suspects the
/// defaults rather than the renderer.
/// </para>
/// <para>
/// <b>The spawn volume is the thing that gets sized wrong.</b> A volume tuned for a courtyard —
/// ten metres across, a metre thick, a second of life — produces almost nothing over open ground,
/// because the drops die before they cross the eye and the column is narrower than the view. The
/// defaults below are sized for an outdoor level; see <see cref="SpawnRadiusMetres"/>.
/// </para>
/// </remarks>
public sealed record PrecipitationSettings
{
    /// <summary>A working outdoor storm: the defaults every other field is measured against.</summary>
    public static PrecipitationSettings Default { get; } = new();

    /// <summary>How much of the authored fall rate is used, from 0 (dry) to 1 (the full rate).</summary>
    /// <remarks>
    /// Separate from <see cref="DropsPerSecond"/> so a level, a script or the console can dial the
    /// storm up and down over time without losing the tuned density it is dialling away from.
    /// </remarks>
    public float Intensity { get; init; } = 1f;

    /// <summary>Drops spawned per second at full <see cref="Intensity"/>.</summary>
    /// <remarks>
    /// <para>
    /// This is the number that decides whether the storm reads as rain or as a few streaks drifting
    /// past, and it was set far too low. Eight hundred a second over a forty-metre column is two and a
    /// half thousand drops spread through five thousand square metres of air — thin enough that the eye
    /// reads each one separately instead of reading weather.
    /// </para>
    /// <para>
    /// The two numbers that matter are this one <em>and</em> <see cref="SpawnRadiusMetres"/>, because
    /// what is seen is drops per unit of air and not drops per second: quadrupling the rate while
    /// halving the column's area multiplies the density by eight.
    /// </para>
    /// </remarks>
    public float DropsPerSecond { get; init; } = 3200f;

    /// <summary>How long a drop lives before it is recycled, in seconds.</summary>
    /// <remarks>
    /// Multiplied by the fall speed this is how far a drop travels, so it and
    /// <see cref="SpawnAboveCameraMetres"/> together decide whether drops are still alive by the time
    /// they reach the eye. Too short and the column hangs above the player's head, visible only when
    /// looking up. Each drop is given somewhere between a little under half of this and all of it; most
    /// never see it, because a drop is retired the moment it reaches the ground. Even the shortest share
    /// has to outlast the slowest drop's fall from the top of the spawn slab to the ground.
    /// </remarks>
    public float LifetimeSeconds { get; init; } = 10f;

    /// <summary>The radius of the column drops spawn into, centred on the eye, in metres.</summary>
    /// <remarks>
    /// The column travels with the camera, so this is not how far the storm reaches — it is how much
    /// of the view is populated. Below about twenty metres the edge of the column is inside the
    /// player's view and the rain visibly stops in mid-air at a fixed distance.
    /// </remarks>
    public float SpawnRadiusMetres { get; init; } = 30f;

    /// <summary>The vertical thickness of the spawn slab, in metres.</summary>
    public float SpawnHeightMetres { get; init; } = 10f;

    /// <summary>How far above the eye the slab sits, in metres.</summary>
    public float SpawnAboveCameraMetres { get; init; } = 10f;

    /// <summary>How hard the air pushes a drop sideways, in metres per second SQUARED.</summary>
    /// <remarks>
    /// <para>
    /// An acceleration and not a velocity, which is the whole of why the fall carries a range of angles
    /// rather than one. The force goes on being integrated for the drop's whole life, so a drop's lean
    /// GROWS with its age, and the air holds drops of every age at once. Handing it over as a one-off
    /// velocity instead — which this did — gives every drop the same tiny lean for its whole fall, and a
    /// storm of parallel lines.
    /// </para>
    /// <para>
    /// What is tuned is the ANGLE and not the constant. A lean is the acceleration times how long the drop
    /// has been falling, and here a drop falls for about one and a half seconds: this column is ten to
    /// twenty metres deep and a drop is retired when it meets the ground. Reaching about seven degrees off
    /// vertical in that time takes <c>tan(7°) × 11.5 / 1.5</c>, which is where this comes from. Shorten
    /// the column or lengthen the life and this wants moving with it.
    /// </para>
    /// <para>
    /// Each drop draws a direction as well as a magnitude. Drawing the two axes independently instead
    /// puts more drops on the diagonals than on the axes, which gives the whole field a grain.
    /// </para>
    /// </remarks>
    public float SpreadMetresPerSecondSquared { get; init; } = 0.9f;


    /// <summary>Terminal fall speed in metres per second, before <see cref="FallSpeedScale"/>.</summary>
    /// <remarks>
    /// A <em>limit</em> and not the speed a drop is given: drops are spawned nearly at rest and pulled down
    /// by <see cref="GravityMetresPerSecondSquared"/> until they reach it, which is what makes the
    /// streaks short at the top of the column and long by the time they pass the eye. Handing every drop
    /// its terminal speed at birth loses that entirely and gives a column of identical streaks
    /// descending as one rigid sheet.
    /// </remarks>
    public float FallSpeedMetresPerSecond { get; init; } = 11.5f;

    /// <summary>How hard drops accelerate downwards, in metres per second squared.</summary>
    /// <remarks>
    /// Varied per drop over a narrow band rather than one figure for all of them, so the column spreads
    /// out as it falls instead of staying in the ranks it was spawned in. This is the middle of that band;
    /// the spread is applied at spawn.
    /// </remarks>
    public float GravityMetresPerSecondSquared { get; init; } = 12f;

    /// <summary>Multiplier on the fall speed: 0 hangs the drops, 1 is the tuned speed.</summary>
    public float FallSpeedScale { get; init; } = 1f;

    /// <summary>The compass direction the wind blows towards on average, in degrees.</summary>
    /// <remarks>The middle of the range the direction wanders over; see <see cref="WindSwingDegrees"/>.</remarks>
    public float WindDirectionDegrees { get; init; }

    /// <summary>How far either side of that direction the wind wanders, in degrees.</summary>
    public float WindSwingDegrees { get; init; } = 12f;

    /// <summary>How often the wind picks a new speed and direction to head towards, in seconds.</summary>
    /// <remarks>
    /// Not how long a gust lasts — the wind eases towards each new target over roughly this long and is
    /// given another before it arrives, so nothing about it ever settles. Short values make it restless
    /// in a way that reads as the camera swaying rather than as weather.
    /// </remarks>
    public float WindVariationSeconds { get; init; } = 7f;

    /// <summary>Lateral wind speed in metres per second.</summary>
    /// <remarks>
    /// Rain that falls dead vertical reads as static no matter how fast it moves, because a vertical
    /// streak on a vertical path has no apparent motion across the frame. A little wind is what makes
    /// it look like weather rather than a screen effect.
    /// </remarks>
    /// <remarks>
    /// The wind it settles around, not a fixed speed: the gust in
    /// <see cref="WindGustMetresPerSecond"/> is added to it and is <b>signed</b>. A storm with one wind
    /// speed is a storm whose rain leans at one fixed angle for as long as anyone watches, which reads
    /// as the whole screen being sheared rather than as air moving.
    /// </remarks>
    public float WindMetresPerSecond { get; init; } = 3.5f;

    /// <summary>How far either side of that the wind gusts, in metres per second.</summary>
    /// <remarks>
    /// <para>
    /// <b>Signed, and weighted towards the middle.</b> Taken as a range between a floor and a ceiling,
    /// the wind only ever blew one way and spent most of its time near neither end of the range — the
    /// rain leaned right, and kept leaning right. Real air does not do that. This is added to
    /// <see cref="WindMetresPerSecond"/> with a sign, so the lean crosses back through vertical and
    /// occasionally goes the other way entirely, and it is drawn from a triangular distribution rather
    /// than a flat one, so the wind spends most of its time near the middle and only visits the extremes.
    /// </para>
    /// <para>
    /// <b>Read these as an angle, not a speed.</b> What is on the screen is the lean, and the lean is
    /// the arctangent of this over the fall speed — so against a fall of eleven and a half metres a
    /// second, a wind of one is six degrees off vertical, which nobody can see and which reads as rain
    /// falling dead flat however carefully the wind is varied. Three and a half is about seventeen
    /// degrees, and the gust carries it between five and thirty. Any change to
    /// <see cref="FallSpeedMetresPerSecond"/> moves the whole range.
    /// </para>
    /// </remarks>
    public float WindGustMetresPerSecond { get; init; } = 2.5f;

    /// <summary>Vertical wind, in metres per second; positive lifts drops and slows the fall.</summary>
    public float WindUpdraftMetresPerSecond { get; init; }

    /// <summary>How strongly gusts modulate local density, from 0 (even) to 1 (sheets and gaps).</summary>
    /// <remarks>
    /// Gusts are a moving noise field sampled at the drop's position that scales its opacity, so the
    /// storm arrives in sheets rather than as a uniform screen of streaks. At 1 the gaps are total,
    /// which reads as the rain flickering on and off; the tuned value is well under half.
    /// </remarks>
    public float GustAmount { get; init; }

    /// <summary>How fast the gust field drifts, in metres per second.</summary>
    public float GustMetresPerSecond { get; init; } = 5f;

    /// <summary>The size of one gust cell, in metres. Smaller is busier.</summary>
    public float GustSizeMetres { get; init; } = 10f;

    /// <summary>The width of a drop's streak at unit distance, in metres.</summary>
    public float DropWidthMetres { get; init; } = 0.012f;

    /// <summary>The unstretched length of a drop's streak, in metres.</summary>
    public float DropLengthMetres { get; init; } = 0.22f;

    /// <summary>How far the streak stretches along its velocity, as a fraction of distance travelled.</summary>
    /// <remarks>
    /// A falling drop photographs as a streak because it moves during the exposure. Stretching along
    /// the drop's own velocity rather than straight down is what keeps wind-blown rain leaning the way
    /// it is actually travelling, instead of leaning while its streaks stay vertical.
    /// </remarks>
    public float MotionStretch { get; init; } = 0.5f;

    /// <summary>Extra streak width per metre of distance, so distant rain does not fall below a pixel.</summary>
    /// <remarks>
    /// The same cheat the impact particles use. A twelve-millimetre streak at sixty metres is a
    /// fraction of a pixel, which temporal antialiasing then removes entirely — the storm thins out
    /// with distance instead of building into a haze.
    /// </remarks>
    public float WidthBoostPerMetre { get; init; } = 0.0015f;

    /// <summary>Inside this distance a drop is drawn from the defocused sheet, in metres.</summary>
    /// <remarks>
    /// <para>
    /// A lens focused on the scene cannot resolve a drop a metre from the front element, and that is
    /// drawn rather than culled: the streaks exist twice, once sharp and once out of focus, and a drop
    /// picks one or the other by depth.
    /// </para>
    /// <para>
    /// <b>It is a switch, not a blend.</b> No drop is ever a mixture of the two sheets — every drop is
    /// wholly sharp or wholly soft. Cross-fading them instead looks like the rain is dissolving at a
    /// fixed distance.
    /// </para>
    /// <para>
    /// <b>Zero disables it</b>, and zero is the default: every drop at every distance takes the sharp
    /// sheet. The defocused sheet ships and is addressed correctly; set this to the distance the lens is
    /// focused past and the near drops soften.
    /// </para>
    /// </remarks>
    public float FocusDistanceMetres { get; init; }

    /// <summary>The drop colour, as linear red, green and blue.</summary>
    /// <remarks>
    /// <para>
    /// A <b>reflectance</b> and not a brightness: it is lit, so it says what fraction of the light around
    /// it a drop sends to the eye and nothing about how much light there is.
    /// </para>
    /// <para>
    /// <b>This is what made the rain white.</b> Used as a brightness — a fixed level written into the
    /// vertex colour — it is a constant scene-referred value in a frame the auto-exposure then lifts to
    /// suit whatever the scene is. In a dark yard the exposure comes up, the scene stays dark and the
    /// rain goes to paper white, because the rain never got the memo that there is no light. Multiplied
    /// by the light there actually is, it tracks the scene: dark rain in a dark yard, and rain you can
    /// see in daylight.
    /// </para>
    /// <para>
    /// A near-neutral grey with the faintest cool lean, sending back a little under two fifths of the
    /// light: water is clear, and most of what a drop shows is the scene refracted through it.
    /// </para>
    /// </remarks>
    public (float R, float G, float B) Colour { get; init; } = (0.385f, 0.39f, 0.4f);

    /// <summary>Peak opacity of a drop at full <see cref="Intensity"/>.</summary>
    public float Alpha { get; init; } = 0.6f;

    /// <summary>Self-lit strength, added on top of the scene lighting.</summary>
    public float EmissiveIntensity { get; init; }

    /// <summary>How strongly drops brighten from the light coming through them.</summary>
    /// <remarks>
    /// <para>
    /// Water is not lit like a surface: a drop is a lens, and the light that makes rain legible is the
    /// light coming through it from behind. Without this a storm in front of a bright sky is a screen
    /// of grey lines that read as dirt on the lens.
    /// </para>
    /// <para>
    /// <b>A stand-in for the frame behind the drop.</b> Ideally a drop glows with whatever is behind it —
    /// sky, a window, a muzzle flash. Nothing in this engine's forward pass can read the frame it is
    /// drawing into, so what stands in for that read is the sun's own colour weighted by how close the
    /// drop sits to it in the view. Outdoors, in daylight, the brightest thing behind a drop is the sun,
    /// and the stand-in and the real thing agree; a drop backlit by a lamp at night is the case it gets
    /// wrong, and it is the case to revisit when scene colour becomes available to this pass.
    /// </para>
    /// <para>
    /// Applied only to drops beyond <see cref="FocusDistanceMetres"/>; the near, defocused drops take
    /// none of it.
    /// </para>
    /// </remarks>
    public float Backlighting { get; init; }

    /// <summary>The colour the backlight glow is tinted by, as linear red, green and blue.</summary>
    public (float R, float G, float B) BacklightTint { get; init; } = (1f, 1f, 1f);

    /// <summary>How much of the surrounding light a drop shows through itself.</summary>
    /// <remarks>
    /// <para>
    /// The ambient term: the light around the drop, desaturated and added to it, which is what stops rain
    /// reading as grey paint over a coloured scene. The same substitution as <see cref="Backlighting"/>
    /// applies — the level's ambient stands in for the frame behind the drop.
    /// </para>
    /// <para>
    /// <b>Mind the scale.</b> The level's ambient is a <em>chromaticity</em>: a unit-magnitude tint that
    /// says what colour the fill light is and nothing about how much of it there is. Added at full
    /// strength to a drop that was already a mid grey, every drop and every splash in the storm clamped to
    /// pure white — and with them went the glimmer, the backlighting and the gusts, because a channel
    /// pinned at 255 cannot show any of them. The rain came out as opaque white paint.
    /// </para>
    /// </remarks>
    public float AmbientIntensity { get; init; } = 1f;

    /// <summary>How much colour survives in that ambient term, from 0 (grey) to 1 (untouched).</summary>
    public float AmbientSaturation { get; init; } = 0.7f;


    /// <summary>Inside this distance from the eye a drop is not drawn, in metres.</summary>
    public float NearFadeMetres { get; init; } = 2f;

    /// <summary>Over how many metres a drop fades out as it approaches a solid surface.</summary>
    /// <remarks>
    /// Drops are flat sprites, so one intersecting a wall shows a hard diagonal cut where the quad
    /// crosses it. Fading against scene depth hides the seam. Set this too wide in a built-up level
    /// and everything within five metres of a surface — which outdoors is most of the ground — fades
    /// away, and the storm quietly disappears.
    /// </remarks>
    public float GeometryFadeMetres { get; init; } = 1f;

    /// <summary>How far above a drop cover is searched for, in metres; 0 disables the shelter test.</summary>
    /// <remarks>
    /// Kept modest on purpose. A generous search finds the surrounding hillside from inside a valley
    /// and shelters the whole level; the useful range is roof height, not sky height.
    /// </remarks>
    public float ShelterHeightMetres { get; init; } = 12f;

    /// <summary>Whether the ground answers the rain with splashes.</summary>
    public bool Splashes { get; init; } = true;

    /// <summary>Splashes raised per second at full <see cref="Intensity"/>.</summary>
    /// <remarks>
    /// <para>
    /// <b>Splashes are their own weather, not a consequence of the drops.</b> They were spawned where a
    /// falling drop crossed the ground, which is the physically obvious arrangement and is why none
    /// were ever visible: the drops fall through the whole column, so their landings were spread over
    /// the thousands of square metres the column covers, and the handful that landed near enough to be
    /// seen were each nine centimetres across. So splashes have their own spawn volume around the
    /// camera, because what the ground is doing is only worth drawing where the player can see it.
    /// </para>
    /// <para>
    /// So this is a rate over <see cref="SplashReachMetres"/>, not a fraction of anything. It buys
    /// density exactly where it reads and nothing at all where it would not.
    /// </para>
    /// </remarks>
    public float SplashesPerSecond { get; init; } = 1200f;

    /// <summary>How far from the eye splashes are raised, in metres.</summary>
    /// <remarks>
    /// Small, on purpose. A splash is a decimetre of water: past a dozen metres it is under a pixel, so
    /// spawning them further out spends the whole budget on things that cannot be seen and leaves the
    /// ground at the player's feet bare.
    /// </remarks>
    public float SplashReachMetres { get; init; } = 13f;

    /// <summary>What share of splashes are the small flat kind rather than a standing crown.</summary>
    /// <remarks>
    /// There are two kinds. The large one is the crown — a flipbook of water thrown upwards and falling
    /// back, drawn standing on the ground and turned to the eye. The small one is a soft blob lying flat
    /// where a drop hit. Both are needed: crowns alone read as a field of little white trees, and blobs
    /// alone read as the ground being speckled rather than struck.
    /// </remarks>
    public float SplashSmallFraction { get; init; } = 0.55f;

    /// <summary>Half the width of a splash's quad, in metres, before its own per-splash variation.</summary>
    /// <remarks>
    /// <para>
    /// <b>A few centimetres, not tens.</b> That is what a raindrop's crown actually measures; a fifth of a
    /// metre, which this was, is several times that.
    /// </para>
    /// <para>
    /// It is the same mistake three times over. The splashes were too big; they were too BRIGHT,
    /// because a sprite covering twenty-five times the screen area emits twenty-five times the light
    /// however dim its colour; and their animation looked coarse, because a hundred-and-twenty-eight
    /// texel cell magnified that far has no detail left to be smooth with.
    /// </para>
    /// <para>
    /// Each crown is drawn between about two thirds of this and all of it, which puts the visible crown at
    /// two and a half to four centimetres across. The small thrown droplets are cut from the same figure at
    /// a much smaller share.
    /// </para>
    /// </remarks>
    public float SplashRadiusMetres { get; init; } = 0.019f;

    /// <summary>How long a splash lives, in seconds.</summary>
    /// <remarks>
    /// This is also how long the crown's eight frames take, so it is the playback rate of the animation
    /// and not only a lifetime: too short and the flipbook strobes, too long and the water hangs in the
    /// air. Each crown takes between a little over half of this and all of it, which with the default
    /// plays the eight frames at roughly seventeen to thirty a second, about twenty on average.
    /// </remarks>
    public float SplashLifetimeSeconds { get; init; } = 0.48f;

    /// <summary>Peak opacity of a splash.</summary>
    /// <remarks>
    /// <para>
    /// Half, against the drops' six tenths — which sounds like the wrong way round until you count what
    /// is actually on the screen. A splash is metres from the eye where a drop is tens, its sheet is a
    /// full-range mask with bright cores where a streak is mostly empty, and every impact draws a crown
    /// AND two droplets over the same few pixels. Additively, that stacks. At parity with the drops the
    /// ground came out lit by its own splashes.
    /// </para>
    /// <para>
    /// Lit splashes, carrying no emissive at all, would leave a night yard's splashes as dark as
    /// everything else in it. Nothing in this forward pass lights a particle, so the brightness has to
    /// come off here instead.
    /// </para>
    /// </remarks>
    public float SplashAlpha { get; init; } = 0.4f;

    /// <summary>The splash colour before lighting, as linear red, green and blue.</summary>
    /// <remarks>Lit by the ambient term.</remarks>
    public (float R, float G, float B) SplashColour { get; init; } = (0.5f, 0.5f, 0.5f);

    /// <summary>How far a splash sits above the surface it forms on, in metres.</summary>
    /// <remarks>
    /// Not decoration: a splash lies flat, and drawn at exactly the ground height it is coplanar with
    /// the floor and loses the depth test to it. The quad then either z-fights or vanishes entirely,
    /// which looks like the splashes were never spawned. A couple of centimetres is enough to clear it
    /// and far too little to read as floating.
    /// </remarks>
    public float SplashLiftMetres { get; init; } = 0.03f;

    /// <summary>Whether the distant drifting sheets are drawn.</summary>
    /// <remarks>
    /// <para>
    /// Off by default. A full curtain would be a layered scrolling sheet, which has not been built, and
    /// what stands in for it here is a single soft cloud, which at any opacity strong enough to notice
    /// reads as a grey cloud parked in the middle distance rather than as rain too far off to resolve.
    /// </para>
    /// <para>
    /// What they are for stands: individual drops stop resolving after twenty or thirty metres, so a
    /// storm made only of drops has a visible edge past which the air is clear. Turning these on fills
    /// that distance, at the cost of the above.
    /// </para>
    /// </remarks>
    public bool Curtains { get; init; }

    /// <summary>How many sheets are alive at once.</summary>
    public int CurtainCount { get; init; } = 24;

    /// <summary>The size of one sheet, in metres.</summary>
    public float CurtainSizeMetres { get; init; } = 55f;

    /// <summary>Peak opacity of a sheet.</summary>
    /// <remarks>
    /// <para>
    /// Meant to be subtle, but 0.05 is below the point where an eight-bit vertex alpha survives at all
    /// once the distance fade has taken a bite out of it -- which is the difference between "restrained"
    /// and "not drawing". The band between those two is narrow, and 0.2 was over the top of it: with a
    /// real cloud mask rather than the generated one, a fifth of an opacity reads as a grey cloud
    /// hanging in the sky rather than as rain too far away to resolve.
    /// </para>
    /// <para>
    /// The sheets are meant to be noticed as haze and never as objects. If one can be pointed at, it is
    /// too strong however good the mask on it is.
    /// </para>
    /// </remarks>
    public float CurtainAlpha { get; init; } = 0.09f;

    /// <summary>How fast the sheets drift past, in metres per second.</summary>
    public float CurtainMetresPerSecond { get; init; } = 6f;

    /// <summary>How far out the sheets are scattered, as a multiple of the drop column's radius.</summary>
    /// <remarks>
    /// One number for both the scattering and the distance fade. They were separate constants, and the
    /// fade's was the smaller of the two, so every sheet that landed inside the larger radius faded to
    /// nothing -- most of them.
    /// </remarks>
    public float CurtainReachScale { get; init; } = 2.5f;

    /// <summary>Returns the wind as a world-space velocity in metres per second.</summary>
    /// <remarks>
    /// Z is up in this world, so the compass angle turns in the XY plane and the updraft is the Z
    /// component. Computed here rather than at each call site so the convention lives in one place.
    /// </remarks>
    public (float X, float Y, float Z) WindVelocity() =>
        WindVelocity(WindMetresPerSecond, WindDirectionDegrees);

    /// <summary>Returns a given wind as a world-space velocity in metres per second.</summary>
    /// <param name="speed">The lateral speed, in metres per second.</param>
    /// <param name="degrees">The compass direction it blows towards.</param>
    /// <returns>The wind velocity.</returns>
    /// <remarks>
    /// Taken as arguments because the wind the storm is blowing at this instant is not a property of the
    /// settings — it wanders about <see cref="WindMetresPerSecond"/> by up to
    /// <see cref="WindGustMetresPerSecond"/> either way, and only the renderer knows where it has got to.
    /// </remarks>
    public (float X, float Y, float Z) WindVelocity(float speed, float degrees)
    {
        float radians = degrees * (MathF.PI / 180f);
        return (MathF.Cos(radians) * speed, MathF.Sin(radians) * speed, WindUpdraftMetresPerSecond);
    }

    /// <summary>The number of drops alive in the steady state, which is what the buffers are sized for.</summary>
    /// <remarks>
    /// Spawn rate multiplied by lifetime. Worth having as one expression because it is the number that
    /// decides both the vertex budget and whether a tuning change is affordable: doubling the lifetime
    /// costs exactly as much as doubling the rate.
    /// </remarks>
    public int SteadyStateDrops() =>
        (int)MathF.Ceiling(MathF.Max(0f, DropsPerSecond * Intensity) * MathF.Max(0f, LifetimeSeconds));
}
