using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

internal sealed partial class DesktopGame
{
    // The sky as a source of indirect light: the colour a GI ray that escapes the geometry returns. The
    // level's Dirlight ambient is a dim fill (~0.27, its "Back Color"), authored to sit on top of the flat
    // ambient rather than to be the sky's whole radiance; r_sky_light scales it up to a daytime skylight so
    // the GI can carry the outdoor and window fill the flat ambient used to stand in for. Only surfaces
    // that actually see the sky read it, so a sealed interior is unaffected.
    private Vector3f SkyFillColour() =>
        ((_effectiveSun ?? _sun)?.Ambient ?? new Vector3f(0.27f, 0.29f, 0.33f)) * RenderCVars.SkyLight.Value;

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
    private static Vector3f WarmedAmbient(Vector3f ambient, float sinElevation)
    {
        float highness = Math.Clamp((sinElevation - 0.05f) / (1.0f - 0.05f), 0f, 1f);
        float warmth = 1f - (highness * highness * (3f - (2f * highness)));

        // A low sun's skylight is strongly warm -- the golden fill the reference bathes an interior in. The
        // authored ambient is a cool blue-grey; blended nearly all the way to a warm horizon tint at a low
        // sun (and left alone at noon), it carries that warmth into the room rather than reading as grey.
        float luminance = (ambient.X + ambient.Y + ambient.Z) / 3f;
        Vector3f horizon = new Vector3f(1.35f, 0.82f, 0.5f) * luminance;
        return ambient + ((horizon - ambient) * warmth);
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
        float highness = Math.Clamp((sinElevation - 0.05f) / (0.90f - 0.05f), 0f, 1f);
        float warmth = 1f - (highness * highness * (3f - (2f * highness)));

        Vector3f warm = new(1.5f, 0.95f, 0.5f);
        return Vector3f.One + ((warm - Vector3f.One) * warmth);
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
}
