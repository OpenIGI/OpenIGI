using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>Which lamps hold a shadow map, and drawing those maps.</summary>
/// <remarks>
/// Only where the device marches its lamp shadows rather than tracing them (see
/// <see cref="IGraphicsDevice.WantsLampShadowMaps"/>). The nearest lamps that face a way -- every wall and
/// ceiling fitting -- take the slots, nearest first, and keep them while they stay among the nearest, so a
/// map is drawn once when its lamp arrives rather than every frame. A couple are redrawn each frame in
/// turn, which is how a soldier walking under a lamp comes to cast in it.
/// </remarks>
internal sealed partial class GameHost
{
    /// <summary>How many maps are drawn in a frame for lamps that have just taken a slot.</summary>
    private const int LampMapsNewPerFrame = 4;

    /// <summary>How many already-drawn maps are redrawn each frame, in turn.</summary>
    private const int LampMapsRefreshedPerFrame = 2;

    /// <summary>The lamp map's field of view: most of the hemisphere a fitting lights.</summary>
    private const float LampMapFieldOfView = 150f * (MathF.PI / 180f);

    // Per slot: the fixture it holds (-1 for none), whether its map still has to be drawn, and the camera
    // it is drawn from.
    private readonly int[] _lampMapFixture = CreateEmptySlots();
    private readonly bool[] _lampMapPending = new bool[64];
    private readonly Vector3d[] _lampMapPosition = new Vector3d[64];
    private readonly Matrix3f[] _lampMapRotation = new Matrix3f[64];
    private readonly Matrix4f[] _lampMapProjection = new Matrix4f[64];
    private readonly int[] _lampMapSlotPerLight = new int[DeferredLocalLight.Maximum];
    private readonly HashSet<int> _lampMapWanted = [];
    private int _lampMapRefreshCursor;

    private static int[] CreateEmptySlots()
    {
        int[] slots = new int[64];
        Array.Fill(slots, -1);
        return slots;
    }

    /// <summary>Gives the frame's nearest facing lamps a map slot each and tells the device which they read.</summary>
    /// <param name="lightCount">How many lamps were handed over, in <c>_localLightOrder</c>'s order.</param>
    /// <param name="range">The reach multiplier the lamps were handed over with.</param>
    private void AssignLampMaps(int lightCount, float range)
    {
        int slots = Math.Min(_device.LampShadowMapSlots, _lampMapFixture.Length);
        if (!_device.WantsLampShadowMaps || slots <= 0)
        {
            _device.SetLampShadowMapSlots(ReadOnlySpan<int>.Empty);
            return;
        }

        // Which fixtures want one: the nearest that face somewhere, as many as there are slots.
        _lampMapWanted.Clear();
        for (int i = 0; i < lightCount && _lampMapWanted.Count < slots; i++)
        {
            int fixture = _localLightOrder[i];
            if (_lightFixtures[fixture].IsDirectional)
            {
                _lampMapWanted.Add(fixture);
            }
        }

        // Slots whose fixture is no longer wanted are freed; the rest keep what they hold.
        for (int slot = 0; slot < slots; slot++)
        {
            if (_lampMapFixture[slot] >= 0 && !_lampMapWanted.Contains(_lampMapFixture[slot]))
            {
                _lampMapFixture[slot] = -1;
                _lampMapPending[slot] = false;
            }
            else if (_lampMapFixture[slot] >= 0)
            {
                _lampMapWanted.Remove(_lampMapFixture[slot]);
            }
        }

        // What is left wanted takes a free slot, and waits there for its map.
        foreach (int fixture in _lampMapWanted)
        {
            int free = Array.IndexOf(_lampMapFixture, -1, 0, slots);
            if (free < 0)
            {
                break;
            }

            LightFixture lamp = _lightFixtures[fixture];
            Vector3f facing = lamp.Direction.Normalized();
            _lampMapFixture[free] = fixture;
            _lampMapPending[free] = true;

            // From just in front of the glass, so the fitting's own housing and the wall it is on are behind
            // the camera rather than filling the map.
            _lampMapPosition[free] = new Vector3d(
                lamp.Position.X + (facing.X * 0.06),
                lamp.Position.Y + (facing.Y * 0.06),
                lamp.Position.Z + (facing.Z * 0.06));
            _lampMapRotation[free] = ShadowCascades.LightRotationFor(-facing);
            float reach = Math.Clamp(lamp.Radius * range, 2f, 60f);
            _lampMapProjection[free] = Matrix4f.CreatePerspective(LampMapFieldOfView, 1f, 0.05f, reach);
        }

        // And the device is told which of this frame's lamps reads which map -- none while its map is still
        // waiting to be drawn, since the slot holds the picture from the lamp it last belonged to.
        for (int i = 0; i < lightCount; i++)
        {
            int slot = Array.IndexOf(_lampMapFixture, _localLightOrder[i], 0, slots);
            _lampMapSlotPerLight[i] = slot >= 0 && !_lampMapPending[slot] ? slot : -1;
        }

        _device.SetLampShadowMapSlots(_lampMapSlotPerLight.AsSpan(0, lightCount));
    }

    /// <summary>Draws the maps that are waiting, and redraws a couple of the rest in turn.</summary>
    /// <param name="pass">The caster pass, which draws through the title's presenters.</param>
    /// <param name="scene">The world's placed objects.</param>
    /// <param name="world">The presenter that draws them.</param>
    /// <param name="characters">The character presenter.</param>
    /// <remarks>Before the scene capture opens, beside the cascades.</remarks>
    private void RenderLampMaps(
        ShadowCasterPass pass,
        WorldScene? scene,
        IWorldPresenter? world,
        ICharacterPresenter? characters)
    {
        int slots = Math.Min(_device.LampShadowMapSlots, _lampMapFixture.Length);
        if (!_device.WantsLampShadowMaps || slots <= 0)
        {
            return;
        }

        int drawnNew = 0;
        for (int slot = 0; slot < slots && drawnNew < LampMapsNewPerFrame; slot++)
        {
            if (_lampMapFixture[slot] >= 0 && _lampMapPending[slot] && DrawLampMap(slot))
            {
                _lampMapPending[slot] = false;
                drawnNew++;
            }
        }

        for (int refreshed = 0, tried = 0; refreshed < LampMapsRefreshedPerFrame && tried < slots; tried++)
        {
            int slot = _lampMapRefreshCursor;
            _lampMapRefreshCursor = (_lampMapRefreshCursor + 1) % slots;
            if (_lampMapFixture[slot] >= 0 && !_lampMapPending[slot] && DrawLampMap(slot))
            {
                refreshed++;
            }
        }

        bool DrawLampMap(int slot) => pass.RenderLampMap(
            slot,
            _lampMapPosition[slot],
            _lampMapRotation[slot],
            _lampMapProjection[slot],
            scene,
            world,
            characters);
    }
}
