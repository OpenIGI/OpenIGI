using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>Which lamps hold shadow maps, and drawing those maps.</summary>
/// <remarks>
/// <para>
/// Only where the device marches its lamp shadows rather than tracing them (see
/// <see cref="IGraphicsDevice.WantsLampShadowMaps"/>). The lamps lighting the view take the map slots,
/// nearest first, and keep them while they stay wanted, so a map is drawn once when its lamp arrives rather
/// than every frame. A couple of slots are redrawn each frame in turn, which is how a soldier walking
/// under a lamp comes to cast in it.
/// </para>
/// <para>
/// Every lamp takes six: a cube of ninety-degree views, as a point light's shadow is drawn -- from just in
/// front of the glass for a fitting that faces a way, from its centre for one that does not.
/// Whatever lies outside a lamp's maps falls to the distance-field march, which cannot hold a thin slab or
/// tell a lamp's own housing from a wall, so the maps leave nothing in front of a lamp outside them.
/// </para>
/// </remarks>
internal sealed partial class GameHost
{
    /// <summary>How many map slots are drawn in a frame for lamps that have just taken them.</summary>
    private const int LampMapsNewPerFrame = 8;

    /// <summary>How many already-drawn slots are redrawn each frame, in turn.</summary>
    private const int LampMapsRefreshedPerFrame = 2;

    /// <summary>How far in front of a fitting's glass its cube is drawn from, in metres.</summary>
    private const double LampMapStandOff = 0.06;

    /// <summary>The most slots this side keeps track of; the device says how many it actually has.</summary>
    private const int LampMapSlotsTracked = 240;

    /// <summary>The bit a lamp's slot carries when it is the first of a cube's six.</summary>
    private const int LampMapCubeBit = 0x100;

    // The six faces of a cube map, along the world's axes.
    private static readonly Vector3f[] CubeFaceAxes =
    [
        new(1f, 0f, 0f), new(-1f, 0f, 0f), new(0f, 1f, 0f), new(0f, -1f, 0f), new(0f, 0f, 1f), new(0f, 0f, -1f),
    ];

    // Per slot: the fixture it holds (-1 for none), whether its map still has to be drawn, and the camera
    // it is drawn from.
    private readonly int[] _lampMapFixture = CreateEmptySlots();
    private readonly bool[] _lampMapPending = new bool[LampMapSlotsTracked];
    private readonly Vector3d[] _lampMapPosition = new Vector3d[LampMapSlotsTracked];
    private readonly Matrix3f[] _lampMapRotation = new Matrix3f[LampMapSlotsTracked];
    private readonly Matrix4f[] _lampMapProjection = new Matrix4f[LampMapSlotsTracked];
    private readonly int[] _lampMapSlotPerLight = new int[DeferredLocalLight.Maximum];
    private readonly List<int> _lampMapWanted = [];
    private int _lampMapRefreshCursor;

    private static int[] CreateEmptySlots()
    {
        int[] slots = new int[LampMapSlotsTracked];
        Array.Fill(slots, -1);
        return slots;
    }

