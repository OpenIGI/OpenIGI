using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using Group = OpenIGI.Presentation.Igi1.SoldierGroup;
using HeldWeapon = OpenIGI.Presentation.Igi1.SoldierHeldWeapon;
using Member = OpenIGI.Presentation.Igi1.SoldierMember;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Builds what the soldiers are drawn with: each group's mesh and model, each soldier's own skinned mesh, and the weapons they hold.</summary>
/// <param name="device">The device the meshes are uploaded to.</param>
/// <param name="logger">Where a model that does not resolve or upload is reported.</param>
/// <param name="magic">The retail multipart-object table a held weapon's presentation needs, or <see langword="null"/>.</param>
/// <param name="heldWeapons">Makes a held weapon's presentation, or <see langword="null"/> for weapons drawn rigid.</param>
internal sealed class SoldierMeshBuilder(
    IGraphicsDevice device,
    IOpenIgiLogger logger,
    MagicObjectRegistry? magic,
    Func<IHeldWeaponPresenter>? heldWeapons)
{
    private readonly IGraphicsDevice _device = device;
    private readonly IOpenIgiLogger _logger = logger;
    private readonly MagicObjectRegistry? _magic = magic;
    private readonly Func<IHeldWeaponPresenter>? _heldWeapons = heldWeapons;

    // Every weapon built, which go back together.
    private readonly List<HeldWeapon> _weapons = [];

    /// <summary>Makes a group's drawing: its stand-pose mesh, the pack instance its textures come from, and its lower levels.</summary>
    /// <param name="mission">The packs the model came from.</param>
    /// <param name="loaded">The group, as the simulation stood it.</param>
    /// <returns>The drawing, or <see langword="null"/> when the mesh does not upload: the soldiers run, but are not drawn.</returns>
    public Group? BuildGroup(LoadedMission mission, SoldierLoadedGroup loaded)
    {
        MeshHandle mesh;
        try
        {
            mesh = _device.CreateMesh(loaded.StandSkin, loaded.Geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(SoldierRenderer.LogCategory, $"'{loaded.Model}' did not upload: {ex.Message}");
            return null;
        }

        (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, loaded.Model);
        Group group = new(loaded.Roster, loaded.Geometry, mesh, instance, pack);
        if (loaded.Roster.Body.Model is { } model)
        {
            group.Lower = SoldierLevelOfDetail.FindLower(mission, loaded.Model, model, out int[] triangles);
            group.Triangles = triangles;
        }

        return group;
    }

    /// <summary>Makes a soldier's own drawing: its first skin, uploaded to both of its GPU streams.</summary>
    /// <param name="group">The soldier's group, whose geometry and model it shares.</param>
    /// <param name="agent">The soldier.</param>
    /// <returns>The member, not yet added to the group.</returns>
    /// <exception cref="InvalidOperationException">The device could not upload the mesh.</exception>
    public Member NewMember(Group group, SoldierAgent agent)
    {
        // Its own mesh, so it can hold its own pose (see Member) -- animated, so each tick's
        // re-skin replaces the vertices in place and the pose replaced becomes the previous-pose
        // stream the motion vectors and the between-tick blending read.
        BonePose initialPose = agent.View.UnrotatedPose();
        Vertex3D[] initialSkin = new Vertex3D[group.Geometry.Vertices.Length];
        group.Geometry.Vertices.CopyTo(initialSkin, 0);
        bool initialSkinReady = false;
        if (group.Body.Model is { } skinModel)
        {
            try
            {
                MefSkinner.Skin(
                    skinModel,
                    initialPose,
                    (float)(1.0 / WorldScene.UnitsPerMetre),
                    initialSkin);
                initialSkinReady = true;
            }
            catch (ArgumentException)
            {
                // Keep the raw mesh if a malformed model cannot be skinned. Its collision path falls
                // back to the authoritative pose until two valid animated uploads establish a pair.
                initialSkinReady = false;
            }
        }

        // Both GPU streams begin with this same skin. A smoothed player shot can therefore use
        // a matching CPU pose pair even on the very first rendered frame; seeding them with raw
        // geometry would make the displayed blend untraceable until a second animation tick.
        MeshHandle own = _device.CreateAnimatedMesh(initialSkin, group.Geometry.Indices);
        return new Member(agent, own)
        {
            DrawnPose = initialPose,
            SkinScratch = initialSkin,
            SkinnedPose = initialSkinReady ? initialPose : null,
            PreviousSkinnedPose = initialSkinReady ? initialPose : null,
        };
    }

    public HeldWeapon? BuildWeapon(LoadedMission mission, string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return null;
        }

        if (!mission.TryGetModel(modelName, out MefModel? model) || model is null)
        {
            _logger.Warning(SoldierRenderer.LogCategory, $"Weapon model '{modelName}' is in no pack this mission opened.");
            return null;
        }

        MeshGeometry? geometry = MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre));
        if (geometry is null)
        {
            _logger.Warning(SoldierRenderer.LogCategory, $"Weapon model '{modelName}' has nothing to draw.");
            return null;
        }

        MeshHandle mesh;
        try
        {
            mesh = _device.CreateMesh(geometry.Vertices, geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
        {
            _logger.Warning(SoldierRenderer.LogCategory, $"Weapon model '{modelName}' did not upload: {ex.Message}");
            return null;
        }

        (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, modelName);
        IHeldWeaponPresenter? presentation = null;
        if (_magic is not null && _heldWeapons?.Invoke() is { } candidate)
        {
            if (candidate.Load(mission, modelName))
            {
                presentation = candidate;
            }
            else
            {
                candidate.Dispose();
            }
        }

        HeldWeapon held = new(geometry, mesh, instance, pack, presentation);
        _weapons.Add(held);
        return held;
    }

    public static (LoadedPack? Pack, MtpInstance? Instance) FindInstance(LoadedMission mission, string name) =>
        mission.TryFindInstance(name, out LoadedPack? pack, out MtpInstance instance) ? (pack, instance) : (null, null);

    /// <summary>Frees every weapon built, and its presentation.</summary>
    public void ReleaseWeapons()
    {
        foreach (HeldWeapon weapon in _weapons)
        {
            weapon.Presentation?.Dispose();
            if (weapon.Mesh.IsValid)
            {
                _device.DestroyMesh(weapon.Mesh);
            }
        }

        _weapons.Clear();
    }
}
