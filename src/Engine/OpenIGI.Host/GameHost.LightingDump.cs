using System.Text.Json;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
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
            json.WriteBoolean("followsDaylight", _lampDaylight.Photocells?.Follows(i) ?? false);
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
}