    /// <summary>Gives the frame's nearest lamps their map slots and tells the device which they read.</summary>
    /// <param name="lightCount">How many lamps were handed over, in <c>_localLightOrder</c>'s order.</param>
    /// <param name="cameraProjection">The camera's projection, whose frustum the lamps' reach is tested against.</param>
    /// <remarks>
    /// Lamps whose light reaches into the view take the slots first, nearest first; the rest of the budget
    /// goes to those that do not, nearest first, so turning round finds their maps already drawn. Taken by
    /// distance alone, the lamps inside the rooms the player stood beside held every slot, and a floodlight
    /// over the door across the yard -- the one lighting what was on screen -- marched the distance field
    /// instead and lit the wall through the doorway, where the traced shadow had it dark.
    /// </remarks>
    private void AssignLampMaps(int lightCount, in Matrix4f cameraProjection)
    {
        int slots = Math.Min(_device.LampShadowMapSlots, LampMapSlotsTracked);
        if (!_device.WantsLampShadowMaps || slots <= 0)
        {
            _device.SetLampShadowMapSlots(ReadOnlySpan<int>.Empty);
            return;
        }

        // Which fixtures want maps: those lighting the view, then the rest, nearest first in each, as many
        // as the slots will hold.
        _lampMapWanted.Clear();
        FrustumCulling.Frustum frustum = FrustumCulling.Frustum.FromProjection(cameraProjection);
        int budget = slots;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < lightCount && budget >= CubeFaceAxes.Length; i++)
            {
                DeferredLocalLight light = _localLightScratch[i];
                bool inView = frustum.SphereVisible(
                    light.ViewPosition.X, light.ViewPosition.Y, light.ViewPosition.Z, light.Radius);
                if (inView == (pass == 0))
                {
                    _lampMapWanted.Add(_localLightOrder[i]);
                    budget -= CubeFaceAxes.Length;
                }
            }
        }

        // Slots whose fixture is no longer wanted are freed; the rest keep what they hold.
        for (int slot = 0; slot < slots; slot++)
        {
            int held = _lampMapFixture[slot];
            if (held >= 0 && !_lampMapWanted.Contains(held))
            {
                _lampMapFixture[slot] = -1;
                _lampMapPending[slot] = false;
            }
        }

        // What is wanted and holds nothing yet takes a free run of slots, and waits there for its maps.
        foreach (int fixture in _lampMapWanted)
        {
            if (Array.IndexOf(_lampMapFixture, fixture, 0, slots) >= 0)
            {
                continue;
            }

            LightFixture lamp = _lightFixtures[fixture];
            int needs = CubeFaceAxes.Length;
            int start = FreeLampMapRun(slots, needs);
            if (start < 0)
            {
                continue;
            }

            float reach = Math.Clamp(lamp.Radius * RenderCVars.LocalLightRange.Value, 2f, 60f);
            if (lamp.IsDirectional)
            {
                // A fitting that faces a way is drawn as a cube too, from a few centimetres in front of its
                // glass. A single wide view out of the glass left everything beyond seventy-five degrees off
                // its axis to the distance field, and that field cannot hold the slab a wall lamp is mounted
                // just above: the wall and the ground under every such canopy lit in broad wedges, where the
                // traced shadow had them dark. A cube sees the whole of the lamp's front, out to its own
                // plane; what lies behind the glass is behind every face's near plane and reads as lit, and
                // the lamp's own aim gives it no light.
                Vector3f facing = lamp.Direction.Normalized();
                Vector3d front = new(
                    lamp.Position.X + (facing.X * LampMapStandOff),
                    lamp.Position.Y + (facing.Y * LampMapStandOff),
                    lamp.Position.Z + (facing.Z * LampMapStandOff));
                Matrix4f faceProjection = Matrix4f.CreatePerspective(MathF.PI * 0.5f, 1f, 0.03f, reach);
                for (int face = 0; face < CubeFaceAxes.Length; face++)
                {
                    PlaceLampMap(
                        start + face,
                        fixture,
                        front,
                        ShadowCascades.LightRotationFor(-CubeFaceAxes[face]),
                        faceProjection);
                }
            }
            else
            {
                // From the lamp's centre with a near plane of a few centimetres, as a point light's cube is
                // drawn, and with its own housing in the maps (see ShadowCasterPass.RenderLampMap). A near
                // plane a quarter of a metre out cut away the door frame a recessed lamp sits in, and its
                // light spilled round the frame onto the wall either side.
                const float near = 0.03f;
                Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI * 0.5f, 1f, near, reach);
                for (int face = 0; face < CubeFaceAxes.Length; face++)
                {
                    PlaceLampMap(
                        start + face,
                        fixture,
                        lamp.Position,
                        ShadowCascades.LightRotationFor(-CubeFaceAxes[face]),
                        projection);
                }
            }
        }

        // And the device is told which of this frame's lamps reads which slots -- none while any of them is
        // still waiting to be drawn, since a slot holds the picture from the lamp it last belonged to.
        for (int i = 0; i < lightCount; i++)
        {
            int fixture = _localLightOrder[i];
            int start = Array.IndexOf(_lampMapFixture, fixture, 0, slots);
            int count = CubeFaceAxes.Length;
            bool ready = start >= 0;
            for (int k = 0; ready && k < count; k++)
            {
                ready = start + k < slots && _lampMapFixture[start + k] == fixture && !_lampMapPending[start + k];
            }

            _lampMapSlotPerLight[i] = ready ? start | LampMapCubeBit : -1;
        }

        _device.SetLampShadowMapSlots(_lampMapSlotPerLight.AsSpan(0, lightCount));
    }

    /// <summary>Finds a run of free slots, or -1.</summary>
    private int FreeLampMapRun(int slots, int length)
    {
        for (int start = 0; start + length <= slots; start++)
        {
            bool free = true;
            for (int k = 0; free && k < length; k++)
            {
                free = _lampMapFixture[start + k] < 0;
            }

            if (free)
            {
                return start;
            }
        }

        return -1;
    }

    private void PlaceLampMap(
        int slot, int fixture, Vector3d position, Matrix3f rotation, in Matrix4f projection)
    {
        _lampMapFixture[slot] = fixture;
        _lampMapPending[slot] = true;
        _lampMapPosition[slot] = position;
        _lampMapRotation[slot] = rotation;
        _lampMapProjection[slot] = projection;
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
        int slots = Math.Min(_device.LampShadowMapSlots, LampMapSlotsTracked);
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
