using System.Text.Json;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;

namespace OpenIGI.Host;

/// <summary>
/// <c>-dumplighting</c>: the light a frame was lit with, written out so an external render can be set
/// up from the same numbers.
/// </summary>
/// <remarks>
/// <para>
/// A second derivation of the sun, the moon or a lamp's strength on the other side would drift the
/// moment either side changed; reading what this frame actually handed the renderer cannot. Everything
/// is in world space and metres, the frame the level is authored in, so the reader needs only a level
/// export's pivot to place it.
/// </para>
/// <para>
/// Every fixture the level has is written, not only the ones this frame selected: an external render
/// lights with all of them, and the selection is a budget of this renderer's rather than a fact about
/// the level. Each carries the strength and reach the frame loop would give it, from the same
/// arithmetic.
/// </para>
/// </remarks>
public sealed partial class GameHost
{
    // The lighting the last frame handed the renderer, and its sun in world space (the handed one is in
    // view space). Recorded unconditionally: two struct copies a frame cost nothing.
    private SceneLighting _dumpLighting;
    private Vector3f _dumpWorldSun;

    /// <summary>Writes the last frame's lighting to <paramref name="path"/> as JSON.</summary>
    /// <param name="path">The file to write.</param>
    private void WriteLightingDump(string path)
    {
        float localLightScale = RenderCVars.LocalLightScale.Value;
        float localLightRange = RenderCVars.LocalLightRange.Value;

        using FileStream stream = File.Create(path);
        using Utf8JsonWriter json = new(stream, new JsonWriterOptions { Indented = true });

        json.WriteStartObject();
        json.WriteString("model", _dumpLighting.Model.ToString());
        WriteVector(json, "sunDirectionWorld", _dumpWorldSun);
        WriteVector(json, "sunColour", _dumpLighting.SunColour);
        json.WriteNumber("lightScale", _dumpLighting.Scale);
        WriteVector(json, "ambient", _dumpLighting.AmbientColour);
        json.WriteNumber("skyLightIntensity", _dumpLighting.SkyLightIntensity);
        WriteVector(json, "atmosphereSunColour", _dumpLighting.AtmosphereSunColour ?? _dumpLighting.SunColour);

        NightSky night = _dumpLighting.Night;
        json.WriteStartObject("night");
        json.WriteNumber("airglow", night.Airglow);
        json.WriteNumber("cloudCover", night.CloudCover);
        json.WriteNumber("starRadiance", night.StarRadiance);
        WriteVector(json, "moonDirection", night.MoonDirection);
        json.WriteNumber("moonRadiance", night.MoonRadiance);
        json.WriteEndObject();

        json.WriteNumber("timeOfDay", RenderCVars.TimeOfDay.Value);
        json.WriteBoolean("autoExposure", RenderCVars.AutoExposure.Value);
        json.WriteNumber("exposure", RenderCVars.Exposure.Value);
        json.WriteNumber("localLightScale", localLightScale);
        json.WriteNumber("localLightRange", localLightRange);
        json.WriteNumber("localLightSource", RenderCVars.LocalLightSource.Value);

        json.WriteStartArray("lamps");
        for (int i = 0; i < _gi.LightFixtures.Count; i++)
        {
            LightFixture fixture = _gi.LightFixtures[i];
            json.WriteStartObject();
            json.WriteNumber("index", i);
            json.WriteString("texture", fixture.Texture);
            json.WriteStartArray("position");
            json.WriteNumberValue(fixture.Position.X);
            json.WriteNumberValue(fixture.Position.Y);
            json.WriteNumberValue(fixture.Position.Z);
            json.WriteEndArray();
            WriteVector(json, "direction", fixture.IsDirectional ? fixture.Direction : default);
            WriteVector(json, "colour", fixture.Colour);

            // Exactly what the frame loop hands the renderer for a lamp nobody has switched: see the
            // local-light block in the frame loop, which this mirrors.
            json.WriteNumber("intensity", MathF.Min(fixture.Intensity, 1.5f) * localLightScale);
            json.WriteNumber("reach", fixture.Radius * localLightRange);
            json.WriteNumber("extent", fixture.Extent);
            json.WriteNumber("area", fixture.Area);

            // Where the lamp stands with the daylight: whether its photocell decides it, whether it has a roof
            // over it and whether it counts as indoors (which keeps it lit by day), and how bright the switchboard has it now.
            json.WriteBoolean("followsDaylight", _lamps.Daylight.Photocells?.Follows(i) ?? false);
            json.WriteBoolean("underRoof", LampDaylightControl.IsUnderRoof(fixture, _shelterField));
            json.WriteBoolean("indoors", LampDaylightControl.IsIndoors(fixture, _shelterField, _mission.EffectWalls));
            json.WriteNumber("level", _gi.LampSwitchboard?.LevelOf(i) ?? 1f);
            json.WriteBoolean("supplied", _gi.LampSwitchboard?.IsSupplied(i) ?? true);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();

        _logger.Information("Desktop", $"Wrote the frame's lighting ({_gi.LightFixtures.Count} lamp(s)) to {path}.");
    }

    private static void WriteVector(Utf8JsonWriter json, string name, Vector3f value)
    {
        json.WriteStartArray(name);
        json.WriteNumberValue(value.X);
        json.WriteNumberValue(value.Y);
        json.WriteNumberValue(value.Z);
        json.WriteEndArray();
    }

    // The path-traced lightmapper's phase 1-2 verification: enumerate the lightmapped texels, bake the
    // direct sun + sky, report coverage, and dump the largest few pages as normal and radiance images. A
    // correct normal page reads as a UV unwrap of the object; a correct radiance page shows the sun and its
    // shadows. Dev-only, driven by IGI_BAKELM_DIR, so it never runs in a normal load.
    private void DumpLightmapVerification(
        WorldScene scene, WorldRayCast cast, Vector3f sunDirection, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        IReadOnlyList<LightmapTarget> targets = LightmapBaker.EnumerateTargets(scene);

        long covered = 0;
        long total = 0;
        foreach (LightmapTarget target in targets)
        {
            total += target.Texels.Length;
            foreach (LightmapTexel texel in target.Texels)
            {
                if (texel.IsCovered)
                {
                    covered++;
                }
            }
        }

        _logger.Information(
            "Lightmap", $"Enumerated {targets.Count} target(s); {covered}/{total} texels covered.");

        Vector3f sunColour = _timeOfDay.Sun?.Colour ?? new Vector3f(1f, 1f, 1f);
        Vector3f skyColour = _timeOfDay.Sun?.Ambient ?? new Vector3f(0.3f, 0.3f, 0.3f);
        Stepping.Drain(LightmapBaker.BakeDirectAndSky(targets, cast, sunDirection, sunColour, skyColour, 64, _work));

        // Phase 3: fold in the multi-bounce indirect. Build the lightmapped triangles and a BVH over them,
        // then gather several bounces reading the last pass's light. Grey albedo for now (colour is 3b),
        // so this verifies the bounce fills and brightens rather than the colour it carries.
        LightmapTriangle[] triangles = LightmapBaker.BuildTriangles(scene, targets, _resources);
        int coloured = triangles.Count(t => MathF.Abs(t.Albedo.X - 0.5f) > 0.02f || MathF.Abs(t.Albedo.Y - 0.5f) > 0.02f || MathF.Abs(t.Albedo.Z - 0.5f) > 0.02f);
        _logger.Information(
            "Lightmap",
            FormattableString.Invariant(
                $"Albedo: {coloured}/{triangles.Length} triangles got a resolved colour; samples ({triangles[0].Albedo.X:F2},{triangles[0].Albedo.Y:F2},{triangles[0].Albedo.Z:F2}) ({triangles[triangles.Length / 2].Albedo.X:F2},{triangles[triangles.Length / 2].Albedo.Y:F2},{triangles[triangles.Length / 2].Albedo.Z:F2})."));
        LightmapBvh bvh = LightmapBvh.Build(triangles);
        _logger.Information("Lightmap", $"Built a BVH over {triangles.Length} triangle(s); gathering bounces.");
        Stepping.Drain(LightmapBaker.BakeBounces(targets, triangles, bvh, skyColour, 4, 64, _work));

        // A numerical spot-check on the largest page: how many texels it covered, and a few of them with
        // their world position (should sit near the placement) and baked radiance (sunlit near the sun
        // colour, shadowed/skylit near the ambient). This settles what an image cannot show clearly.
        if (targets.OrderByDescending(t => t.Texels.Length).FirstOrDefault() is { } sample)
        {
            int sampleCovered = 0;
            foreach (LightmapTexel t in sample.Texels)
            {
                if (t.IsCovered)
                {
                    sampleCovered++;
                }
            }

            Vector3d origin = scene.Placements[sample.PlacementIndex].Position;
            _logger.Information(
                "Lightmap",
                FormattableString.Invariant(
                    $"Largest page p{sample.PlacementIndex} {sample.Width}x{sample.Height}: {sampleCovered} covered, placement at ({origin.X:F1},{origin.Y:F1},{origin.Z:F1})."));

            int shown = 0;
            for (int i = 0; i < sample.Texels.Length && shown < 4; i++)
            {
                if (!sample.Texels[i].IsCovered)
                {
                    continue;
                }

                LightmapTexel t = sample.Texels[i];
                Vector3f rad = sample.Radiance[i];
                _logger.Information(
                    "Lightmap",
                    FormattableString.Invariant(
                        $"  texel {i}: pos ({t.WorldPosition.X:F1},{t.WorldPosition.Y:F1},{t.WorldPosition.Z:F1}) n ({t.Normal.X:F2},{t.Normal.Y:F2},{t.Normal.Z:F2}) rad ({rad.X:F2},{rad.Y:F2},{rad.Z:F2})"));
                shown++;
            }
        }

        int dumped = 0;
        foreach (LightmapTarget target in targets.OrderByDescending(t => t.Texels.Length).Take(6))
        {
            string stem = FormattableString.Invariant(
                $"lm_p{target.PlacementIndex}_r{target.RecordIndex}_{target.Width}x{target.Height}");

            Bgra32[] normals = new Bgra32[target.Texels.Length];
            LightmapBaker.DumpTargetNormals(target, normals);
            BitmapWriter.Write(
                Path.Combine(outputDirectory, stem + "_normal.bmp"), target.Width, target.Height, normals);

            Bgra32[] radiance = new Bgra32[target.Texels.Length];
            LightmapBaker.DumpTargetRadiance(target, radiance, exposure: 1f);
            BitmapWriter.Write(
                Path.Combine(outputDirectory, stem + "_radiance.bmp"), target.Width, target.Height, radiance);

            dumped++;
        }

        _logger.Information("Lightmap", $"Dumped {dumped} page(s) to {outputDirectory}.");
    }
}
