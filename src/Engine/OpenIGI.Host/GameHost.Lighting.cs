using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Core.Configuration;
using OpenIGI.Game.Player;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Lighting;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The sky as a source of indirect light: the colour a GI ray that escapes the geometry returns. The
    // level's Dirlight ambient is a dim fill (~0.27, its "Back Color"), authored to sit on top of the flat
    // ambient rather than to be the sky's whole radiance; r_sky_light scales it up to a daytime skylight so
    // the GI can carry the outdoor and window fill the flat ambient used to stand in for. Only surfaces
    // that actually see the sky read it, so a sealed interior is unaffected.
    private Vector3f SkyFillColour() =>
        ((_effectiveSun ?? _sun)?.Ambient ?? new Vector3f(0.27f, 0.29f, 0.33f)) * RenderCVars.SkyLight.Value;

    /// <summary>The sky's light averaged over every direction, as the lighting pass itself has it.</summary>
    /// <returns>
    /// The radiance a white diffuse surface would send back if the sky lit it from all round, with
    /// <c>r_sky_light</c> already in it; the flat fill where the renderer has no sky to report.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For what hangs in the air rather than sits on a surface -- a falling drop is lit from every side.
    /// Taken from the same harmonics the deferred lighting and the fog read, averaged over the sphere: the
    /// linear terms' constants, and a third of the <c>z</c>-squared term, the only quadratic one whose
    /// average is not nought.
    /// </para>
    /// <para>
    /// Not the level's flat ambient. That is what the renderer fell back on before it had a sky, and at
    /// night it is several times the light the scene is actually lit by -- which is what drew every rain
    /// streak brighter than the ground it fell past, and a rainy night as a wall of rain.
    /// </para>
    /// </remarks>
    private Vector3f AmbientSkyLight()
    {
        Span<float> harmonics = stackalloc float[28];

        if (!_device.TryGetSkyIrradiance(harmonics))
        {
            return SkyFillColour();
        }

        return new Vector3f(
            MathF.Max(0f, harmonics[3] + (harmonics[14] / 3f)),
            MathF.Max(0f, harmonics[7] + (harmonics[18] / 3f)),
            MathF.Max(0f, harmonics[11] + (harmonics[22] / 3f)));
    }

    // The same, for a named sun rather than whatever the last frame used: the surface cache's convergence
    // runs before a level's first frame has set one.
    private static Vector3f SkyFillColour(DirectionalLightSettings sun) => SkyLight.Fill(sun);

    /// <summary>Warms a level's cool zenith ambient towards the horizon as the sun sinks.</summary>
    /// <remarks>
    /// The sky an interior sees at a low sun is the warm horizon, not the cool zenith: the atmosphere
    /// reddens as the sun sinks and a real skylight carries that warmth into the room. The level ships a
    /// single cool ambient triple with no notion of the sun's height, so read literally it leaves a dusk-lit
    /// room as flatly blue as a noon one. This blends it towards a golden horizon tint by how low the sun
    /// sits -- keeping its own brightness so only the hue shifts -- and fades the warmth out above a moderate
    /// elevation, where the ambient is left alone. Applied at the source so both the deferred ambient and the
    /// GI sky fill inherit it.
    /// </remarks>
    /// <summary>Re-points a light's colour to a target chromaticity while keeping its own luminance.</summary>
    /// <remarks>
    /// The level's authored light colours carry the game's stylised warmth; this swaps the HUE for a
    /// physical daylight one -- a near-white sun, a cool-blue sky -- without touching how BRIGHT the level
    /// asked the light to be. The target is scaled by the ratio of the source's luminance to its own, so a
    /// dim ambient stays dim and a strong sun stays strong; only the balance between the channels changes.
    /// </remarks>
    private static Vector3f DaylightColour(Vector3f colour, Vector3f chroma)
    {
        float luma = (colour.X * 0.2126f) + (colour.Y * 0.7152f) + (colour.Z * 0.0722f);
        float chromaLuma = (chroma.X * 0.2126f) + (chroma.Y * 0.7152f) + (chroma.Z * 0.0722f);
        return chroma * (luma / MathF.Max(chromaLuma, 1e-4f));
    }

    private static Vector3f WarmedAmbient(Vector3f ambient, float sinElevation)
    {
        // Left as the level authored it. An earlier version blended the ambient hard towards a golden
        // horizon by how low the sun sat, on the theory that a low sun is a warm sun -- but mission 1 is a
        // COLD clear morning, its sun as low as a dusk one and its skylight cool blue off the snow, and the
        // warming dumped orange over a room that should read a clean neutral. A low sun is not a warm
        // one; only a SETTING sun is, and that is a per-mission call rather than a function of height. The
        // authored cool ambient is what balances the slightly warm sun into neutral daylight, so it stands.
        _ = sinElevation;
        return ambient;
    }

    /// <summary>A per-channel tint for the sky dome that reddens towards the horizon as the sun sinks.</summary>
    /// <param name="sinElevation">The sine of the sun's elevation; 1 is overhead, negative is below the horizon.</param>
    /// <remarks>
    /// The authored sky is a fixed blue with no sense of the sun's height. A low sun scatters the blue out of
    /// the light that reaches the ground and leaves the horizon warm, so this multiplies the dome towards a
    /// warm tint by how low the sun sits -- one at a high sun, so a noon sky is the authored blue untouched.
    /// It is the sky counterpart to <see cref="WarmedAmbient"/>: together they warm both what a window shows
    /// and what the fill carries in.
    /// </remarks>
    private static Vector3f SkyWarmthTint(float sinElevation)
    {
        // Neutral. The sky the window shows and the fill it carries stay the authored cool blue rather than
        // being reddened towards the horizon by the sun's height. See WarmedAmbient: a cold morning has a
        // low sun over a cool sky, and tinting the sky warm to match a low sun turned the snow outside the
        // window and the light off it orange, where it should read a clean cool daylight.
        _ = sinElevation;
        return Vector3f.One;
    }

    // Used only for a level that declares no Dirlight at all. The angles are mission 1's, which the probe
    // showed put the sun 60 degrees up at noon, with the warm sun and cool ambient every retail level pairs.
    private static readonly DirectionalLightSettings FallbackSun = new(
        DirectionalLightSettings.DirectionFrom(0.5236f, -4.92183f),
        new Vector3f(1.0f, 0.96f, 0.88f),
        new Vector3f(0.27f, 0.29f, 0.33f),
        Beta: 0.5236f,
        Gamma: -4.92183f,
        TimeOfDay: 12f,
        Intensity: 18f,
        AffectsTerrain: true,
        AffectsObjects: true);

    /// <summary>The sun -- or the moon -- the level is lit by at the cycle's current hour.</summary>
    /// <remarks>
    /// <para>
    /// One answer for everything that lights the level, because two answers is how the level ended up lit
    /// twice. The frame used to derive its sun inline while the surface cache converged under
    /// <c>_effectiveSun ?? _sun</c> -- and during a load the first is not set yet, so the second, the
    /// level's raw authored Dirlight, lit the whole cache. Every Project I.G.I. mission authors that as a
    /// NOON sun, so a night mission's every distant surface was converged in daylight and stayed that way:
    /// the steady refresh only reaches cards near the camera, so a tunnel across the valley glowed until
    /// the player walked up to it.
    /// </para>
    /// <para>
    /// A pinned sun (<c>-sundir</c>) keeps the level's own colour with the pinned direction.
    /// Otherwise the level's bound sun is swept to the hour; a title that leaves its fill to the renderer
    /// has its ambient re-pointed to a daylight chroma by day; and under cloud the moon loses most of its
    /// directional light to the deck.
    /// </para>
    /// </remarks>
    private DirectionalLightSettings CycleSun()
    {
        DirectionalLightSettings light;
        bool pinned = _options.SunDirection is not null;
        if (_options.SunDirection is { } fixedSun)
        {
            // A fixed sun for matching an external render: the level's own colour and ambient, but a
            // direction pinned to the one given, bypassing the day/night sweep -- which leaves the
            // cycle's nightfall at nought, so the re-tint below treats it as full day.
            Vector3f dir = new(fixedSun.X, fixedSun.Y, fixedSun.Z);
            float len = dir.Length;
            dir = len > 1e-5f ? dir / len : Vector3f.UnitZ;
            light = (_sun ?? FallbackSun) with { Direction = dir };
        }
        else
        {
            // The BOUND sun, not the raw authored one: a title whose levels do not author a noon sun has
            // already had that answered for it. See DayNightBinding.
            light = _dayNight.Apply(_cycleSun ?? _sun ?? FallbackSun);
        }

        // The sun keeps the level's own authored chromaticity, because rewriting it to a neutral key is
        // the kind of grade that makes two renders of the same level impossible to compare channel for
        // channel. Only the ambient is re-pointed, since the level's flat cool triple stands in for a sky
        // the dynamic model now largely derives itself -- and only for a title that leaves its fill to the
        // renderer: one that authored an ambient per level means it (the sequel's night fills with a cold
        // blue). Only by DAY, crossed out as the night comes in: the fill the cycle crosses to at night is
        // its own moonlit blue, and re-pointed as well a clear night was filled with a warm amber.
        if (_deferredLighting is null)
        {
            Vector3f warmed = DaylightColour(light.Ambient, new Vector3f(1.18f, 0.88f, 0.58f));
            float day = 1f - _dayNight.Nightfall;
            light = light with
            {
                Ambient = light.Ambient + ((warmed - light.Ambient) * day),
            };
        }

        // Under cloud the moon is no longer a lamp in the sky with hard shadows: most of its light reaches
        // the ground scattered through the deck, which the sky's overcast carries as fill. What stays
        // directional is the little that comes through thin patches of cloud.
        if (!pinned && _dayNight.Moonrise > 0f)
        {
            light = light with
            {
                Colour = light.Colour * MoonVeil(),
            };
        }

        return light;
    }

    /// <summary>How much of the moon's light is left once the weather and <c>r_moonlight</c> have had it.</summary>
    private float MoonVeil()
    {
        float cover = WeatherCloudCover() * _dayNight.Nightfall;
        return (1f - (0.75f * cover)) * RenderCVars.Moonlight.Value;
    }

    // How bright the brightest star is drawn, in the lit scene's own units. Relative to the night sky it is
    // drawn over, which sits around the airglow's few thousandths: some thirty times that, so the few
    // brightest stars stand out as points and the rest sit at the edge of seeing, as under a dark sky.
    private const float StarRadiance = 0.12f;

    // How bright the moon's disc is drawn, likewise, before r_moon_brightness. The real ratio to the sky is
    // several thousand and would burn the disc to white and flood the bloom across half the screen. This is
    // bright enough to read as the brightest thing in a night sky, and no brighter: at 2.5 the bloom spread
    // it into a glow over a third of the sky.
    private const float MoonDiscRadiance = 1.0f;

    /// <summary>How overcast the sky is, as far as the night is concerned: a raining or snowing level is.</summary>
    /// <remarks>
    /// The level's own weather decides it, through the same flag the rain's draw and sound read, not whether
    /// the storm is drawing this frame. That one goes off whenever the camera steps under a roof, and the
    /// sky outside does not clear because the player went indoors.
    /// </remarks>
    private float WeatherCloudCover()
    {
        bool falling = (_rain?.IsWeatherEnabled ?? false) || (_snow?.IsWeatherEnabled ?? false);
        return falling ? Math.Clamp(0.8f + (0.2f * RenderCVars.StormIntensity.Value), 0f, 1f) : 0f;
    }

    /// <summary>What the night sky holds this frame beyond the atmosphere: glow, cover, stars and moon.</summary>
    /// <remarks>
    /// Everything is weighted by how far into night the cycle is, so a day, or a capture with a
    /// pinned sun, carries none of it and draws exactly the sky it always did.
    /// </remarks>
    private NightSky FrameNightSky()
    {
        float night = _dayNight.Nightfall;
        if (night <= 0f || _options.SunDirection is not null)
        {
            return NightSky.None;
        }

        float cover = WeatherCloudCover() * night;
        bool stars = RenderCVars.Stars.Value;

        Vector3f moon = _dayNight.MoonDirection;
        float moonUp = Math.Clamp((moon.Z + 0.02f) / 0.06f, 0f, 1f);
        moonUp = moonUp * moonUp * (3f - (2f * moonUp));

        return new NightSky(
            // Not weighted by the night: the glow is emission, there all day, and only outshone while the
            // sun lights the sky. Faded in with the night, it was missing through the whole of dusk, which
            // left the sky between the sun's last light and full night darker than the night itself.
            Airglow: RenderCVars.NightAirglow.Value,
            CloudCover: cover,
            StarRadiance: stars ? StarRadiance * _dayNight.StarVisibility : 0f,
            MoonDirection: moon,
            MoonRadiance: stars ? MoonDiscRadiance * RenderCVars.MoonBrightness.Value * moonUp * night : 0f,
            Seconds: _loop.TimeSource.Milliseconds / 1000f);
    }

    // The mission's authored grade, or the one r_lut_table names instead. Loaded per level rather than
    // per frame -- it is a volume texture upload, while how much of it to apply is a number.
    // A mission with no table of its own is not a fault: it renders ungraded, which is what every
    // mission did before this existed.
    private void ApplyColourLookup(int mission)
    {
        string named = RenderCVars.ColourLookupTable.Value.Trim();
        string name = named.Length > 0
            ? named
            : FormattableString.Invariant($"mission{mission}");

        // Anchored to the assets directory and stripped of any path, so r_lut_table cannot be pointed
        // at an arbitrary file on the machine from a settings file or a +statement.
        string file = Path.Combine(
            AppContext.BaseDirectory, "Assets", "luts", Path.GetFileName(name) + ".cube");

        ColourLookupTable? table = ColourLookupTable.Load(file, _logger);

        if (table is null && named.Length > 0)
        {
            _logger.Warning("Grade", $"No colour table called '{named}' in Assets/luts; the frame is ungraded.");
        }

        _device.SetColourLookup(table);
    }

    // Holds the load open until the probe field lands, keeping the window alive throughout.
    //
    // The pump is the whole point. This runs on the thread that owns the GL context, so a plain wait
    // stops the process answering the operating system and the watchdog resets the driver -- which is
    // the fault that put this bake on a worker to begin with, and it exits 0xCFFFFFFF rather than
    // reporting anything. Draining the queue and presenting a cleared frame costs almost nothing and
    // keeps the process demonstrably alive.
    private IrradianceVolume? WaitForLighting(IrradianceProbeBake bake)
    {
        int startedAt = _loop.TimeSource.Milliseconds;

        while (true)
        {
            // Taken here rather than in the loop's condition, because TryCollect answers true exactly
            // once by contract -- asking again after the loop to find out what it handed over would
            // get nothing, and the level would load unlit having just waited for its lighting.
            if (bake.TryCollect(out IrradianceVolume? ready))
            {
                _logger.Information(
                    "Light", $"Waited {_loop.TimeSource.Milliseconds - startedAt} ms for the probe field.");

                return ready;
            }

            if (_window.IsClosing)
            {
                bake.Cancel();
                return null;
            }

            _window.PumpEvents();

            // Cleared to the level's own fog colour rather than to black, so the wait reads as the
            // level arriving rather than as the picture having gone.
            _device.Clear(_scheduler.ClearColour);
            _device.SwapBuffers();

            // The bake is bounded work on a thread pool rather than a poll of something that may never
            // arrive -- but a level whose trace somehow never finishes must not become a game that
            // never starts. Falling through here leaves the blend to cover it.
            if (_loop.TimeSource.Milliseconds - startedAt > LightingWaitMilliseconds)
            {
                _logger.Warning(
                    "Light",
                    $"The probe field did not arrive within {LightingWaitMilliseconds / 1000} s; "
                    + "entering the level unlit and blending it in when it lands.");

                return null;
            }
        }
    }

    /// <summary>How much of the night-vision shift a night this deep takes, nought to one.</summary>
    /// <param name="nightfall">How far into night the sky is (<see cref="RenderCVars.Nightfall"/>).</param>
    /// <returns>Nought through dusk, rising once the sun is some six degrees under, one at full night.</returns>
    private static float NightVisionWeight(float nightfall)
    {
        float t = Math.Clamp((nightfall - 0.45f) / 0.55f, 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>Registers the console's handle on the time of day.</summary>
    /// <param name="console">The console to register with.</param>
    private void RegisterTimeOfDayCommands(ConsoleSystem console)
    {
        // The clock, set the way the [ and ] scrub sets it: mid-mission, with nothing else reset. What a
        // capture needs to reproduce a scrub into the night without a key held down.
        console.Commands.Add(
            "settime",
            "Moves the time of day to an hour, 0 to 24, as the [ and ] scrub does; a second number sweeps there over that many seconds. Not in the original.",
            context =>
            {
                if (context.Count < 1
                    || !float.TryParse(context[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hours))
                {
                    context.Print($"The time is {_dayNight.TimeHours:0.00}.");
                    return;
                }

                float seconds = context.Count > 1
                    && float.TryParse(context[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float over)
                    ? over
                    : 0f;
                _dayNight.SweepToHour(hours, seconds);
                context.Print(seconds > 0f ? $"Sweeping to {hours:0.00} over {seconds:0.#} s." : $"Time set to {_dayNight.TimeHours:0.00}.");
            });
    }

    /// <summary>Reads the Flashlight action and flips the switch, with its click.</summary>
    /// <param name="menuMode">Whether a menu holds the input.</param>
    /// <param name="consoleOpen">Whether the console does.</param>
    /// <remarks>
    /// Read per frame beside the map computer's key rather than on the thirty-hertz tick, so a quick tap is
    /// never missed. Only while the light could be seen -- in the player's own view, alive, with nothing else
    /// holding the picture -- so the key cannot switch it on unseen behind a cutscene.
    /// </remarks>
    private void UpdateFlashlightSwitch(bool menuMode, bool consoleOpen)
    {
        if (!_flashlight.CanSwitch(menuMode, consoleOpen, _playerDead) || _player is not { } player)
        {
            return;
        }

        player.InputProfile = _configuration.Active;
        if (!player.WasActionPressed(_window.Input, PlayerAction.Flashlight))
        {
            return;
        }

        bool on = _flashlight.Toggle();
        _audioOut.Sounds?.PlayExternal(on ? FlashlightClick.OnSoundName : FlashlightClick.OffSoundName, gain: 0.8f, priority: 180);
    }
}
