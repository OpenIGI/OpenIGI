using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Draws the soldiers: each body, the weapon in its hand, the stationary and vehicle guns, and their muzzle flames.</summary>
/// <remarks>
/// The order is the original's and is kept: the guns on their stands and the vehicles' guns first, then each
/// soldier's weapon ahead of his body, which carry different previous transforms into the geometry pass.
/// </remarks>
/// <param name="groups">The groups the renderer draws; shared, not copied.</param>
/// <param name="device">The device to draw with.</param>
/// <param name="armoury">What each soldier and gun is armed with.</param>
/// <param name="frames">What resolves a soldier drawn before its frame was.</param>
/// <param name="textures">The textures the models name, loaded on first use.</param>
/// <param name="logger">Where a stationary gun's first draw is reported.</param>
internal sealed class SoldierWeaponDrawer(
    List<SoldierGroup> groups,
    IGraphicsDevice device,
    SoldierArmoury armoury,
    SoldierFrameInterpolator frames,
    Func<TexTextureCache> textures,
    IOpenIgiLogger logger)
{
    private readonly List<SoldierGroup> _groups = groups;

    /// <summary>Gets or sets where the level's light is read from, or none to draw at the textures.</summary>
    public Func<FixedFunctionSun?>? Sun { get; set; }

    // The light in a model's own frame: the orientation takes the model to the world as M * v, so its transpose (the
    // row-vector Transform) takes the world's light back.
    private void LightModel(Matrix3f orientation) =>
        _device.SetVertexSun(Sun?.Invoke() is { } sun ? orientation.Transform(sun.Direction) : Vector3f.Zero);
    private readonly IGraphicsDevice _device = device;
    private readonly SoldierArmoury _armoury = armoury;
    private readonly SoldierFrameInterpolator _frames = frames;
    private readonly Func<TexTextureCache> _textures = textures;
    private readonly IOpenIgiLogger _logger = logger;

    /// <summary>Gets or sets the sector cull the soldiers are drawn by, or <see langword="null"/> for none.</summary>
    public Game.Igi1.World.Igi1SectorVisibility? Visibility { get; set; }

    /// <summary>Gets or sets where the level's detail settings are read from, or none to draw every body whole.</summary>
    public Func<ModelLodPolicy?>? Lod { get; set; }

    /// <summary>Gets the eye the soldiers were last drawn from, which the tick poses their lower levels for.</summary>
    public Camera3D? LastEye { get; private set; }

    /// <summary>Gets that eye's horizontal half-angle tangent.</summary>
    public float LastFieldOfView { get; private set; } = 1f;

    /// <summary>Draws every active soldier's body and held weapon, and the stationary and vehicle guns.</summary>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The current world projection.</param>
    public void Draw(in Camera3D camera, in Matrix4f projection)
    {
        if (_groups.Count == 0 && _armoury.StationaryWeaponVisuals.Count == 0 && _armoury.VehicleGuns is null)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);

        // StationaryGun's holder is a separate authored model. An AI gunner replaces it with the
        // mounted soldier's hand attachment, but the first-person player has no equivalent world
        // attachment, so the holder stays visible while the player owns the task.
        DrawStationaryGuns(camera, projection);
        DrawVehicleGuns(camera, projection);

        // Motion is a geometry-pass concern: only the G-buffer records it, and the previous pose is
        // built against the scene camera, which the shadow cascades' light cameras are not.

        // Narrowed to the outside's window only from the frame's own eye; a light's view of the soldiers has no window.
        Game.Igi1.World.Igi1SectorVisibility? clipping = Visibility is { } eyeCull && eyeCull.IsEye(camera) ? eyeCull : null;

        // Each view picks its own level for each body (0x49F700); the frame's eye is the one the tick poses them for.
        ModelLodPolicy? lod = Lod?.Invoke() is { HasBoneSettings: true } settings ? settings : null;
        float fieldOfView = projection.M11 != 0f ? 1f / projection.M11 : 1f;
        if (Visibility is null || Visibility.IsEye(camera))
        {
            LastEye = camera;
            LastFieldOfView = fieldOfView;
        }

        ViewClip? appliedClip = null;
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                if (!ConditionalContainerGate.IsTaskActive(member.Agent.View.Soldier))
                {
                    continue;
                }

                // A dead soldier keeps being drawn until its HumanAI takes the body out of the world, ten
                // seconds after it lands and only once no render drew it (0x44F410). Each soldier carries its
                // own skinned mesh, so a corpse is posed by its own death clip.
                if (!member.HasFrameState)
                {
                    _frames.ResolveFrameState(member, 1f, smooth: false);
                }

                // Every pass the frame draws a soldier in leaves out the ones the sector cull does, its shadow among them.
                if (Visibility is { } cull && !IsDrawn(cull, group, member))
                {
                    continue;
                }

                // Beyond its cutoff a body is not drawn at all, nor the gun in its hand.
                int level = lod is null ? 0 : SoldierLevelOfDetail.Select(lod, group, member, camera, fieldOfView);
                if (level < 0)
                {
                    continue;
                }

                ViewClip? clip = clipping?.ClipOfObject(WorldScene.ToUnits(member.DrawnWhere));
                if (clip != appliedClip)
                {
                    _device.SetDrawClip(clip);
                    appliedClip = clip;
                }

                Matrix4f modelView = camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation);

                // The pose blend applies to the body's animated mesh in every pass — the shadow pass
                // blends too, or the body would stand a step from its own shadow.
                _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);

                // The gun first, with its OWN previous transform: it is placed by the hand bone, and
                // handing it the body's previous pose would report it sliding by the distance between
                // the two.
                DrawWeapon(member, camera, projection);

                // Matte, and out of the ambient occlusion: see SurfaceMaterial.Body. The occlusion's
                // right repair is a thickness test in the search itself; until that exists, soldiers
                // are left out rather than shaded wrongly.
                _device.SetMaterialOverride(SurfaceMaterial.Body with
                {
                    NoHistory = member.NoHistoryThisFrame,
                });

                LightModel(member.DrawnOrientation);
                if (level > 0 && level == member.Level && member.LevelMesh.IsValid)
                {
                    SoldierLevel lower = group.Lower[level - 1];
                    DrawSubsets(lower.Geometry, lower.Instance, lower.Pack, member.LevelMesh, modelView);
                }
                else
                {
                    DrawSubsets(group, member, modelView);
                }

                _device.SetVertexSun(Vector3f.Zero);

                _device.SetMaterialOverride(SurfaceMaterial.Default);
                _device.ClearPoseInterpolation();
            }
        }

        if (appliedClip is not null)
        {
            _device.SetDrawClip(null);
        }
    }

    /// <summary>Whether the sector cull lets a soldier be drawn from the frame's eye, or there is no cull.</summary>
    public bool IsSectorVisible(SoldierGroup group, SoldierMember member) =>
        Visibility is not { } cull || IsDrawn(cull, group, member);

    // The soldier's own position is what the inside sphere is measured to; its exclusion sphere is its mesh's render sphere,
    // moved by the root bone and turned by the body, as a bone object is excluded.
    private static bool IsDrawn(Game.Igi1.World.Igi1SectorVisibility cull, SoldierGroup group, SoldierMember member)
    {
        Vector3d origin = WorldScene.ToUnits(member.DrawnWhere);
        if (group.Body.Model is not { } model)
        {
            return cull.IsObjectVisible(origin, origin, 0f);
        }

        Vector3f root = model.BoneHierarchy is { BoneCount: > 0 } rig ? rig.Translations[0] : Vector3f.Zero;
        Vector3f turned = member.DrawnOrientation.Transposed().Transform(model.RenderSphereCentre + root);
        return cull.IsObjectVisible(origin, new Vector3d(origin.X + turned.X, origin.Y + turned.Y, origin.Z + turned.Z), model.RenderSphereRadius);
    }

    private void DrawStationaryGuns(in Camera3D camera, in Matrix4f projection)
    {
        foreach ((AiStationaryGun gun, SoldierHeldWeapon? visual) in _armoury.StationaryWeaponVisuals)
        {
            // 0x474190 poses the gun on its stand every tick, whoever is on it (0x474420): an AI gunner holds his own
            // weapon, which 0x4600C0 keeps on his hand, and works the stand's. Another game's gun on the mount draws itself.
            if (visual is null || gun.IsHandedOver)
            {
                continue;
            }

            if (_armoury.FirstDrawOf(gun.TaskId))
            {
                _logger.Information(
                    SoldierRenderer.LogCategory + ".Gunner",
                    $"static visual task={gun.TaskId} weapon={gun.WeaponId} "
                    + $"position=({gun.Position.X:0.##},{gun.Position.Y:0.##},{gun.Position.Z:0.##}) drawn");
            }

            // 0x474420's pose: on the stand's mount point, turned and elevated by the shown aim. The
            // gun's barrel is authored on local +Y and the stand's on local -Y; the π inside
            // GunOrientation is what swings one onto the other.
            if (visual.Presentation is { } presentation)
            {
                presentation.Draw(
                    new BoneAttachment(gun.GunPosition, gun.GunOrientation),
                    camera,
                    projection,
                    firedThisTick: false,
                    shotSequence: 0,
                    WeaponDrawParts.BodyOnly);
            }
            else
            {
                // No multipart presenter is built for the level's weapons, so the gun is drawn the way a
                // soldier's is: as its one rigid mesh. Requiring the presenter here is what left every
                // stand empty.
                DrawRigidWeapon(visual, gun.GunPosition / WorldScene.UnitsPerMetre, gun.GunOrientation, camera);
            }
        }
    }

    private void DrawVehicleGuns(in Camera3D camera, in Matrix4f projection)
    {
        if (_armoury.VehicleGuns is not { } guns)
        {
            return;
        }

        foreach (VehicleGunVisual gun in guns())
        {
            if (!_armoury.TryGetVehicleWeapon(gun.WeaponModel, out SoldierHeldWeapon? visual) || visual is null)
            {
                continue;
            }

            if (visual.Presentation is { } presentation)
            {
                presentation.Draw(
                    new BoneAttachment(gun.PositionUnits, gun.Orientation),
                    camera,
                    projection,
                    firedThisTick: false,
                    shotSequence: 0,
                    WeaponDrawParts.BodyOnly);
            }
            else
            {
                DrawRigidWeapon(visual, gun.PositionUnits / WorldScene.UnitsPerMetre, gun.Orientation, camera);
            }
        }
    }

    /// <summary>Draws the muzzle flames of every gun that fired this tick, in the forward pass.</summary>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The current world projection.</param>
    public void DrawMuzzleFlashes(in Camera3D camera, in Matrix4f projection)
    {
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                if (!member.Agent.FiredThisTick
                    || !member.HasWeaponFrameState
                    || member.Weapon is not { } weapon
                    || weapon.Presentation is not { } presentation
                    || (Visibility is { } cull && !IsDrawn(cull, group, member)))
                {
                    continue;
                }

                presentation.Draw(
                    new BoneAttachment(
                        member.DrawnWeaponOrigin * WorldScene.UnitsPerMetre,
                        member.DrawnWeaponRotation),
                    camera,
                    projection,
                    firedThisTick: true,
                    member.Agent.ShotSequence,
                    WeaponDrawParts.FlameOnly);
            }
        }

        // A gun the player is firing from its stand flames at the same pose DrawStationaryGuns drew.
        foreach ((AiStationaryGun gun, SoldierHeldWeapon? visual) in _armoury.StationaryWeaponVisuals)
        {
            if (gun.IsHandedOver || !gun.FiredThisTick || visual?.Presentation is not { } gunPresentation)
            {
                continue;
            }

            gunPresentation.Draw(
                new BoneAttachment(gun.GunPosition, gun.GunOrientation),
                camera,
                projection,
                firedThisTick: true,
                gun.ShotSequence,
                WeaponDrawParts.FlameOnly);
        }

        // A vehicle's gun flames on the ticks it puts a round out, on the weapon object DrawVehicleGuns drew. Only a
        // weapon model with a gun-flame child has one: a T80's M2HB does, its cannon does not.
        if (_armoury.VehicleGuns is { } vehicleGuns)
        {
            foreach (VehicleGunVisual gun in vehicleGuns())
            {
                if (!gun.FiredThisTick
                    || !_armoury.TryGetVehicleWeapon(gun.WeaponModel, out SoldierHeldWeapon? visual)
                    || visual?.Presentation is not { } vehiclePresentation)
                {
                    continue;
                }

                vehiclePresentation.Draw(
                    new BoneAttachment(gun.PositionUnits, gun.Orientation),
                    camera,
                    projection,
                    firedThisTick: true,
                    gun.ShotSequence,
                    WeaponDrawParts.FlameOnly);
            }
        }
    }

    /// <summary>Draws the weapon a soldier is holding, at its hand bone's own world transform.</summary>
    /// <param name="member">The soldier.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The current world projection.</param>
    /// <remarks>
    /// <para>
    /// The attachment handlers (<c>0x4600C0</c>) do exactly two things: they add the
    /// object's own position to the bone's, and they <b>copy the bone's rotation whole</b>. There is no
    /// per-weapon offset anywhere in the engine, so the model's own origin is where the hand grips it.
    /// </para>
    /// <para>
    /// The transform itself — hand bone through the body's heading, interpolated between ticks — was
    /// resolved by <see cref="SoldierFrameInterpolator.ResolveFrameState"/>. The composed rotation is <c>heading · bone</c>,
    /// applied as <c>M·v</c>, which is the convention
    /// <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/> and the bone palette both use —
    /// <b>not</b> <see cref="Matrix3f.Transform(Vector3f)"/>'s row-vector one, which is its inverse.
    /// </para>
    /// <para>
    /// A gun is rigid, so unlike the body its previous <em>transform</em> reprojects every vertex
    /// exactly; last frame's is reported as its previous pose on the geometry pass.
    /// </para>
    /// </remarks>
    internal void DrawWeapon(SoldierMember member, in Camera3D camera, in Matrix4f projection)
    {
        if (member.Weapon is not { } weapon || !member.HasWeaponFrameState)
        {
            return;
        }

        if (weapon.Presentation is { } presentation)
        {
            presentation.Sun = Sun?.Invoke();
            // Body only: the muzzle flame is LIGHT, and drawing it here put it into the deferred
            // G-buffer to be shaded like scenery -- a one-frame dark square at the muzzle. It is
            // drawn by DrawMuzzleFlashes instead, in the forward pass, like the player's own.
            presentation.Draw(
                new BoneAttachment(
                    member.DrawnWeaponOrigin * WorldScene.UnitsPerMetre,
                    member.DrawnWeaponRotation),
                camera,
                projection,
                firedThisTick: false,
                member.Agent.ShotSequence,
                WeaponDrawParts.BodyOnly);
            return;
        }

        DrawRigidWeapon(weapon, member.DrawnWeaponOrigin, member.DrawnWeaponRotation, camera);
    }

    // A weapon model drawn whole as one rigid mesh, for when no multipart presenter was built for it.
    // The origin is in metres, as CreateModelView takes it.
    private void DrawRigidWeapon(SoldierHeldWeapon weapon, Vector3d originMetres, Matrix3f rotation, in Camera3D camera)
    {
        Matrix4f modelView = camera.CreateModelView(originMetres, rotation);
        LightModel(rotation);

        foreach (MeshSubset subset in weapon.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial
                && weapon.Instance is { } instance
                && weapon.Pack is { } pack
                && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
            {
                diffuse = _textures().Get(texture);
            }

            _device.DrawMesh(
                weapon.Mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                diffuse,
                TextureHandle.None,
                BlendMode.Opaque);
        }

        _device.SetVertexSun(Vector3f.Zero);
    }

    // Every textured subset of a body's own mesh, opaque, through the material the caller set.
    public void DrawSubsets(SoldierGroup group, SoldierMember member, in Matrix4f modelView) =>
        DrawSubsets(group.Geometry, group.Instance, group.Pack, member.Mesh, modelView);

    private void DrawSubsets(MeshGeometry geometry, MtpInstance? named, LoadedPack? from, MeshHandle mesh, in Matrix4f modelView)
    {
        foreach (MeshSubset subset in geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial
                && named is { } instance
                && from is { } pack
                && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
            {
                diffuse = _textures().Get(texture);
            }

            _device.DrawMesh(
                mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                diffuse,
                TextureHandle.None,
                BlendMode.Opaque);
        }
    }
}
