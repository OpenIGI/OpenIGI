using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.ViewModel;

/// <content>Muzzle flashes at a weapon object's muzzle, <c>MuzzleFlash_EnterHandler</c> (<c>igi2 0x4D0E80</c>).</content>
public sealed partial class Igi2ViewModelRenderer
{
    /// <summary>The magic vertex a weapon object's muzzle is, <c>RigidDynCubeObj_GetMagicVertexPos(…, 2, …)</c>.</summary>
    public const int MuzzleMagicVertex = 2;

    /// <summary>World units across the barrel a size of one makes; a first-person weapon's front quad uses <see cref="FirstPersonFrontSpan"/>.</summary>
    public const float FlashSpan = 4096f;

    /// <summary>World units across the barrel a size of one makes for a weapon object drawn in the first-person layer.</summary>
    public const float FirstPersonFrontSpan = 5120f;

    /// <summary>How far behind the muzzle the side quad starts, in world units at a size of one.</summary>
    public const float SideBehind = -2048f;

    /// <summary>How far ahead of the muzzle the side quad reaches, in world units at a size of one.</summary>
    public const float SideAhead = 14336f;

    private static readonly ushort[] FlashIndices = [0, 1, 2, 1, 3, 2];
    private readonly List<MeshHandle> _flashMeshes = [];
    private readonly Vertex3D[] _flashVertices = new Vertex3D[4];
    private readonly List<PendingFlash> _pendingFlashes = [];
    private int _flashMeshCursor;

    /// <summary>Notes a part's flashes to be drawn once the pass's solid models are down.</summary>
    private void QueueFlashes(IReadOnlyList<TitleMuzzleFlash>? flashes, string model, Vector3d position, Matrix3f orientation)
    {
        if (flashes is { Count: > 0 })
        {
            _pendingFlashes.Add(new PendingFlash(flashes, model, position, orientation));
        }
    }

    /// <summary>
    /// Draws the queued flashes over the pass. Each sits at its weapon object's muzzle in the object's frame: the front quad across
    /// the barrel, as strong as the eye looks down it (the squared cosine, times one and a half, less a quarter, held to nought to one),
    /// and the side quad along it, turned about the barrel to face the eye, as strong as the rest. Both add white times the texture,
    /// weighted by the flash's strength.
    /// </summary>
    private void DrawQueuedFlashes(in Camera3D camera, bool firstPerson)
    {
        if (_pendingFlashes.Count == 0 || _textures is null || _models is null)
        {
            _pendingFlashes.Clear();
            return;
        }

        Vector3f ambient = _device.ModelAmbient;
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetVertexSun(Vector3f.Zero);
        _device.SetModelAmbient(Vector3f.One);
        _device.ClearPoseInterpolation();
        foreach (PendingFlash pending in _pendingFlashes)
        {
            if (_models(pending.Model) is not { } model || !model.TryGetMagicVertex(MuzzleMagicVertex, out Vector3f muzzle))
            {
                continue;
            }

            Vector3f offset = pending.Orientation.Transposed().Transform(muzzle);
            Vector3d at = new(pending.Position.X + offset.X, pending.Position.Y + offset.Y, pending.Position.Z + offset.Z);
            Matrix4f modelView = camera.CreateModelView(WorldScene.ToMetres(at), pending.Orientation);

            // The eye's axis in the weapon object's frame; its sign falls out of both the square and the side quad's symmetry.
            Vector3f eye = Normalise((camera.Orientation * pending.Orientation).Transposed().Transform(new Vector3f(0f, 0f, 1f)));
            foreach (TitleMuzzleFlash flash in pending.Flashes)
            {
                DrawFlash(flash, eye, modelView, firstPerson);
            }
        }

        _pendingFlashes.Clear();
        _device.SetModelAmbient(ambient);
        _device.SetDepthState(DepthState.TestAndWrite);
    }

    private void DrawFlash(TitleMuzzleFlash flash, Vector3f eye, in Matrix4f modelView, bool firstPerson)
    {
        float front;
        float side;
        if (flash.SideTexture is null)
        {
            front = 1f;
            side = 0f;
        }
        else
        {
            float along = eye.Y;
            front = Math.Clamp((along * along * 1.5f) - 0.25f, 0f, 1f);
            side = 1f - front;
        }

        const float metres = (float)(1.0 / WorldScene.UnitsPerMetre);
        if (front > 0f)
        {
            float r = flash.Size * (firstPerson ? FirstPersonFrontSpan : FlashSpan) * metres;
            Bgra32 colour = White(front * flash.Intensity);
            _flashVertices[0] = new(new(-r, 0f, -r), default, 0f, 0f, colour: colour);
            _flashVertices[1] = new(new(r, 0f, -r), default, 1f, 0f, colour: colour);
            _flashVertices[2] = new(new(-r, 0f, r), default, 0f, 1f, colour: colour);
            _flashVertices[3] = new(new(r, 0f, r), default, 1f, 1f, colour: colour);
            DrawFlashQuad(flash.FrontTexture, modelView);
        }

        if (side > 0f && flash.SideTexture is { } sideTexture)
        {
            // Across the barrel and the eye, in the object's frame, kept level with the barrel's cross-section.
            float x = eye.Z, z = -eye.X;
            float length = MathF.Sqrt((x * x) + (z * z));
            if (length > 0f)
            {
                x /= length;
                z /= length;
            }

            float w = flash.Size * FlashSpan * metres;
            float back = flash.Size * SideBehind * metres, ahead = flash.Size * SideAhead * metres;
            Bgra32 colour = White(side * flash.Intensity);
            _flashVertices[0] = new(new(-x * w, back, -z * w), default, 0f, 0f, colour: colour);
            _flashVertices[1] = new(new(-x * w, ahead, -z * w), default, 1f, 0f, colour: colour);
            _flashVertices[2] = new(new(x * w, back, z * w), default, 0f, 1f, colour: colour);
            _flashVertices[3] = new(new(x * w, ahead, z * w), default, 1f, 1f, colour: colour);
            DrawFlashQuad(sideTexture, modelView);
        }
    }

    private void DrawFlashQuad(string texture, in Matrix4f modelView)
    {
        TextureHandle handle = _textures!.Resolve(texture);
        if (handle == TextureHandle.None)
        {
            return;
        }

        if (_flashMeshCursor == _flashMeshes.Count)
        {
            _flashMeshes.Add(_device.CreateMeshStorage(4, 6));
        }

        MeshHandle mesh = _flashMeshes[_flashMeshCursor++];
        _device.UploadMeshRange(mesh, 0, _flashVertices, 0, FlashIndices);
        _device.DrawMesh(mesh, 0, 6, modelView, handle, TextureHandle.None, BlendMode.Additive);
    }

    private static Bgra32 White(float alpha) => new(255, 255, 255, (byte)Math.Clamp((int)((alpha * 255f) + 0.5f), 0, 255));

    private static Vector3f Normalise(Vector3f v)
    {
        float length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
        return length > 0f ? new Vector3f(v.X / length, v.Y / length, v.Z / length) : v;
    }

    private sealed record PendingFlash(IReadOnlyList<TitleMuzzleFlash> Flashes, string Model, Vector3d Position, Matrix3f Orientation);
}
