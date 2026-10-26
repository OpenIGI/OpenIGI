using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>A level's <c>RainEffect</c> as the original runs and draws it: its weather, told each tick whether to fall, and its drawing.</summary>
/// <remarks>
/// <para>
/// Project I.G.I.'s <c>RainEffect</c> (<c>RainEffect_Changed</c>, <c>0x436A50</c>) makes its weather from "Is Rain" (snow when it is
/// false), "Traceline start" and "Traceline end" in metres (where a tile's drop starts above the eye, and how much farther it falls)
/// and "Rain Alpha" (0.3125 when the level leaves it out); <c>RainEffect_Run</c> (<c>0x436990</c>) has "Is Active" say each tick
/// whether it falls, and without one it never does.
/// </para>
/// <para>
/// Every "Is Active" the original's levels carry is a constant -- <c>1</c>, <c>TRUE</c> or <c>FALSE</c> -- so it is read once.
/// </para>
/// </remarks>
public sealed class RetailWeather : IDisposable
{
    /// <summary>The alpha a <c>RainEffect</c> without one falls at (<c>RainEffect_Create</c>, <c>0x436950</c>).</summary>
    public const float DefaultAlpha = 0.3125f;

    private readonly RetailPrecipitationRenderer _renderer;
    private readonly bool _falling;

    private RetailWeather(RetailPrecipitation precipitation, RetailPrecipitationRenderer renderer, bool falling)
    {
        Precipitation = precipitation;
        _renderer = renderer;
        _falling = falling;
    }

    /// <summary>Gets the weather.</summary>
    public RetailPrecipitation Precipitation { get; }

    /// <summary>Makes a level's weather from its <c>RainEffect</c>, or none when it has none.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="device">The device it is drawn with.</param>
    /// <param name="style">Whose drawing it follows.</param>
    /// <param name="flake">What a flake is drawn with: the game's <c>snow.tex</c>.</param>
    /// <returns>The weather, or <see langword="null"/>.</returns>
    public static RetailWeather? FromLevel(GameTask levelRoot, IGraphicsDevice device, PrecipitationStyle style, TextureHandle flake)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(device);
        if (RainRenderer.FindRainEffect(levelRoot) is not { } task)
        {
            return null;
        }

        bool rain = RainRenderer.ReadFlag(task, "Is Rain");
        bool falling = task.TryGetScriptValue("Is Active", out object? active) && active switch
        {
            bool flag => flag,
            int value => value != 0,
            float value => value != 0f,
            string text => RainRenderer.IsTrueText(text),
            _ => false,
        };

        float start = RainRenderer.ReadFloat(task, "Traceline start", 50f);
        float end = RainRenderer.ReadFloat(task, "Traceline end", 20f);
        float alpha = Math.Clamp(RainRenderer.ReadFloat(task, "Rain Alpha", DefaultAlpha), 0f, 1f);

        // Rain_SetAlpha (0x499FA0): the foot of a drop takes the alpha, rounded to a byte.
        RetailPrecipitationRenderer renderer = new(device, style)
        {
            Texture = flake,
            Colour = new Bgra32(255, 255, 255, (byte)MathF.Round(alpha * 255f)),
            HeightUnits = start * WorldScene.UnitsPerMetre,
            FallUnits = (start + end) * WorldScene.UnitsPerMetre,
        };

        return new RetailWeather(new RetailPrecipitation(snow: !rain), renderer, falling);
    }

    /// <summary>Runs the weather one tick.</summary>
    public void Tick()
    {
        Precipitation.IsFalling = _falling;
        Precipitation.Tick();
    }

    /// <summary>Draws the weather around the eye.</summary>
    /// <param name="camera">The eye, in metres.</param>
    /// <param name="projection">The projection the frame is drawn with.</param>
    /// <param name="viewportHeight">The picture's height in pixels.</param>
    /// <param name="ground">What the weather stops on.</param>
    public void Draw(in Camera3D camera, in Matrix4f projection, int viewportHeight, WorldRayCast? ground) =>
        _renderer.Draw(Precipitation, camera, projection, viewportHeight, ground);

    /// <inheritdoc/>
    public void Dispose() => _renderer.Dispose();
}
