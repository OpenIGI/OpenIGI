using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The keyed lights, as dirlighttask.c and globallighttask.c update them through the day.</summary>
public sealed class Igi2LightTests
{
    /// <summary>
    /// A sun between two keyframes takes their colours part way and turns from the earlier's direction towards the later's by the share
    /// of the angle between them; at a keyframe's hour it is that keyframe's; before the first it is the first's.
    /// </summary>
    [Fact]
    public void A_sun_is_keyed_through_the_day()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        TaskType lightType = types.Register<Igi2DirlightTask>(Igi2DirlightTask.TypeName);
        TaskType keyType = types.Register<Igi2DirlightKeyframeTask>(Igi2DirlightKeyframeTask.TypeName);
        Igi2DirlightTask sun = (Igi2DirlightTask)tree.New(null, lightType);
        Igi2DirlightKeyframeTask morning = (Igi2DirlightKeyframeTask)tree.New(sun, keyType);
        Igi2DirlightKeyframeTask noon = (Igi2DirlightKeyframeTask)tree.New(sun, keyType);
        (morning.Hour, morning.Beta, morning.Gamma, morning.FrontColour) = (6f, 0f, 0f, new Vector3f(1f, 0f, 0f));
        (noon.Hour, noon.Beta, noon.Gamma, noon.FrontColour) = (12f, MathF.PI / 2f, 0f, new Vector3f(0f, 0f, 1f));
        sun.Prepare();

        sun.SetTime(6f);
        Assert.Equal(new Vector3f(1f, 0f, 0f), sun.FrontColour);
        Assert.Equal(morning.Direction, sun.Direction);

        sun.SetTime(9f);
        Assert.Equal(0.5f, sun.FrontColour.X, 5);
        Assert.Equal(0.5f, sun.FrontColour.Z, 5);
        float elevation = MathF.Asin(sun.Direction.Z);
        Assert.Equal(MathF.PI / 4f, elevation, 2);

        sun.SetTime(1f);
        Assert.Equal(morning.FrontColour, sun.FrontColour);
        sun.SetTime(20f);
        Assert.Equal(noon.FrontColour, sun.FrontColour);
    }

    /// <summary>Without keyframes a sun is dark and points straight up.</summary>
    [Fact]
    public void A_sun_without_keyframes_is_dark()
    {
        Igi2DirlightTask sun = new();
        sun.Prepare();
        Assert.Equal(default, sun.FrontColour);
        Assert.Equal(new Vector3f(0f, 0f, 1f), sun.Direction);
    }

    /// <summary>
    /// A global light between two keyframes interpolates every category straight; a category linked to another takes its values, a
    /// chain of links carrying through.
    /// </summary>
    [Fact]
    public void A_global_light_is_keyed_and_linked()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        TaskType lightType = types.Register<Igi2GlobalLightTask>(Igi2GlobalLightTask.TypeName);
        TaskType keyType = types.Register<Igi2GlobalLightKeyframeTask>(Igi2GlobalLightKeyframeTask.TypeName);
        Igi2GlobalLightTask light = (Igi2GlobalLightTask)tree.New(null, lightType);
        Igi2GlobalLightKeyframeTask dawn = (Igi2GlobalLightKeyframeTask)tree.New(light, keyType);
        Igi2GlobalLightKeyframeTask dusk = (Igi2GlobalLightKeyframeTask)tree.New(light, keyType);
        dawn.Hour = 0f;
        dawn.TerrainAmbient = new Vector3f(0f, 0f, 0f);
        dawn.TerrainFogDensity = 0f;
        dawn.Category2Link = 1;
        dawn.Category1Link = 0;
        dusk.Hour = 10f;
        dusk.TerrainAmbient = new Vector3f(1f, 1f, 1f);
        dusk.TerrainFogDensity = 1f;
        dusk.SkyColour = new Vector3f(0f, 0f, 0f);
        light.Prepare();

        Assert.Equal(dawn.Linked[0], dawn.Linked[1]);
        Assert.Equal(dawn.Linked[0], dawn.Linked[2]);

        light.SetTime(2.5f);
        Assert.Equal(0.25f, light.Category[0].Ambient.X, 5);
        Assert.Equal(0.25f, light.Category[0].FogDensity, 5);
        Assert.Equal(0.75f, light.SkyColour.X, 5);
    }

    /// <summary>
    /// A lens flare follows its sun's colour, eases its intensity towards how much of the sun is seen, a fifth a frame, and strings its
    /// items along the line through the display's centre in whole pixels; a mirrored item is four quads about its place.
    /// </summary>
    [Fact]
    public void A_lens_flare_eases_in_and_strings_its_items()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        TaskType sunType = types.Register<Igi2DirlightTask>(Igi2DirlightTask.TypeName);
        TaskType keyType = types.Register<Igi2DirlightKeyframeTask>(Igi2DirlightKeyframeTask.TypeName);
        TaskType flareType = types.Register<Igi2LensflareTask>(Igi2LensflareTask.TypeName);
        TaskType itemType = types.Register<Igi2LensflareItemTask>(Igi2LensflareItemTask.TypeName);
        Igi2DirlightTask sun = (Igi2DirlightTask)tree.New(null, sunType);
        Igi2DirlightKeyframeTask key = (Igi2DirlightKeyframeTask)tree.New(sun, keyType);
        key.FrontColour = new Vector3f(1f, 0.5f, 0.25f);
        sun.Prepare();
        Igi2LensflareTask flare = (Igi2LensflareTask)tree.New(sun, flareType);
        Igi2LensflareItemTask item = (Igi2LensflareItemTask)tree.New(flare, itemType);
        (item.Position, item.Size, item.SpriteIndex, item.Mirror) = (0.5f, 100f, 12, false);

        System.Numerics.Vector2 min = new(0f, 0f), max = new(640f, 480f);
        IReadOnlyList<Igi2LensflareQuad> quads = flare.Frame(new System.Numerics.Vector2(100f, 100f), 50000f, min, max, static () => 1f);
        Assert.Equal(key.FrontColour, flare.Colour);
        Assert.Equal(0.2f, flare.Intensity, 5);
        Igi2LensflareQuad quad = Assert.Single(quads);
        Assert.Equal(Igi2LensflareItemTask.Sprites - 1, quad.Sprite);
        Assert.Equal(320f, quad.X0);
        Assert.Equal(240f, quad.Y0);
        Assert.Equal(320f + 99f, quad.X1, 3);

        // Hidden behind the near plane, it fades a fifth a frame and is gone below a hundredth.
        for (int i = 0; i < 30; i++)
        {
            quads = flare.Frame(new System.Numerics.Vector2(100f, 100f), 1f, min, max, static () => 1f);
        }

        Assert.Empty(quads);
        item.Mirror = true;
        Assert.Equal(4, Next(flare).Count);

        static IReadOnlyList<Igi2LensflareQuad> Next(Igi2LensflareTask flare)
        {
            IReadOnlyList<Igi2LensflareQuad> drawn = [];
            for (int i = 0; i < 5; i++)
            {
                drawn = flare.Frame(new System.Numerics.Vector2(100f, 100f), 50000f, new(0f, 0f), new(640f, 480f), static () => 1f);
            }

            return drawn;
        }
    }
}
