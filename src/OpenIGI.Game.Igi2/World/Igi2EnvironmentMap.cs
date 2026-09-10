using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// One face of a baked environment cube map, named by the direction it looks along in the engine's own
/// axes.
/// </summary>
/// <remarks>
/// <para>
/// The numeric value is three things at once, and that is not a coincidence: it is the number in the
/// face's filename, the index of the file in the array handed to <c>Envmap_LoadFromMemory</c>
/// (<c>igi2ed 0x581670</c>), and the <c>D3DCUBEMAP_FACES</c> slot that function loads it into — it loops
/// <c>i = 0..5</c> calling <c>GetCubeMapSurface(i, 0, …)</c> with <c>ppxRawData[i]</c>, so there is no
/// remapping anywhere.
/// </para>
/// <para>
/// The names here are the <b>engine's</b> axes (X east, Y north, Z up), which are not Direct3D's. The
/// two are related by the swizzle the water vertex shader applies to the reflection vector before
/// sampling — see <see cref="Igi2EnvironmentMap.CubeDirection"/> — and that swizzle is exactly what
/// makes <see cref="PositiveZ"/> land in the <c>POSITIVE_Y</c> slot and <see cref="PositiveY"/> in
/// <c>POSITIVE_Z</c>. Both halves of that were read separately and agree: the bake in
/// <c>Envmap_CubeMapRender</c> (<c>igi2ed 0x581930</c>) points its camera at exactly these six
/// directions, in exactly this order.
/// </para>
/// </remarks>
public enum Igi2CubeFace
{
    /// <summary>Looking along engine +X. <c>Envmap_CubeMapRender</c> case 0; <c>D3DCUBEMAP_FACE_POSITIVE_X</c>.</summary>
    PositiveX = 0,

    /// <summary>Looking along engine −X. Case 1; <c>D3DCUBEMAP_FACE_NEGATIVE_X</c>.</summary>
    NegativeX = 1,

    /// <summary>Looking straight up, along engine +Z. Case 2; <c>D3DCUBEMAP_FACE_POSITIVE_Y</c>.</summary>
    PositiveZ = 2,

    /// <summary>Looking straight down, along engine −Z. Case 3; <c>D3DCUBEMAP_FACE_NEGATIVE_Y</c>.</summary>
    NegativeZ = 3,

    /// <summary>Looking along engine +Y. Case 4; <c>D3DCUBEMAP_FACE_POSITIVE_Z</c>.</summary>
    PositiveY = 4,

    /// <summary>Looking along engine −Y. Case 5; <c>D3DCUBEMAP_FACE_NEGATIVE_Z</c>.</summary>
    NegativeY = 5,
}

