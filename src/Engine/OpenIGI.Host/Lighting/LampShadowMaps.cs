using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Lighting;

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
internal sealed class LampShadowMaps
{
    private readonly IGraphicsDevice _device;

    /// <summary>Initializes a new instance of the <see cref="LampShadowMaps"/> class.</summary>
    /// <param name="device">The device whose lamp-map slots this fills.</param>
    public LampShadowMaps(IGraphicsDevice device) => _device = device ?? throw new ArgumentNullException(nameof(device));

    /// <summary>How many map slots are drawn in a frame for lamps that have just taken them.</summary>
    private const int LampMapsNewPerFrame = 8;

    /// <summary>How many already-drawn slots are redrawn each frame, in turn.</summary>
    private const int LampMapsRefreshedPerFrame = 2;

    /// <summary>How many faces of lamps someone is near are redrawn each frame, before the rest.</summary>
    /// <remarks>
    /// One lamp's cube, taking the lamps in turn. The turn-taking refresh alone came round to any one face every couple of seconds,
    /// so a soldier walking under a lamp left his shadow behind him, and a face drawn a second before its
    /// neighbour cut the shadow in two along the cube's seam.
    /// </remarks>
    private const int LampMapsMovingFacesPerFrame = 6;

    // Where the redraw of lamps with someone near resumes next frame, and the cube starts it walks.
    private int _lampMapMovingCursor;
    private readonly List<int> _lampMapOccupied = [];
    private readonly List<Vector3d> _lampMapPeople = [];

    /// <summary>The least distance in front of a fitting's centre its cube is drawn from, in metres.</summary>
    /// <remarks>
    /// The most is a quarter of the fitting's size, up to twenty centimetres (see <see cref="LampMapView"/>).
    /// </remarks>
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

    // The kept maps (where the device keeps them; see IShadowDevice.CachesLampShadowMaps). Per slot: whether its
    // static casters are drawn and still true, whether its live map has to be made from them this frame, and the
    // lamp and reach its cube was placed for. Per cube, by its first slot: whether a mover was in reach last frame,
    // so the frame it leaves draws the map once more without it.
    private readonly bool[] _lampMapStaticValid = new bool[LampMapSlotsTracked];
    private readonly bool[] _lampMapLiveDue = new bool[LampMapSlotsTracked];
    private readonly bool[] _lampMapHadMovers = new bool[LampMapSlotsTracked];
    private readonly Vector3d[] _lampMapLamp = new Vector3d[LampMapSlotsTracked];
    private readonly double[] _lampMapReach = new double[LampMapSlotsTracked];
    private readonly List<WorldCasterBounds> _casterChanges = [];
    private readonly List<WorldCasterBounds> _movableCasters = [];

    // The fixtures whose light reaches into this frame's view (see Assign). Only theirs are drawn again for what
    // moves: a lamp lighting nothing on screen shows no shadow, and its map catches up the frame it does.
    private readonly HashSet<int> _lampsInView = [];

    /// <summary>How many live faces are drawn again a frame, at most, for what moves in them.</summary>
    /// <remarks>
    /// A cache update budget, as a kept shadow's updates are budgeted: a level under a few dozen lamps with as many
    /// guards and moving parts in their reach has more faces with something moving in them than a frame affords,
    /// so the faces take turns -- a face just made untrue first, then those of lamps lighting the view, then by how
    /// long each has waited. Each costs a copy and the movers alone, so this is twice the faces the whole
    /// redraw it replaced managed, for less: measured on the first mission's guarded yard, 110 fps became 118.
    /// </remarks>
    private const int LampMapsLiveFacesPerFrame = 12;

    // When each face's live map was last drawn, and the faces waiting this frame with their claims.
    private readonly long[] _lampMapLiveFrame = new long[LampMapSlotsTracked];
    private readonly List<(int Slot, long Claim)> _liveCandidates = [];
    private long _liveFrame;
    private int _lampMapRefreshCursor;

