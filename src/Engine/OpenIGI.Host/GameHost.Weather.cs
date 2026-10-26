using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    private RainRenderer? _rain;
    private StormRenderer? _storm;

    // The least light a rain drop is lit by, as the displayed picture sees it. Rain in an even light is as
    // bright as what is behind it and all but disappears, and a night lit only by its sky is that light;
    // the eye adapted to it still sees the rain as a veil. The drop's own grey, its opacity and the thin,
    // mostly clear streak it is printed with all come on top, so it takes this much for the rain to read:
    // chosen by eye on mission 3's night, where 2.5 barely showed, 5 reads as rain with the dome and the
    // hills clear through it, and the level's daytime fill (about 10) was a wall. See
    // PrecipitationGeometry.BuildDrops.
    private const float RainVeilDisplay = 5f;
    private SnowRenderer? _snow;

    // The level's weather as its title's original runs and draws it, which the original's look draws in place of the two above.
    private IWeatherPresenter? _retailWeather;

    // Where the level's roofs are, seen from above. One thing decides whether the sky can reach a point
    // -- the fog, the falling rain, its splashes and the standing water all ask this -- and it is built
    // once when the mission loads, out of the geometry itself rather than out of which tasks happen to
    // be named after buildings.
    private ShelterHeightField _shelterField = ShelterHeightField.Empty;
    private TextureHandle _shelterMap = TextureHandle.None;
    private Vector3d _stormEyeMetres;

    private void ApplyWeatherSettings()
    {
        // The campaign screen's choice overrides both the level's own data and the player's
        // r_weather_kind, and it has to FORCE: eleven of the fourteen retail missions author no weather
        // at all, so a choice that only asked politely would do nothing in most of the campaign and read
        // as a broken setting. Default forces nothing and leaves every answer where it was.
        WeatherKind weatherKind = _conditions.WeatherStyle(WeatherCVars.WeatherStyle.Value);
        bool forceWeather = WeatherCVars.WeatherForce.Value || _conditions.ForcesWeather;

        _rain?.ApplySettings(
            WeatherCVars.WeatherEnabled.Value,
            weatherKind,
            WeatherCVars.WeatherSpeed.Value,
            forceWeather);
        _snow?.ApplySettings(
            WeatherCVars.WeatherEnabled.Value,
            weatherKind,
            WeatherCVars.WeatherSpeed.Value,
            forceWeather);

        if (_storm is { } storm)
        {
            // Resolved after the rain renderer, not before it, so this reads the answer the console just
            // produced rather than the one from the last time the level was loaded. The level decides
            // whether it rains at all (unless r_weather_force overrides it); r_storm only decides which
            // renderer draws it.
            bool rains = _rain?.IsWeatherEnabled ?? false;
            storm.Enabled = rains && WeatherCVars.Storm.Value;
            storm.Configure(storm.Settings with
            {
                Intensity = WeatherCVars.StormIntensity.Value,
                FallSpeedScale = WeatherCVars.WeatherSpeed.Value,
            });

            // Reported every time rather than only on a change: the case worth seeing is the one where
            // nothing changed and the storm is still not drawing, and a log that only fires on a
            // transition is silent for exactly that.
            _logger.Information(
                "Storm",
                FormattableString.Invariant(
                    $"enabled={storm.Enabled} active={storm.IsActive} rains={rains} r_storm={WeatherCVars.Storm.Value} r_weather_enabled={WeatherCVars.WeatherEnabled.Value} kind={weatherKind} force={forceWeather} campaign={_conditions.Weather}/{_conditions.TimeOfDay} intensity={WeatherCVars.StormIntensity.Value}"));
        }
    }

    // Whether the camera sits inside one of the current level's building shells, and so out of the rain.
    // The volumes are the per-mission shells precomputed by RainRenderer.BuildBuildingVolumes.
    /// <summary>Reports whether the camera has a roof over it, so the frame is drawn as an interior.</summary>
    /// <param name="position">Where the camera stands, in world metres.</param>
    /// <remarks>
    /// This used to be "inside the bounding box of a task named <c>Building_</c>", and that box is not
    /// the building: one standing at an angle to the world axes fills barely a third of it, so a quarter
    /// of the ground the test called indoors on mission one has open sky over it. That is what stood the
    /// fog down and stopped the rain over the middle of an open apron. What decides it now is whether
    /// anything solid is actually overhead — see <see cref="ShelterHeightField.IsUnderCover"/>, which
    /// takes a small vote around the eye so a pipe passing over does not read as a ceiling.
    /// </remarks>
    private bool IsCameraInsideBuilding(Vector3d position) => _shelterField.IsUnderCover(position);

    /// <summary>Builds the level's top-down map of what the solid world stands over, and binds it.</summary>
    /// <param name="scene">The mission's placed objects.</param>
    /// <param name="loaded">The mission whose models bend its spline segments, as the collision index bends them.</param>
    /// <remarks>
    /// <para>
    /// The one answer to "can the sky reach this point", for the fog, the falling rain, its splashes and
    /// the standing water alike. It is a raster of the collision geometry seen from above rather than a
    /// list of building bounding boxes, because the boxes were wrong both ways round: on mission one a
    /// quarter of the ground inside them has open sky over it — a building at an angle to the world axes
    /// fills barely a third of its own box — while two fifths of the ground genuinely under a roof lay
    /// outside every box, its shelter never having been labelled a building.
    /// </para>
    /// <para>
    /// The CPU keeps the raster for its own questions and the GPU is handed a packed copy, so the puddle
    /// a pixel draws and the drop the storm culls are decided by the same numbers.
    /// </para>
    /// </remarks>
    private void BuildShelterField(WorldScene scene, LoadedMission loaded)
    {
        ReleaseShelterField();

        long started = _loop.TimeSource.Milliseconds;
        _shelterField = ShelterHeightField.Build(
            scene.Placements,
            collision: _titleRuntime.Formats.Collision,
            models: name => loaded.TryGetModel(name, out MefModel? model) ? model : null);
        if (_shelterField.IsEmpty)
        {
            _logger.Information("Shelter", "the level places nothing solid; nothing is sheltered");
            return;
        }

        Bgra32[] texels = _shelterField.Encode(out float scale);
        _shelterMap = _device.CreateTexture(
            new TextureDescription(
                _shelterField.Width,
                _shelterField.Depth,
                MipLevels: 1,
                TextureFilter.Point,
                TextureAddressMode.Clamp) { HoldsData = true },
            texels);

        _giRoofTransform = new ShelterMapTransform(
                (float)_shelterField.OriginX,
                (float)_shelterField.OriginY,
                (float)_shelterField.CellMetres,
                _shelterField.Width,
                _shelterField.Depth,
                _shelterField.LowestZ,
                scale,
                (float)ShelterHeightField.SurfaceHeadroomMetres,
                (float)ShelterHeightField.MaxOverheadMetres);
        _terrainHeightsGivenTo = null;
        _device.SetShelterMap(
            _shelterMap,
            new ShelterMapTransform(
                (float)_shelterField.OriginX,
                (float)_shelterField.OriginY,
                (float)_shelterField.CellMetres,
                _shelterField.Width,
                _shelterField.Depth,
                _shelterField.LowestZ,
                scale,
                (float)ShelterHeightField.SurfaceHeadroomMetres,
                (float)ShelterHeightField.MaxOverheadMetres));

        _logger.Information(
            "Shelter",
            FormattableString.Invariant(
                $"{_shelterField.Width}x{_shelterField.Depth} at {_shelterField.CellMetres:0.00} m, z {_shelterField.LowestZ:0.0}..{_shelterField.HighestZ:0.0}, built in {_loop.TimeSource.Milliseconds - started} ms"));
    }

    /// <summary>Drops the level's shelter map, and tells the device there is none.</summary>
    private void ReleaseShelterField()
    {
        _giRoofTransform = ShelterMapTransform.None;
        ReleaseTerrainRaster();
        _shelterField = ShelterHeightField.Empty;
        _device.SetShelterMap(TextureHandle.None, ShelterMapTransform.None);
        if (_shelterMap.IsValid)
        {
            _device.DestroyTexture(_shelterMap);
            _shelterMap = TextureHandle.None;
        }
    }
}
