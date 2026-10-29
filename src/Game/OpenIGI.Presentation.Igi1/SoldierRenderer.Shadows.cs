using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <content>
/// Each human's mark on the ground: <c>Human_Create</c> gives every one, the player among them, a <see cref="HumanShadow"/> of
/// <c>shadow1.tex</c> that fades over 4,096,000 units, shown wherever the ground under it was found.
/// </content>
public sealed partial class SoldierRenderer
{
    // A body's origin stands this far above its feet (Human+0x40), and the ground is looked for down from it.
    private const double OriginHeight = 3877.2;

    // How far below the origin an object's collision is looked for: ten metres.
    private const double ShadowProbeDepth = 40960.0;

    private readonly List<TitleGroundDecal> _shadowDecals = [];
    private readonly HumanShadow _shadow = new(HumanShadow.Igi1MaxDistance);
    private GroundDecalDrawer? _shadowDrawer;
    private TexTextureCache? _shadowTextures;
    private ShadowGround _playerShadowGround;

    // Counts the ticks ended, so each human's shadow ground is probed once a tick.
    private long _shadowTick;

    /// <inheritdoc/>
    public void DrawGroundShadows(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _shadowDecals.Clear();
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                if (ConditionalContainerGate.IsTaskActive(member.Agent.View.Soldier) && member.HasFrameState && _drawer.IsSectorVisible(group, member))
                {
                    AddShadow(WorldScene.ToUnits(member.DrawnWhere), ref member.ShadowGround);
                }
            }
        }

        if (_playerBody.FeetUnits is { } feet)
        {
            AddShadow(feet, ref _playerShadowGround);
        }

        if (_shadowDecals.Count == 0)
        {
            return;
        }

        _shadowDrawer ??= new GroundDecalDrawer(_device);
        _shadowTextures ??= new TexTextureCache(
            _device, _resources, _logger, LogCategory, TextureAddressMode.Clamp, static name => [$"LOCAL:common/textures/{name}"]);
        _shadowDrawer.Draw(_shadowDecals, _shadowTextures.Get, camera, projection);
    }

    // Shadow_MagicObjEnterHandler (0x4E09D0): the square lies on the ground found under the origin, nine tenths grey times the
    // fade; its alpha is the texture's alone.
    private void AddShadow(Vector3d feetUnits, ref ShadowGround ground)
    {
        // The ground under the origin, as the body stands on it: the highest of the objects' collision and the terrain.
        // Once a tick, as in the original: its shadow casts nothing itself but reads the ground Human_ProbeGround
        // (0x462390) found on the human's tick -- groundPos, groundDist and groundUp, handed to Shadow_New. The frames
        // between two ticks draw on that ground; a cast every frame was a tenth of a browser's frame.
        Vector3d origin = new(feetUnits.X, feetUnits.Y, feetUnits.Z + OriginHeight);
        if (!ground.Probed || ground.Tick != _shadowTick)
        {
            ground = ProbeShadowGround(origin) with { Probed = true, Tick = _shadowTick };
        }

        if (ground.Height == double.MinValue)
        {
            return;
        }

        double height = ground.Height;
        _shadow.Place(new Vector3d(origin.X, origin.Y, height), ground.Normal, (float)(origin.Z - height));
        byte grey = (byte)Math.Clamp((int)(0.9f * _shadow.Strength * 255f), 0, 255);
        _shadowDecals.Add(new TitleGroundDecal(
            HumanShadow.ShadowTexture, _shadow.Position, _shadow.Normal, HumanShadow.Radius, HumanShadow.Hover, Bgra32.FromRgba(grey, grey, grey, 255)));
    }

    private ShadowGround ProbeShadowGround(Vector3d origin)
    {
        double height = double.MinValue;
        Vector3f normal = new(0f, 0f, 1f);
        if (_sim.Cast is { } cast && cast(origin, new Vector3d(origin.X, origin.Y, origin.Z - ShadowProbeDepth), out WorldRayHit hit))
        {
            height = hit.Position.Z;
            normal = hit.Normal;
        }

        if (_sim.Terrain is { } terrain
            && terrain.TryGetGround(origin.X, origin.Y, origin.Z, out TerrainGround found)
            && found.Height > height)
        {
            height = found.Height;
            normal = new Vector3f((float)found.NormalX, (float)found.NormalY, (float)found.NormalZ);
        }

        return new ShadowGround { Height = height, Normal = normal };
    }

    private void ReleaseShadows()
    {
        _shadowDrawer?.Dispose();
        _shadowDrawer = null;
        _shadowTextures?.Clear();
        _shadowTextures = null;
    }
}

/// <summary>The ground found under a human's shadow on a tick: <see cref="double.MinValue"/> high when there was none.</summary>
internal struct ShadowGround
{
    public bool Probed;
    public long Tick;
    public double Height;
    public Vector3f Normal;
}