/// <summary>
/// One of a water sheet's baked environment cube maps: six square faces, decoded and ready to become a
/// cube texture.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it holds, and what it hands over.</b> The six faces as <see cref="Bgra32"/>, in the order a
/// cube sampler wants them, with the sampling rule written down — and <see cref="Upload"/>, which turns
/// them into a device cube map. When this type was first written the device had no cube entry point at
/// all and this remark listed what one would need; three of the four items are now
/// <c>CreateCubeTexture</c>, <c>UpdateCubeFace</c> and <c>SetEnvironmentCube</c>. The fourth, a draw
/// path with three-component texture coordinates, is not needed after all: I.G.I. 2's water is drawn
/// camera-relative, so a reflection direction can be built in the vertex stage out of the position and
/// normal the mesh already carries.
/// </para>
/// <para>
/// <b>Only water samples these.</b> <c>Envmap_LoadFromMemory</c> has exactly one caller,
/// <c>Water_LoadEnvmapData</c> (<c>igi2ed 0x646810</c>), and <c>Envmap_Create</c> (<c>0x5817C0</c>) has
/// exactly one, the editor's water rebake. The <c>*_DrawEnvmapHandler</c> and
/// <c>*_EnumerateEnvmapHandler</c> pairs on terrain, forests, the flat sky, rigid objects and spline
/// objects point the other way: they are what gets drawn <em>into</em> a map while it is baked.
/// <c>Terrain_DrawEnvmapHandler</c> (<c>0x632E10</c>) is a one-line forward to
/// <c>Terrain_EnterHandler</c>, which settles it.
/// </para>
/// <para>
/// <b>And only above quality zero.</b> <c>Water_LoadEnvmapData</c> returns before reading anything unless
/// <c>WaterRender_GetQualityLevel() &gt; 0</c>, and <c>WaterRender_Draw</c> (<c>0x577950</c>) binds the
/// cube to <em>stage 0</em> in that case; at quality zero it binds the sheet's two ordinary 2D textures
/// to stages 0 and 1 instead and the reflection is the flat <c>Envmap</c> image. So a port with no cube
/// support is not rendering something wrong — it is rendering the original's low quality setting.
/// </para>
/// </remarks>
public sealed class Igi2EnvironmentMap
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.envmap";

    /// <summary>How many faces a cube map has.</summary>
    public const int FaceCount = 6;

    /// <summary>The directory, under a level, that holds the archives the faces ship in.</summary>
    public const string ArchiveDirectory = "envmaps";

    private readonly Bgra32[][] _faces;

    private Igi2EnvironmentMap(int taskId, int index, int edge, Bgra32[][] faces)
    {
        TaskId = taskId;
        Index = index;
        Edge = edge;
        _faces = faces;
    }

    /// <summary>Gets the level task id of the water this map belongs to.</summary>
    public int TaskId { get; }

    /// <summary>Gets the map's index within that water's <c>Resource list</c>.</summary>
    public int Index { get; }

    /// <summary>Gets the edge length of every face, in pixels.</summary>
    /// <remarks>
    /// One number for all six. The original creates the cube texture once, at the width
    /// <c>D3DXGetImageInfoFromFileInMemory</c> reports for <em>face zero</em>, and then loads the other
    /// five into it through <c>D3DXLoadSurfaceFromFileInMemory</c>, which would silently rescale a face
    /// that disagreed. All 31 shipped maps agree, so nothing rescales; this loader refuses the case
    /// rather than reproducing a stretch nobody has seen.
    /// </remarks>
    public int Edge { get; }

    /// <summary>Gets one face's pixels, row-major from its top-left corner.</summary>
    /// <param name="face">The face.</param>
    /// <returns><see cref="Edge"/> × <see cref="Edge"/> pixels.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="face"/> is not one of the six.</exception>
    public ReadOnlySpan<Bgra32> this[Igi2CubeFace face]
    {
        get
        {
            int index = (int)face;
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, FaceCount);
            return _faces[index];
        }
    }

    /// <summary>Uploads the six faces as a device cube map.</summary>
    /// <param name="device">The device to create the cube on.</param>
    /// <returns>A handle to the cube, ready for <c>SetEnvironmentCube</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// One cube at <see cref="Edge"/>, six faces written in order, no mip chain — which is exactly what
    /// <c>Envmap_LoadFromMemory</c> (<c>igi2ed 0x581670</c>) builds: a single-mip <c>A8R8G8B8</c> cube at
    /// face zero's width, with file <c>i</c> loaded into cube slot <c>i</c> and no remapping.
    /// </para>
    /// <para>
    /// <b>The face order needs no translation and that is not a coincidence.</b> The engine bakes its
    /// faces in engine axes — <c>Envmap_CubeMapRender</c>'s switch puts up and down in slots 2 and 3,
    /// where a sampler expects ±Y — and the water shader swizzles <c>.xzy</c> on the way to the lookup,
    /// which exchanges Y and Z and lines the two up again. So the file index, the
    /// <see cref="Igi2CubeFace"/> value and the sampler's slot are all the same number, and the caller
    /// that has to do the exchange is whatever builds the direction, not this.
    /// </para>
    /// </remarks>
    public TextureHandle Upload(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        TextureHandle cube = device.CreateCubeTexture(Edge, $"water {TaskId} envmap {Index}");
        for (int face = 0; face < FaceCount; face++)
        {
            device.UpdateCubeFace(cube, face, _faces[face]);
        }

        return cube;
    }

    /// <summary>Names the archive a water task's baked faces ship in.</summary>
    /// <param name="taskId">The water's level task id.</param>
    /// <returns>The bare archive filename, which lives under <see cref="ArchiveDirectory"/>.</returns>
    /// <remarks>
    /// <c>Water_GetResourceFilename</c> (<c>igi2ed 0x6465C0</c>): <c>sprintf(zFilename, "water_%d.res",
    /// Level_GetLevelQTaskIDFromQTask(ptQTask))</c>. The same level-table slot that names the faces
    /// themselves, so a water's whole reflection is addressed by where the level put the task.
    /// </remarks>
    public static string ArchiveName(int taskId) =>
        string.Create(CultureInfo.InvariantCulture, $"water_{taskId}.res");

    /// <summary>Loads one of a water sheet's environment maps.</summary>
    /// <param name="water">The sheet.</param>
    /// <param name="index">One of <see cref="WaterTask.EnvmapIndices"/>.</param>
    /// <param name="archive">The sheet's <c>water_&lt;id&gt;.res</c>.</param>
    /// <returns>The six decoded faces.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A face is missing from the archive, or the six do not agree on one square size.
    /// </exception>
    /// <exception cref="BitmapFormatException">A face is not a bitmap this reader supports.</exception>
    public static Igi2EnvironmentMap Load(WaterTask water, int index, IresArchive archive)
    {
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(archive);

        Bgra32[][] faces = new Bgra32[FaceCount][];
        int edge = -1;

        for (int face = 0; face < FaceCount; face++)
        {
            string resource = water.CubeMapFaceResource(index, face);
            if (!TryRead(archive, resource, out ReadOnlyMemory<byte> bytes))
            {
                throw new InvalidOperationException(
                    $"Environment map {index} of water {water.Id} is missing face {face} ('{resource}').");
            }

            BitmapImage image = BitmapImage.Read(bytes.Span);
            if (image.Width != image.Height)
            {
                throw new InvalidOperationException(
                    $"Face {face} of environment map {index} of water {water.Id} is "
                    + $"{image.Width}×{image.Height}, which is not square.");
            }

            if (edge < 0)
            {
                edge = image.Width;
            }
            else if (image.Width != edge)
            {
                throw new InvalidOperationException(
                    $"Face {face} of environment map {index} of water {water.Id} is {image.Width} across, "
                    + $"but face 0 is {edge}.");
            }

            faces[face] = image.Pixels.ToArray();
        }

        return new Igi2EnvironmentMap(water.Id, index, edge, faces);
    }

    /// <summary>Loads every environment map a water sheet names.</summary>
    /// <param name="water">The sheet.</param>
    /// <param name="archive">The sheet's <c>water_&lt;id&gt;.res</c>.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <returns>The maps, in the order <c>Resource list</c> names them.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// A sheet loads several because <c>Environmental map index</c> is a level expression rather than a
    /// constant, so a mission can switch which one is reflected as it runs. The list is capped at five by
    /// the <c>"%d %d %d %d %d"</c> the original parses it with, which <see cref="WaterTask.EnvmapIndices"/>
    /// already honours.
    /// </remarks>
    public static IReadOnlyList<Igi2EnvironmentMap> LoadAll(
        WaterTask water,
        IresArchive archive,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(archive);

        IIgiLogger log = logger ?? NullLogger.Instance;
        List<Igi2EnvironmentMap> maps = [];

        foreach (int index in water.EnvmapIndices)
        {
            maps.Add(Load(water, index, archive));
        }

        log.Information(
            LogCategory,
            $"Water {water.Id}: {maps.Count} environment map(s), {maps.Count * FaceCount} face(s).");
        return maps;
    }

    /// <summary>
    /// Builds the direction the water's reflection is sampled with — the cube-map texture coordinate.
    /// </summary>
    /// <param name="eyeToSurface">
    /// The surface point relative to the eye, in the engine's axes. Need not be normalized.
    /// </param>
    /// <param name="normal">The water surface normal, in the engine's axes. Expected to be unit length.</param>
    /// <param name="maxReflectionCosine">
    /// The sheet's <see cref="WaterTask.MaxReflectionAngle"/>, which the original clamps the view/normal
    /// cosine to from below.
    /// </param>
    /// <returns>The three-component direction to sample the cube with.</returns>
    /// <remarks>
    /// <para>
    /// This is the whole of the original's rule, and it is readable rather than inferred: the water
    /// vertex shaders are not compiled into the binary, they are <b>assembly source strings</b> handed to
    /// <c>D3DXAssembleShader</c> at start-up by <c>WaterRender_SetModeCallback</c>
    /// (<c>igi2ed 0x579740</c>). The three of them live at <c>0x6B9B90</c>, <c>0x6B98D0</c> and
    /// <c>0x6B95E8</c>, and the third is the one that samples a cube:
    /// </para>
    /// <code>
    /// dp3 r0.x, v0, v0
    /// rsq r0.x, r0.x
    /// mul r0, r0.x, -v0                                   ; V = normalize(eye - surface)
    /// dp3 r2.x, r0, v1                                    ; V·N
    /// max r2.x, r2.x, c14.z                               ; clamped at vMaxReflectionAngle
    /// mul r3.x, r2.x, c5.w                                ; c5.w = 2.0
    /// mad oT0.xyz, v1.xzy, r3.xxx, -r0.xzy                ; (2(V·N)N - V).xzy
    /// </code>
    /// <para>
    /// So the reflection is the ordinary one, and <b>the swizzle is the whole answer to which face is
    /// which</b>: the vector handed to the sampler is <c>(R.x, R.z, R.y)</c>, the engine's Y and Z
    /// exchanged. That is what puts the engine's up axis on Direct3D's, and it is why the bake's third
    /// face — the camera looking along engine +Z, straight up — belongs in the <c>POSITIVE_Y</c> slot.
    /// The bake and the sampler were read from different functions and agree on all six.
    /// </para>
    /// <para>
    /// <b>The clamp is a parameter, not a constant.</b> <c>c14.z</c> is loaded from
    /// <c>WaterContext_t::vMaxReflectionAngle</c> (offset <c>0x3C</c>, set at <c>0x5789FB</c>), the
    /// task's <c>Max Reflection Angle (cos)</c>. Below that cosine the reflection stops opening out
    /// toward the horizon and stays where the cutoff puts it, which is why a grazing view of these
    /// sheets does not sample the sea floor. <c>c5</c> is the literal vector <c>(0, 0.5, 1, 2)</c>, set
    /// at <c>0x578957</c>–<c>0x57896C</c>; that is where the 2 comes from.
    /// </para>
    /// <para>
    /// The low-quality shader at <c>0x6B9B90</c> builds the same reflection and then does
    /// <c>mul r1, r1, c11; add oT1, r1, c12</c> — a component-wise scale and offset, which cannot
    /// swizzle and cannot address a cube. That one is for the flat <c>Envmap</c> image on stage 1.
    /// </para>
    /// </remarks>
    public static Vector3f CubeDirection(Vector3f eyeToSurface, Vector3f normal, float maxReflectionCosine)
    {
        // r0 in the shader: the unit vector from the surface back towards the eye.
        Vector3f view = (-eyeToSurface).Normalized();
        float cosine = MathF.Max(Vector3f.Dot(view, normal), maxReflectionCosine);
        Vector3f reflection = (2f * cosine * normal) - view;

        return new Vector3f(reflection.X, reflection.Z, reflection.Y);
    }

    /// <summary>Picks the face a cube direction lands on.</summary>
    /// <param name="direction">A direction already swizzled by <see cref="CubeDirection"/>.</param>
    /// <returns>The face, and where on it the direction lands.</returns>
    /// <exception cref="ArgumentException"><paramref name="direction"/> is the zero vector.</exception>
    /// <remarks>
    /// The selection and the two coordinates are Direct3D's cube rule, unchanged since Direct3D 8 and
    /// identical in OpenGL and Vulkan: the largest-magnitude component picks the face, the other two
    /// divided by it give <c>s</c> and <c>t</c> in −1…1, and <c>v</c> runs down the face image. That part
    /// is the API's, not the game's — what the game contributes is the swizzle that makes its own axes
    /// arrive in the right order, and the six camera directions it baked to match.
    /// </remarks>
    public static (Igi2CubeFace Face, float U, float V) Project(Vector3f direction)
    {
        float ax = MathF.Abs(direction.X);
        float ay = MathF.Abs(direction.Y);
        float az = MathF.Abs(direction.Z);

        Igi2CubeFace face;
        float sc;
        float tc;
        float ma;

        if (ax >= ay && ax >= az)
        {
            ma = ax;
            if (direction.X >= 0f)
            {
                face = Igi2CubeFace.PositiveX;
                sc = -direction.Z;
            }
            else
            {
                face = Igi2CubeFace.NegativeX;
                sc = direction.Z;
            }

            tc = -direction.Y;
        }
        else if (ay >= az)
        {
            ma = ay;
            sc = direction.X;
            if (direction.Y >= 0f)
            {
                face = Igi2CubeFace.PositiveZ;
                tc = direction.Z;
            }
            else
            {
                face = Igi2CubeFace.NegativeZ;
                tc = -direction.Z;
            }
        }
        else
        {
            ma = az;
            if (direction.Z >= 0f)
            {
                face = Igi2CubeFace.PositiveY;
                sc = direction.X;
            }
            else
            {
                face = Igi2CubeFace.NegativeY;
                sc = -direction.X;
            }

            tc = -direction.Y;
        }

        if (ma <= 0f)
        {
            throw new ArgumentException("A cube direction of zero length names no face.", nameof(direction));
        }

        return (face, ((sc / ma) + 1f) * 0.5f, ((tc / ma) + 1f) * 0.5f);
    }

    /// <summary>The inverse of <see cref="Project"/>: the direction one point on one face looks along.</summary>
    /// <param name="face">The face.</param>
    /// <param name="u">Across the face, 0 at its left edge and 1 at its right.</param>
    /// <param name="v">Down the face, 0 at its top edge and 1 at its bottom.</param>
    /// <returns>
    /// The direction, in the same swizzled space <see cref="CubeDirection"/> produces. Not normalized.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="face"/> is not one of the six.</exception>
    /// <remarks>
    /// Here so that the claim about face order is checkable against the shipped pixels rather than only
    /// against the two functions it was read from: neighbouring faces have to agree along the edge they
    /// share, and they only do if both the order and the orientation are right.
    /// </remarks>
    public static Vector3f Direction(Igi2CubeFace face, float u, float v)
    {
        float sc = (2f * u) - 1f;
        float tc = (2f * v) - 1f;

        return face switch
        {
            Igi2CubeFace.PositiveX => new Vector3f(1f, -tc, -sc),
            Igi2CubeFace.NegativeX => new Vector3f(-1f, -tc, sc),
            Igi2CubeFace.PositiveZ => new Vector3f(sc, 1f, tc),
            Igi2CubeFace.NegativeZ => new Vector3f(sc, -1f, -tc),
            Igi2CubeFace.PositiveY => new Vector3f(sc, -tc, 1f),
            Igi2CubeFace.NegativeY => new Vector3f(-sc, -tc, -1f),
            _ => throw new ArgumentOutOfRangeException(nameof(face), face, "Not one of the six cube faces."),
        };
    }

    /// <summary>Reads the texel a direction lands on.</summary>
    /// <param name="direction">A direction in the space <see cref="CubeDirection"/> produces.</param>
    /// <returns>The nearest texel.</returns>
    /// <exception cref="ArgumentException"><paramref name="direction"/> is the zero vector.</exception>
    /// <remarks>
    /// Nearest, not filtered. The original samples these with <c>D3DTEXF_LINEAR</c> on both filters and
    /// <c>D3DTADDRESS_CLAMP</c> on both axes (<c>WaterRender_SetTextureState</c>, <c>igi2ed 0x579A90</c>),
    /// so this is not what the hardware does — it is here to make the face layout inspectable from a
    /// test, and for a CPU fallback that would rather be plainly wrong than subtly so.
    /// </remarks>
    public Bgra32 Sample(Vector3f direction)
    {
        (Igi2CubeFace face, float u, float v) = Project(direction);

        int x = Math.Clamp((int)(u * Edge), 0, Edge - 1);
        int y = Math.Clamp((int)(v * Edge), 0, Edge - 1);

        return _faces[(int)face][(y * Edge) + x];
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 environment map {Index} of water {TaskId}, {FaceCount}×{Edge}²";

    private static bool TryRead(IresArchive archive, string resource, out ReadOnlyMemory<byte> bytes)
    {
        // The water archives key their entries by the full LOCAL:missions/… path a level was built from,
        // rather than by the MISSION: alias the task generates — the third of the three paths
        // Water_LoadEnvmapData tries. Both really occur, so the comparison is on the filename, which is
        // the part the virtual file system exists to make interchangeable.
        string wanted = FileNameOf(resource);

        foreach (IresEntry entry in archive.Entries)
        {
            if (FileNameOf(entry.Name).Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                bytes = entry.Data;
                return true;
            }
        }

        bytes = default;
        return false;
    }

    private static string FileNameOf(string resource) => resource[(resource.LastIndexOf('/') + 1)..];
}
