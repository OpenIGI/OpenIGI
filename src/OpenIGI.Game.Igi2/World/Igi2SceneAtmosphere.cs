using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// The air a level is played in: what colour the distance goes, how quickly it gets there, and the flat
/// light everything sits in. Read from I.G.I. 2's <c>GlobalLight</c> task.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the sequel's whole atmosphere, and the port had none of it.</b>
/// <c>GlobalLightQTask_DrawCallback</c> (<c>igi2ed 0x6534E0</c>) is four calls long and does all of it
/// once a frame:
/// </para>
/// <code>
/// DrawTerrainMesh_SetAmbientColor(globalLight + 108)   // tObjectAmbientColor
/// vDensity = *(float *)(globalLight + 132) * 0.0005    // vObjectFogDensity
/// DrawTerrainMesh_SetFog(globalLight + 120, vDensity)  // tObjectFogColor
/// Sky_SetColor(globalLight + 56)
/// </code>
/// <para>
/// Those three offsets are twelve bytes apart and then four, which is exactly
/// <c>GlobalLightCategory_t</c> as the dedicated server's own type information lays it out —
/// <c>tObjectAmbientColor</c>, <c>tObjectFogColor</c>, <c>vObjectFogDensity</c> — so the arithmetic
/// names itself.
/// </para>
/// <para>
/// <b>The fog is on everything.</b> Every render state block in the game enables it against this one
/// colour: the ground (<c>TerrainRender_SetGenericState</c>, <c>0x66BAD0</c>), the foliage
/// (<c>ForestRender_SetGenericState_AlphaTestFoliage</c>, <c>0x57C770</c>), the water and the objects.
/// <c>TerrainRender_CalculateRenderVertex</c> (<c>0x66A740</c>) writes <c>exp(-density * distance)</c>
/// into the fourth float of every terrain vertex, and <c>TerrainRender_SetVertexShaderConstants</c>
/// (<c>0x667D20</c>) hands the shader the view-space depth row scaled by the same density. It is a
/// plain exponential haze, per vertex, in front of the whole world.
/// </para>
/// <para>
/// <b>Which is why a night level reads as night.</b> location1/level1 authors a fog colour of
/// (0, 0.162, 0.287) — a deep blue — over an ambient of (0.058, 0.100, 0.180). Without the haze the
/// port drew every surface at its own unfogged colour against a black sky, so the distance stayed as
/// contrasty as the foreground and nothing took the night's cast.
/// </para>
/// <para>
/// <b>This is the sequel's alone.</b> Project I.G.I. has no <c>GlobalLight</c> task and no fog here,
/// and nothing in this type is reachable from its title — see
/// <c>ITitlePresentationProvider.ReadAtmosphere</c>, which only this game answers.
/// </para>
/// </remarks>
/// <param name="FogColour">The colour the distance goes, linear, from <c>Fog color terrain</c>.</param>
/// <param name="FogDensity">
/// The extinction per metre: the authored <c>Fog density terrain</c> times
/// <see cref="AuthoredDensityScale"/>. A fragment keeps <c>exp(-density * distance)</c> of itself.
/// </param>
/// <param name="Ambient">The flat light every surface sits in, from <c>Ambient color terrain</c>.</param>
/// <param name="SkyColour">The sky's own colour, from <c>Sky color</c>.</param>
/// <param name="TimeOfDay">The keyframe's <c>Time</c>, an hour of the day.</param>
/// <param name="ObjectAmbient">
/// The flat light an unlightmapped OBJECT sits in, from <c>Ambient color object category 1</c>. The
/// keyframe authors four object categories beside the terrain's, and the renderer reads exactly one of
/// them: <c>GlobalLightQTask_DrawCallback</c> copies every category into <c>Mesh3D_atAmbientColor</c>
/// and <c>Mesh3D_atFogColor</c> in order, terrain first, and every object state block —
/// <c>Direct3DRender_SetGenericStateRigid</c> (<c>0x56CA90</c>), <c>SetGenericStateLightmap</c>
/// (<c>0x56CFD0</c>), the forest's and the water's — reads the slot at index one. Nothing anywhere
/// reads index two or beyond. This is the value <c>Lightsource_SetAmbientlight</c> (<c>0x41C990</c>)
/// hands Direct3D as <c>D3DRS_AMBIENT</c> for the fixed-function lighting of objects without a bake.
/// (The same slot's fog colour and density are what objects are fogged against; every retail level
/// probed authors them equal to the terrain's, so <see cref="FogColour"/> serves both.)
/// </param>
public readonly record struct Igi2SceneAtmosphere(
    Vector3f FogColour,
    float FogDensity,
    Vector3f Ambient,
    Vector3f SkyColour,
    float TimeOfDay,
    Vector3f ObjectAmbient)
{
    /// <summary>The task type the level declares its atmosphere on.</summary>
    public const string TaskTypeName = "GlobalLight";

    /// <summary>The task type each of its keyframes is declared as.</summary>
    public const string KeyframeTypeName = "GlobalLightKeyframe";

    /// <summary>What the authored density is multiplied by on its way to a per-metre extinction.</summary>
    /// <remarks>
    /// <para>
    /// <c>GlobalLightQTask_DrawCallback</c> writes <c>vObjectFogDensity * 0.0005</c>, and that number is
    /// per DIRECT3D UNIT, not per metre. <c>Direct3D_New</c> (<c>igi2ed 0x55AE63</c>) sets
    /// <c>Direct3D_vScale</c> to <c>0.001</c> — a Direct3D unit is a thousand engine units, about a
    /// quarter of a metre — and every distance the fog is evaluated over is scaled by it:
    /// <c>Direct3DRender_SetFogParameters</c> (<c>0x5681E0</c>) multiplies an object's view depth by
    /// <c>vScale</c> before the density, and <c>TerrainRender_SetVertexShaderConstants</c>
    /// (<c>0x667D20</c>) builds the matrix <c>TerrainRender_CalculateRenderVertex</c> (<c>0x66A740</c>)
    /// takes its depth from with <c>vScale</c> on its diagonal. A metre is 4.096 of those units, so the
    /// per-metre extinction is <c>0.0005 × 4.096</c>.
    /// </para>
    /// <para>
    /// location1/level1's authored 0.85 is therefore 1.74e-3 per metre — a haze that halves what it is
    /// looking through every four hundred metres — and not the 4.25e-4 this constant first read, which
    /// left every tank, tree and hill a quarter as hazed as the original draws them.
    /// </para>
    /// </remarks>
    public const float AuthoredDensityScale = 0.0005f * (float)(Game.World.WorldScene.UnitsPerMetre / 1000.0);

    /// <summary>
    /// The highest <c>Time</c> treated as an hour of the day rather than as an unused slot.
    /// </summary>
    /// <remarks>
    /// The same template rows the sun has, and the same reason for skipping them: location1/level1
    /// ships keyframes at 12, 1003, 1004 and 1005, and only the first is an hour. See
    /// <see cref="Game.World.DirectionalLightSettings.LatestHour"/>.
    /// </remarks>
    public const float LatestHour = 24f;

    /// <summary>Reads a level's atmosphere, if it declares one.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="atmosphere">The atmosphere, when the level declares a <c>GlobalLight</c>.</param>
    /// <returns><see langword="true"/> when one was read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static bool TryRead(GameTask levelRoot, out Igi2SceneAtmosphere atmosphere)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        atmosphere = default;

        if (Find(levelRoot) is not { } light || SelectKeyframe(light) is not { } keyframe)
        {
            return false;
        }

        atmosphere = new Igi2SceneAtmosphere(
            Colour(keyframe, "Fog color terrain", Vector3f.Zero),
            Math.Max(0f, Float(keyframe, "Fog density terrain", 0f)) * AuthoredDensityScale,
            Colour(keyframe, "Ambient color terrain", Vector3f.Zero),
            Colour(keyframe, "Sky color", new Vector3f(0.5f, 0.55f, 0.6f)),
            Float(keyframe, "Time", 12f),
            Colour(keyframe, "Ambient color object category 1", Vector3f.Zero));

        return true;
    }

    private static DeclaredTask? SelectKeyframe(DeclaredTask light)
    {
        DeclaredTask? best = null;
        float bestTime = float.MaxValue;

        foreach (GameTask child in light.Children)
        {
            if (child is not DeclaredTask declared
                || !string.Equals(declared.Type.Name, KeyframeTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            float time = Float(declared, "Time", float.MaxValue);
            if (time >= 0f && time <= LatestHour && time < bestTime)
            {
                best = declared;
                bestTime = time;
            }
        }

        return best;
    }

    private static DeclaredTask? Find(GameTask task)
    {
        if (task is DeclaredTask declared
            && string.Equals(declared.Type.Name, TaskTypeName, StringComparison.Ordinal))
        {
            return declared;
        }

        foreach (GameTask child in task.Children)
        {
            if (Find(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static float Float(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) && value is float number ? number : fallback;

    private static Vector3f Colour(DeclaredTask task, string name, Vector3f fallback) =>
        task.TryGetValue(name, out object? value) && value is Vector3f colour ? colour : fallback;
}
