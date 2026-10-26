using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>What a level's <c>RainEffect</c> makes its weather of.</summary>
/// <param name="Rain">Whether it is rain rather than snow.</param>
/// <param name="StartMetres">How far above the eye a tile's drop starts ("Traceline start").</param>
/// <param name="EndMetres">How much farther below the eye it may fall ("Traceline end").</param>
/// <param name="Colour">The weather's colour and alpha.</param>
/// <param name="Falling">Whether it is told to fall, asked each tick.</param>
public readonly record struct RetailWeatherSettings(bool Rain, float StartMetres, float EndMetres, Bgra32 Colour, Func<bool> Falling);

/// <summary>A level's weather as the original runs and draws it: told each tick whether to fall, and drawn about the eye.</summary>
/// <remarks>
/// Both games' <c>RainEffect</c> make a <c>Rain</c> object of the level's settings and tell it each tick whether to fall; the object
/// runs on the game's ticks (<see cref="RetailPrecipitation"/>) and draws on the frame (<see cref="RetailPrecipitationRenderer"/>).
/// Each title builds this from its own task, with its own textures.
/// </remarks>
public sealed class RetailWeather : IWeatherPresenter
{
    /// <summary>The alpha a <c>RainEffect</c> without one falls at (<c>RainEffect_Create</c>, <c>0x436950</c>).</summary>
    public const float DefaultAlpha = 0.3125f;

    // The most ticks a frame catches up; a longer gap is not run through.
    private const int MaximumCatchUp = 120;

    private readonly IGraphicsDevice _device;
    private readonly RetailPrecipitationRenderer _renderer;
    private readonly Func<bool> _falling;
    private readonly TextureHandle[] _owned;
    private long? _tick;

    /// <summary>Initializes a new instance of the <see cref="RetailWeather"/> class.</summary>
    /// <param name="device">The device it is drawn with.</param>
    /// <param name="style">Whose drawing it follows.</param>
    /// <param name="settings">What it is made of.</param>
    /// <param name="texture">What a flake, or an I.G.I. 2 drop, is drawn with; it is the weather's to release.</param>
    /// <param name="random">The weather's own stream; a freshly seeded one without it.</param>
    /// <param name="copies">Where I.G.I. 2 draws each drop's copies, in world units; once, where it is, without them.</param>
    public RetailWeather(
        IGraphicsDevice device,
        PrecipitationStyle style,
        in RetailWeatherSettings settings,
        TextureHandle texture,
        IgiRandom? random = null,
        IReadOnlyList<Vector3f>? copies = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settings.Falling);
        _device = device;
        _falling = settings.Falling;
        _owned = texture.IsValid ? [texture] : [];
        Precipitation = new RetailPrecipitation(snow: !settings.Rain, random);
        _renderer = new RetailPrecipitationRenderer(device, style)
        {
            Texture = texture,
            Colour = settings.Colour,
            HeightUnits = settings.StartMetres * WorldScene.UnitsPerMetre,
            FallUnits = (settings.StartMetres + settings.EndMetres) * WorldScene.UnitsPerMetre,
            Copies = copies ?? [],
        };
    }

    /// <summary>Gets the weather.</summary>
    public RetailPrecipitation Precipitation { get; }

    /// <summary>Gets the colour a level's alpha gives Project I.G.I.'s weather: white, the alpha rounded to a byte (<c>Rain_SetAlpha</c>, <c>0x499FA0</c>).</summary>
    /// <param name="alpha">The level's "Rain Alpha".</param>
    /// <returns>The colour.</returns>
    public static Bgra32 WhiteAt(float alpha) => new(255, 255, 255, (byte)MathF.Round(Math.Clamp(alpha, 0f, 1f) * 255f));

    /// <summary>Reads Project I.G.I.'s <c>RainEffect</c> (<c>RainEffect_Changed</c>, <c>0x436A50</c>; <c>RainEffect_Run</c>, <c>0x436990</c>).</summary>
    /// <param name="task">The task.</param>
    /// <returns>What it makes its weather of.</returns>
    /// <remarks>
    /// "Is Rain" (snow when false), "Traceline start", "Traceline end", "Rain Alpha" (0.3125 without one), and "Is Active", which says
    /// each tick whether it falls and without which it never does. Every "Is Active" the levels carry is a constant -- <c>1</c>,
    /// <c>TRUE</c> or <c>FALSE</c> -- so it is read once.
    /// </remarks>
    public static RetailWeatherSettings ReadIgi1(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        bool falling = task.TryGetScriptValue("Is Active", out object? active) && active switch
        {
            bool flag => flag,
            int value => value != 0,
            float value => value != 0f,
            string text => RainRenderer.IsTrueText(text),
            _ => false,
        };

        return new RetailWeatherSettings(
            RainRenderer.ReadFlag(task, "Is Rain"),
            RainRenderer.ReadFloat(task, "Traceline start", 50f),
            RainRenderer.ReadFloat(task, "Traceline end", 20f),
            WhiteAt(RainRenderer.ReadFloat(task, "Rain Alpha", DefaultAlpha)),
            () => falling);
    }

    /// <summary>Uploads the first of a texture's resource names that is there, clamped.</summary>
    /// <param name="device">The device.</param>
    /// <param name="resources">Where it is read from.</param>
    /// <param name="names">The names it is tried under, in order.</param>
    /// <returns>The texture, or <see cref="TextureHandle.None"/>.</returns>
    public static TextureHandle LoadTexture(IGraphicsDevice device, ResourceRegistry resources, params ReadOnlySpan<string> names)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        foreach (string name in names)
        {
            if (resources.TryLoad(name, out ReadOnlyMemory<byte> bytes) && TexFile.IsLoopAsset(bytes.Span))
            {
                TextureImage image = TexFile.Load(bytes.Span);
                return device.CreateTexture(
                    new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, TextureAddressMode.Clamp),
                    image.Pixels);
            }
        }

        return TextureHandle.None;
    }

    /// <inheritdoc/>
    public void Draw(in WeatherFrame frame)
    {
        // Run on the game's ticks up to the frame's, then drawn.
        long now = frame.Tick;
        long from = _tick is { } last && now - last <= MaximumCatchUp ? last : now - 1;
        for (long tick = from; tick < now; tick++)
        {
            Precipitation.IsFalling = _falling();
            Precipitation.Tick();
        }

        _tick = now;
        _renderer.Draw(Precipitation, frame.Camera, frame.Projection, frame.ViewportHeight, frame.Ground);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _renderer.Dispose();
        foreach (TextureHandle texture in _owned)
        {
            _device.DestroyTexture(texture);
        }
    }
}