    private static int[] CreateEmptySlots()
    {
        int[] slots = new int[LampMapSlotsTracked];
        Array.Fill(slots, -1);
        return slots;
    }

    /// <summary>Gives the frame's nearest lamps their map slots and tells the device which they read.</summary>
    /// <param name="lightCount">How many lamps were handed over.</param>
    /// <param name="cameraProjection">The camera's projection, whose frustum the lamps' reach is tested against.</param>
    /// <param name="lights">The frame's lamps, in view space, in light order.</param>
    /// <param name="order">For each of the frame's lamps, the fixture it is.</param>
    /// <param name="fixtures">The level's fixtures.</param>
    /// <remarks>
    /// Lamps whose light reaches into the view take the slots first, nearest first; the rest of the budget
    /// goes to those that do not, nearest first, so turning round finds their maps already drawn. Taken by
    /// distance alone, the lamps inside the rooms the player stood beside held every slot, and a floodlight
    /// over the door across the yard -- the one lighting what was on screen -- marched the distance field
    /// instead and lit the wall through the doorway, where the traced shadow had it dark.
    /// </remarks>
    public void Assign(
        int lightCount,
        in Matrix4f cameraProjection,
        ReadOnlySpan<DeferredLocalLight> lights,
        ReadOnlySpan<int> order,
        IReadOnlyList<LightFixture> fixtures)
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
        _lampsInView.Clear();
        FrustumCulling.Frustum frustum = FrustumCulling.Frustum.FromProjection(cameraProjection);
        int budget = slots;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < lightCount && budget >= CubeFaceAxes.Length; i++)
            {
                DeferredLocalLight light = lights[i];
                bool inView = frustum.SphereVisible(
                    light.ViewPosition.X, light.ViewPosition.Y, light.ViewPosition.Z, light.Radius);
                if (inView)
                {
                    _lampsInView.Add(order[i]);
                }

                if (inView == (pass == 0))
                {
                    _lampMapWanted.Add(order[i]);
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
                _lampMapStaticValid[slot] = false;
                _lampMapLiveDue[slot] = false;
                _lampMapHadMovers[slot] = false;
            }
        }

        // What is wanted and holds nothing yet takes a free run of slots, and waits there for its maps.
        foreach (int fixture in _lampMapWanted)
        {
            if (Array.IndexOf(_lampMapFixture, fixture, 0, slots) >= 0)
            {
                continue;
            }

            LightFixture lamp = fixtures[fixture];
            int needs = CubeFaceAxes.Length;
            int start = FreeLampMapRun(slots, needs);
            if (start < 0)
            {
                continue;
            }

            (Vector3d from, Matrix4f projection) = LampMapView(lamp);
            double reach = LampReach(lamp);
            for (int face = 0; face < CubeFaceAxes.Length; face++)
            {
                PlaceLampMap(
                    start + face, fixture, from, ShadowCascades.LightRotationFor(-CubeFaceAxes[face]), projection);
                _lampMapLamp[start + face] = lamp.Position;
                _lampMapReach[start + face] = reach;
            }
        }

        // And the device is told which of this frame's lamps reads which slots -- none while any of them is
        // still waiting to be drawn, since a slot holds the picture from the lamp it last belonged to.
        for (int i = 0; i < lightCount; i++)
        {
            int fixture = order[i];
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

    /// <summary>Where a lamp's cube of maps is drawn from, and the projection each of its faces looks through.</summary>
    /// <param name="lamp">The fixture.</param>
    /// <returns>The camera's absolute position and the faces' shared projection.</returns>
    /// <remarks>
    /// <para>
    /// A fitting that faces a way is drawn from a few centimetres in front of its glass. A single wide view
    /// out of the glass left everything beyond seventy-five degrees off its axis to the distance field, and
    /// that field cannot hold the slab a wall lamp is mounted just above: the wall and the ground under every
    /// such canopy lit in broad wedges, where the traced shadow had them dark. A cube sees the whole of the
    /// lamp's front, out to its own plane; what lies behind the glass is behind every face's near plane and
    /// reads as lit, and the lamp's own aim gives it no light.
    /// </para>
    /// <para>
    /// One that does not is drawn from its centre, as a point light's cube is, with its near plane at three
    /// quarters of the fitting's size (five to thirty centimetres; five against a measured face). Nearer is
    /// the fitting's housing, which surrounds its centre: at three centimetres every face of the cube saw the
    /// box the bulb sits in, and a wall lamp shadowed the whole wall and the yard in front of it where the
    /// traced lamp lit them; at half its size the box's sides still stood inside the cube and cut a dark
    /// bow-tie along the wall either side of every lamp. Further loses the wall a room's lamp hangs a hand's
    /// breadth from: at the whole fitting's size, forty centimetres, a lamp thirty-five from the wall did not
    /// see it, and lit the air and the ground on its far side.
    /// </para>
    /// </remarks>
    private static (Vector3d From, Matrix4f Projection) LampMapView(LightFixture lamp)
    {
        float reach = Math.Clamp(lamp.Radius * LocalLightCVars.LocalLightRange.Value, 2f, 60f);
        if (!lamp.IsDirectional)
        {
            float housing = lamp.HasRect ? 0.05f : Math.Clamp(MathF.Max(lamp.Extent, 0.05f) * 0.75f, 0.05f, 0.3f);
            return (lamp.Position, Matrix4f.CreatePerspective(MathF.PI * 0.5f, 1f, housing, reach));
        }

        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI * 0.5f, 1f, 0.03f, reach);

        // Out past the fitting's own dome: six centimetres is in front of a flat batten's glass, but a round
        // canopy light seventy centimetres across hangs its diffuser well below its centre, and a cube drawn
        // from inside that saw the diffuser on every face -- the canopy floor under four such lamps read as
        // wholly shadowed where the traced lamps lit it.
        Vector3f facing = lamp.Direction.Normalized();
        double standOff = Math.Clamp(lamp.Extent * 0.25, LampMapStandOff, 0.2);
        Vector3d front = new(
            lamp.Position.X + (facing.X * standOff),
            lamp.Position.Y + (facing.Y * standOff),
            lamp.Position.Z + (facing.Z * standOff));
        return (front, projection);
    }

    /// <summary>Finds the cubes whose lamp has a living soldier or the player within its reach.</summary>
    /// <param name="slots">How many slots the device holds.</param>
    /// <param name="fixtures">The level's fixtures.</param>
    /// <param name="people">Adds where everyone who casts stands, in metres.</param>
    private void CollectOccupiedLampMaps(int slots, IReadOnlyList<LightFixture> fixtures, Action<List<Vector3d>> people)
    {
        _lampMapOccupied.Clear();
        _lampMapPeople.Clear();
        people(_lampMapPeople);
        if (_lampMapPeople.Count == 0)
        {
            return;
        }

        for (int start = 0; start + CubeFaceAxes.Length <= slots; start += CubeFaceAxes.Length)
        {
            int fixture = _lampMapFixture[start];
            if (fixture < 0 || fixture >= fixtures.Count)
            {
                continue;
            }

            LightFixture lamp = fixtures[fixture];
            double reach = Math.Clamp(lamp.Radius * LocalLightCVars.LocalLightRange.Value, 2f, 60f) + 1.0;
            foreach (Vector3d person in _lampMapPeople)
            {
                double dx = person.X - lamp.Position.X;
                double dy = person.Y - lamp.Position.Y;
                double dz = person.Z - lamp.Position.Z;
                if ((dx * dx) + (dy * dy) + (dz * dz) < reach * reach)
                {
                    _lampMapOccupied.Add(start);
                    break;
                }
            }
        }
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
        _lampMapStaticValid[slot] = false;
        _lampMapLiveDue[slot] = false;
        _lampMapPosition[slot] = position;
        _lampMapRotation[slot] = rotation;
        _lampMapProjection[slot] = projection;
    }

    /// <summary>Draws the maps that are waiting, and redraws a couple of the rest in turn.</summary>
    /// <param name="pass">The caster pass, which draws through the title's presenters.</param>
    /// <param name="scene">The world's placed objects.</param>
    /// <param name="world">The presenter that draws them.</param>
    /// <param name="characters">The character presenter.</param>
    /// <param name="fixtures">The level's fixtures.</param>
    /// <param name="people">Adds where everyone who casts stands, in metres, for the lamps near them to be redrawn first.</param>
    /// <remarks>Before the scene capture opens, beside the cascades.</remarks>
    public void Render(
        ShadowCasterPass pass,
        WorldScene? scene,
        IWorldPresenter? world,
        ICharacterPresenter? characters,
        IReadOnlyList<LightFixture> fixtures,
        Action<List<Vector3d>> people)
    {
        int slots = Math.Min(_device.LampShadowMapSlots, LampMapSlotsTracked);
        if (!_device.WantsLampShadowMaps || slots <= 0)
        {
            return;
        }

        if (_device.CachesLampShadowMaps)
        {
            RenderKept(slots, pass, scene, world, characters, fixtures, people);
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

        // Lamps someone stands or walks near: their whole cube first, so the shadow moves with the body.
        CollectOccupiedLampMaps(slots, fixtures, people);
        int movingDrawn = 0;
        for (int k = 0; k < _lampMapOccupied.Count && movingDrawn < LampMapsMovingFacesPerFrame; k++)
        {
            int start = _lampMapOccupied[(_lampMapMovingCursor + k) % _lampMapOccupied.Count];
            for (int face = 0; face < CubeFaceAxes.Length && movingDrawn < LampMapsMovingFacesPerFrame; face++)
            {
                if (!_lampMapPending[start + face] && DrawLampMap(start + face))
                {
                    movingDrawn++;
                }
            }
        }

        if (_lampMapOccupied.Count > 0)
        {
            _lampMapMovingCursor = (_lampMapMovingCursor + 1) % _lampMapOccupied.Count;
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

    /// <summary>Keeps every lamp's static casters apart from its movers, and redraws only what changed.</summary>
    /// <remarks>
    /// <para>
    /// A cube is drawn twice over, as two layers. Its static casters -- every placement that has never moved -- are
    /// drawn into a map of its own and kept until something in the lamp's reach changes: a placement there begins
    /// to move, appears, goes, or swaps its model. Each frame something that moves stands in the lamp's reach, the
    /// live map is made again from a copy of the kept one with the movers drawn over it, which is the map a whole
    /// draw makes, because depth keeps the nearer of the two. A lamp with nothing moving near it is not drawn at all.
    /// </para>
    /// <para>
    /// What it replaces redrew a couple of whole cubes a frame in turn and a lamp's cube a face at a time while
    /// someone stood under it -- the whole room each time, for the one body moving in it, and a soldier's shadow
    /// a few frames behind him. Here the room is drawn when it changes and the bodies every frame.
    /// </para>
    /// <para>
    /// Kept maps are redrawn a few faces a frame, nearest-placed first, so a level coming up or a blast that
    /// changes many rooms spreads over frames; until a face's kept map is redrawn, its live map stands as it was.
    /// </para>
    /// </remarks>
    private void RenderKept(
        int slots,
        ShadowCasterPass pass,
        WorldScene? scene,
        IWorldPresenter? world,
        ICharacterPresenter? characters,
        IReadOnlyList<LightFixture> fixtures,
        Action<List<Vector3d>> people)
    {
        // What changed among the static casters, and which kept maps that makes untrue.
        _casterChanges.Clear();
        bool reset = scene is null || world is null || world.TakeStaticCasterChanges(scene, _casterChanges);
        for (int start = 0; start + CubeFaceAxes.Length <= slots; start += CubeFaceAxes.Length)
        {
            if (_lampMapFixture[start] < 0)
            {
                continue;
            }

            if (reset || AnyWithinReach(start, _casterChanges))
            {
                for (int face = 0; face < CubeFaceAxes.Length; face++)
                {
                    _lampMapStaticValid[start + face] = false;
                }
            }
        }

        // The kept maps that are untrue or not drawn yet, a few faces a frame.
        int keptDrawn = 0;
        for (int slot = 0; slot < slots && keptDrawn < LampMapsNewPerFrame; slot++)
        {
            if (_lampMapFixture[slot] >= 0 && !_lampMapStaticValid[slot] && DrawLayer(slot, staticCasters: true))
            {
                _lampMapStaticValid[slot] = true;
                _lampMapLiveDue[slot] = true;
                keptDrawn++;
            }
        }

        // And the live maps. A face is drawn again where something that moves stands inside it, stood there last
        // frame, or its kept map was just redrawn -- its subjects gathered per face, as a shadow's are gathered per
        // frustum: a soldier under a lamp stands in one or two of its six faces, and the other four keep their map.
        // And only for a lamp whose light reaches into the view, as only the shadows of lights that affect the view
        // are drawn at all: one lighting nothing on screen keeps the map it has until it does.
        _lampMapPeople.Clear();
        people(_lampMapPeople);
        _movableCasters.Clear();
        if (scene is not null && world is not null)
        {
            world.CollectMovableCasters(scene, _movableCasters);
        }

        foreach (Vector3d person in _lampMapPeople)
        {
            _movableCasters.Add(new WorldCasterBounds(person, PersonCasterRadius));
        }

        _liveFrame++;
        _liveCandidates.Clear();
        for (int start = 0; start + CubeFaceAxes.Length <= slots; start += CubeFaceAxes.Length)
        {
            if (_lampMapFixture[start] < 0)
            {
                continue;
            }

            bool inView = _lampsInView.Contains(_lampMapFixture[start]);
            bool cubeHasMovers = AnyWithinReach(start, _movableCasters);
            for (int face = 0; face < CubeFaceAxes.Length; face++)
            {
                int slot = start + face;
                bool movers = cubeHasMovers && AnyInFace(slot, _movableCasters);
                if (_lampMapStaticValid[slot] && (_lampMapLiveDue[slot] || movers || _lampMapHadMovers[slot]))
                {
                    // The claim: a face that has no live map from its kept one yet, then a lamp lighting the view, then
                    // how many frames it has waited.
                    long waited = _liveFrame - _lampMapLiveFrame[slot];
                    long claim = (_lampMapLiveDue[slot] ? 1L << 40 : 0L) + (inView ? 1L << 20 : 0L) + waited;
                    _liveCandidates.Add((slot, claim));
                }

                // A face that had movers keeps its claim until it is drawn without them.
                _lampMapHadMovers[slot] = movers || (_lampMapHadMovers[slot] && !_lampMapLiveDue[slot]);
            }
        }

        _liveCandidates.Sort(static (left, right) => right.Claim.CompareTo(left.Claim));
        int liveDrawn = 0;
        foreach ((int slot, long _) in _liveCandidates)
        {
            if (liveDrawn >= LampMapsLiveFacesPerFrame)
            {
                break;
            }

            if (DrawLayer(slot, staticCasters: false))
            {
                _lampMapLiveDue[slot] = false;
                _lampMapPending[slot] = false;
                _lampMapLiveFrame[slot] = _liveFrame;
                _lampMapHadMovers[slot] = AnyInFace(slot, _movableCasters) && AnyWithinReach(slot - (slot % CubeFaceAxes.Length), _movableCasters);
                liveDrawn++;
            }
        }


        bool DrawLayer(int slot, bool staticCasters) => pass.RenderLampMapLayer(
            slot,
            _lampMapPosition[slot],
            _lampMapRotation[slot],
            _lampMapProjection[slot],
            scene,
            world,
            characters,
            staticCasters);
    }

    // Whether any of the spheres reaches into the lamp's reach: a caster past it is past every face's far plane.
    private bool AnyWithinReach(int start, List<WorldCasterBounds> spheres)
    {
        Vector3d lamp = _lampMapLamp[start];
        double reach = _lampMapReach[start];
        foreach (WorldCasterBounds sphere in spheres)
        {
            double dx = sphere.Centre.X - lamp.X;
            double dy = sphere.Centre.Y - lamp.Y;
            double dz = sphere.Centre.Z - lamp.Z;
            double within = reach + sphere.Radius;
            if ((dx * dx) + (dy * dy) + (dz * dz) < within * within)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How far a person's shadow can reach from where they stand, in metres: a body, a raised arm, a gun.</summary>
    private const double PersonCasterRadius = 2.5;

    // Whether any of the spheres stands inside one face: within its reach and inside its four side planes.
    private bool AnyInFace(int slot, List<WorldCasterBounds> spheres)
    {
        Camera3D face = new(_lampMapPosition[slot], _lampMapRotation[slot]);
        FrustumCulling.Frustum frustum = FrustumCulling.Frustum.FromProjection(_lampMapProjection[slot]);
        foreach (WorldCasterBounds sphere in spheres)
        {
            if (frustum.SphereWithinSides(face.CreateModelView(sphere.Centre), new BoundingSphere(Vector3f.Zero, (float)sphere.Radius)))
            {
                return true;
            }
        }

        return false;
    }

    // How far a lamp's maps see: its faces' far plane, as LampMapView sets it, with a metre for the stand-off.
    private static double LampReach(LightFixture lamp) =>
        Math.Clamp(lamp.Radius * LocalLightCVars.LocalLightRange.Value, 2f, 60f) + 1.0;

    // The surface cache's coarse lamp maps: six small faces for every lamp in the level. Drawn a few faces a frame
    // after a level loads, nearest lamps first, and kept: they hold the static world, which draws the same picture
    // every time. The relight reads a lamp's six once all six are drawn and marches the distance field for it until
    // then (see IGraphicsDevice.LampBounceMapSlots).
    /// <summary>How many coarse faces are drawn a frame until every lamp has its six.</summary>
    /// <remarks>Sixty-four texels a side each: a dozen cost less than one of the shadow maps' new faces.</remarks>
    private const int LampBounceFacesPerFrame = 12;

    // Per fixture: how many of its six faces are drawn, and what the relight is told (the first face, or -1).
    private int[] _lampBounceFaces = [];
    private int[] _lampBounceEntries = [];
    private int[] _lampBounceOrder = [];
    private IReadOnlyList<LightFixture>? _lampBounceFixtures;

    // The device's count as of the last face drawn here; anything else means it let the maps go.
    private int _lampBounceVersion = -1;

    /// <summary>Draws the next few coarse faces, and tells the relight which lamps have all six.</summary>
    /// <param name="pass">The caster pass, which draws through the title's presenters.</param>
    /// <param name="scene">The world's placed objects.</param>
    /// <param name="world">The presenter that draws them.</param>
    /// <param name="cardLighting">The surface cache's relight, when it is on.</param>
    /// <param name="fixtures">The level's fixtures; another list than last time is a new level.</param>
    /// <param name="eye">Where the camera is, which orders a new level's lamps nearest first.</param>
    public void RenderBounce(
        ShadowCasterPass pass,
        WorldScene? scene,
        IWorldPresenter? world,
        SurfaceCacheLighting? cardLighting,
        IReadOnlyList<LightFixture> fixtures,
        Vector3d eye)
    {
        int slots = _device.LampBounceMapSlots;
        if (slots <= 0 || cardLighting is not { } cards)
        {
            return;
        }

        // A new level, or the device let the maps go (a settings change rebuilds the shadow resources):
        // everything is drawn again.
        if (!ReferenceEquals(_lampBounceFixtures, fixtures)
            || _lampBounceFaces.Length != fixtures.Count
            || _device.LampBounceMapVersion != _lampBounceVersion)
        {
            ResetLampBounceMaps(fixtures, eye);
        }

        int lamps = Math.Min(fixtures.Count, slots / CubeFaceAxes.Length);
        int drawn = 0;
        for (int k = 0; k < lamps && drawn < LampBounceFacesPerFrame; k++)
        {
            int fixture = _lampBounceOrder[k];
            while (_lampBounceFaces[fixture] < CubeFaceAxes.Length && drawn < LampBounceFacesPerFrame)
            {
                int face = _lampBounceFaces[fixture];
                (Vector3d from, Matrix4f projection) = LampMapView(fixtures[fixture]);
                if (!pass.RenderLampMap(
                    (k * CubeFaceAxes.Length) + face,
                    from,
                    ShadowCascades.LightRotationFor(-CubeFaceAxes[face]),
                    projection,
                    scene,
                    world,
                    characters: null,
                    coarse: true))
                {
                    return;
                }

                _lampBounceVersion = _device.LampBounceMapVersion;
                _lampBounceFaces[fixture]++;
                drawn++;
                if (_lampBounceFaces[fixture] == CubeFaceAxes.Length)
                {
                    _lampBounceEntries[fixture] = k * CubeFaceAxes.Length;
                }
            }
        }

        cards.SetLampMapEntries(_lampBounceEntries);
    }

    // Each of the frame's lamps' coarse cube, in light order, for the device.
    private readonly int[] _lampCoarsePerLight = new int[DeferredLocalLight.Maximum];

    /// <summary>Tells the device which coarse cube each of this frame's lamps has.</summary>
    /// <param name="lightCount">How many lamps the frame was given.</param>
    /// <param name="order">For each of the frame's lamps, the fixture it is.</param>
    /// <remarks>
    /// For the lamp shadows of the lamps the fine maps do not reach: past the fine maps' budget a lamp was
    /// answered by the distance field alone, which at a distance cannot hold the wall it stands behind.
    /// </remarks>
    public void AssignCoarse(int lightCount, ReadOnlySpan<int> order)
    {
        int count = Math.Min(lightCount, _lampCoarsePerLight.Length);
        for (int i = 0; i < count; i++)
        {
            int fixture = order[i];
            _lampCoarsePerLight[i] = (uint)fixture < (uint)_lampBounceEntries.Length ? _lampBounceEntries[fixture] : -1;
        }

        _device.SetLampCoarseMapSlots(_lampCoarsePerLight.AsSpan(0, count));
    }

    /// <summary>Forgets every coarse face and orders the lamps nearest the camera first.</summary>
    private void ResetLampBounceMaps(IReadOnlyList<LightFixture> fixtures, Vector3d eye)
    {
        int count = fixtures.Count;
        _lampBounceFixtures = fixtures;
        _lampBounceFaces = new int[count];
        _lampBounceEntries = new int[count];
        Array.Fill(_lampBounceEntries, -1);
        _lampBounceVersion = _device.LampBounceMapVersion;

        // Nearest first, so the lamps around the player are right within a few frames of the load; the
        // order is fixed then, since a lamp's slots are where it sits in it.
        _lampBounceOrder = [.. Enumerable.Range(0, count).OrderBy(i =>
        {
            Vector3d at = fixtures[i].Position;
            double dx = at.X - eye.X;
            double dy = at.Y - eye.Y;
            double dz = at.Z - eye.Z;
            return (dx * dx) + (dy * dy) + (dz * dz);
        })];
    }
}
