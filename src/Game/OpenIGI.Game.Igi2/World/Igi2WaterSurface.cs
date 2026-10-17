using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One travelling wave train on a water sheet.</summary>
/// <param name="Frequency">How many crests per unit distance.</param>
/// <param name="Amplitude">How far the crest rises.</param>
/// <param name="Speed">How fast the train travels.</param>
/// <param name="Angle">Which way it travels, in radians.</param>
public readonly record struct Igi2WaterWave(float Frequency, float Amplitude, float Speed, float Angle);

/// <summary>One sheet of water a level places.</summary>
/// <param name="TaskId">The level task id, which is what names its baked environment map.</param>
/// <param name="Centre">Where the sheet sits, in world units.</param>
/// <param name="Radius">
/// How far it reaches from its centre along each axis, in world units — a <b>half-extent</b>, not a
/// radius. <c>Water_IsUnderWater</c> (<c>igi2ed 0x6477D0</c>) tests X and Y against it independently,
/// so a sheet is a square; the name is kept because that is what the parameter is called.
/// </param>
/// <param name="Detail">How finely it is tessellated.</param>
/// <param name="Alpha">How transparent it is.</param>
/// <param name="FadeOut">How fast the waves flatten with distance from the centre.</param>
/// <param name="UvScale">How far the surface texture is tiled, in metres per repeat.</param>
/// <param name="Colour">The water's own colour.</param>
/// <param name="EnvmapIndices">Which baked cube maps its archive holds, in the order the level lists them.</param>
/// <param name="Texture">Its surface texture, named as the task carries it.</param>
/// <param name="Envmap">Its environment map, named as the task carries it.</param>
/// <param name="Waves">Its wave trains, at most four of which are ever active.</param>
/// <param name="Specular">The colour of the sun's glint on it, <c>Specular color</c>.</param>
/// <param name="AbsorptionLength">How deep, in metres, the water takes to hide what is under it.</param>
/// <param name="MaxReflectionAngle">The smallest cosine its reflection is built with.</param>
public readonly record struct Igi2WaterSheet(
    int TaskId,
    Vector3d Centre,
    double Radius,
    float Detail,
    float Alpha,
    float FadeOut,
    float UvScale,
    Vector3f Colour,
    IReadOnlyList<int> EnvmapIndices,
    string Texture,
    string Envmap,
    IReadOnlyList<Igi2WaterWave> Waves,
    Vector3f Specular = default,
    float AbsorptionLength = 0f,
    float MaxReflectionAngle = 0f);

/// <summary>
/// The water a level places: where each sheet is, and what animates it.
/// </summary>
/// <remarks>
/// <para>
/// A sheet is a <c>Water</c> task with <c>WaterLayer</c> children, each layer one travelling wave train.
/// <c>Water_SetVertexFunctionParameters</c> (<c>igi2ed 0x6474A0</c>) collects them and <b>stops at
/// four</b> — a fifth layer is authored data the engine never reads. It also fixes the clock:
/// <c>Water_vTime = Timer_nTicks / 60</c>, so wave phase is seconds at the simulation's own tick rate.
/// </para>
/// <para>
/// <b>The displacement is in <see cref="Igi2WaterWaveField"/>, and an earlier version of this remark was
/// wrong about it.</b> It said the waves were evaluated by a compiled vertex shader and could not be
/// recovered. They are not: <c>Water_VertexFunction</c> (<c>igi2ed 0x645B30</c>) sums the trains on the
/// CPU and differences the normal, and <c>Water_EnterHandler</c> hands a pointer to it into the
/// renderer's draw method as a callback. The name of <c>Water_SetVertexFunctionParameters</c> is what
/// misled — those are arguments to an ordinary function.
/// </para>
/// <para>
/// <b>How a sheet blends is readable, and it is not what one would guess.</b>
/// <c>WaterRender_SetBlendState</c> (<c>0x57B6F0</c>) always enables blending and then branches on
/// <c>WaterRender_iQualityLevel</c>: above zero it sets <c>SRCBLEND = ONE</c> with
/// <c>DESTBLEND = SRCALPHA</c>, so the water's own colour is added whole and the scene behind it is
/// attenuated by the water's alpha; at zero or below it falls back to ordinary
/// <c>SRCALPHA</c>/<c>INVSRCALPHA</c>. The two are visibly different, and only the second is the
/// blend a port would arrive at on its own.
/// </para>
/// </remarks>
public sealed class Igi2WaterSurface
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.water";

    /// <summary>The most wave layers the original ever reads from a sheet.</summary>
    /// <remarks><c>Water_SetVertexFunctionParameters</c> breaks out of its walk at four.</remarks>
    public const int MaximumActiveLayers = 4;

    /// <summary>How many simulation ticks make one second of wave phase.</summary>
    /// <remarks><c>Water_vTime = Timer_nTicks * 0.016666...</c>, which is a sixtieth.</remarks>
    public const double TicksPerSecond = 60.0;

    private Igi2WaterSurface(IReadOnlyList<Igi2WaterSheet> sheets) => Sheets = sheets;

    /// <summary>Gets the level's sheets, in the order it declared them.</summary>
    public IReadOnlyList<Igi2WaterSheet> Sheets { get; }

    /// <summary>Gets a value indicating whether the level places any water.</summary>
    public bool IsEmpty => Sheets.Count == 0;

    /// <summary>Collects the water a level places.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <returns>The water, which may be empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static Igi2WaterSurface Build(GameTask levelRoot, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        List<Igi2WaterSheet> sheets = [];

        foreach (GameTask task in Igi2TaskTree.Descendants(levelRoot))
        {
            if (task is not WaterTask water)
            {
                continue;
            }

            // The layers are the task's own children, in declaration order, which is the order the
            // original walks them in before it stops at four.
            List<Igi2WaterWave> waves = [];
            foreach (GameTask child in task.Children)
            {
                if (child is WaterLayerTask layer)
                {
                    waves.Add(new Igi2WaterWave(layer.Frequency, layer.Amplitude, layer.Speed, layer.Angle));
                }
            }

            sheets.Add(new Igi2WaterSheet(
                task.Id,
                water.Position,
                water.Size * WorldScene.UnitsPerMetre,
                water.Detail,
                water.Alpha,
                water.FadeOut,
                water.UvScale,
                water.DiffuseColour,
                water.EnvmapIndices,
                water.Texture,
                water.Envmap,
                waves,
                water.SpecularColour,
                water.AbsorptionLength,
                water.MaxReflectionAngle));
        }

        log.Information(LogCategory, $"Water: {sheets.Count} sheet(s).");
        return new Igi2WaterSurface(sheets);
    }

    /// <summary>Gets the wave phase clock for a tick count.</summary>
    /// <param name="ticks">The simulation's tick counter.</param>
    /// <returns>The time the wave trains are evaluated at, in seconds.</returns>
    public static double TimeFor(long ticks) => ticks / TicksPerSecond;

    /// <inheritdoc/>
    public override string ToString() => $"I.G.I. 2 water, {Sheets.Count} sheet(s)";
}
