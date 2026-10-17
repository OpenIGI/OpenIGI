using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Rendering;
using Xunit;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary><c>WaterRender_Draw</c>'s subdivision: fine near the eye, coarse far from it, never what is out of view.</summary>
public sealed class Igi2WaterTessellationTests
{
    private const float Metre = 4096f;

    private static readonly Matrix4f Projection = Matrix4f.CreatePerspective(1.2f, 16f / 9f, 0.1f, 5000f);

    [Fact]
    public void A_sheet_far_below_the_eye_stays_its_two_triangles()
    {
        Igi2WaterTessellation tessellation = Build(Sheet(100f), new Vector3f(0f, 0f, 10000f * Metre), pitch: -1.5f);

        Assert.Equal(4, tessellation.VertexCount);
        Assert.Equal(6, tessellation.IndexCount);
    }

    [Fact]
    public void Near_the_eye_it_is_cut_finely()
    {
        Igi2WaterTessellation tessellation = Build(Sheet(1000f), new Vector3f(0f, 0f, 2f * Metre), pitch: -0.3f);

        Assert.True(tessellation.VertexCount > 100, $"only {tessellation.VertexCount} vertices near the eye.");

        // Some vertex within a few metres of the point under the eye: a sheet two kilometres wide is cut down to
        // the size of the view near it.
        Assert.Contains(
            tessellation.Vertices.ToArray(),
            static v => MathF.Abs(v.Position.X) < 2f && MathF.Abs(v.Position.Y) < 4f);
    }

    [Fact]
    public void Every_triangle_faces_up()
    {
        Igi2WaterTessellation tessellation = Build(Sheet(1000f), new Vector3f(0f, 0f, 2f * Metre), pitch: -0.3f);
        Vertex3D[] vertices = tessellation.Vertices.ToArray();
        ushort[] indices = tessellation.Indices.ToArray();

        Assert.NotEmpty(indices);
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3f a = vertices[indices[i]].Position, b = vertices[indices[i + 1]].Position, c = vertices[indices[i + 2]].Position;
            float up = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
            Assert.True(up > 0f, $"triangle {i / 3} faces down.");
        }
    }

    [Fact]
    public void Water_behind_the_eye_is_not_drawn()
    {
        // Looking straight up from above the sheet: all of it is behind the view.
        Igi2WaterTessellation tessellation = Build(Sheet(2f), new Vector3f(0f, 0f, 5f * Metre), pitch: 1.5f);

        Assert.Equal(0, tessellation.IndexCount);
    }

    [Fact]
    public void The_surface_moves_with_the_clock()
    {
        Igi2WaterSheet sheet = Sheet(200f, new Igi2WaterWave(8f, 0.5f, 1f, 0f));
        Vector3f eye = new(0f, 0f, 3f * Metre);
        Vertex3D[] first = Build(sheet, eye, -0.3f, seconds: 0.0).Vertices.ToArray();
        Vertex3D[] later = Build(sheet, eye, -0.3f, seconds: 5.0).Vertices.ToArray();

        // The corners stay where the square is; the surface between them rises and falls.
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(first[i].Position.X, later[i].Position.X);
            Assert.Equal(first[i].Position.Y, later[i].Position.Y);
        }

        int shared = Math.Min(first.Length, later.Length);
        Assert.Contains(Enumerable.Range(0, shared), i => first[i].Position.Z != later[i].Position.Z);
    }

    [Fact]
    public void A_shore_is_shallow_and_open_water_deep()
    {
        // Ground a metre under the surface on the west half, far under it on the east.
        Igi2WaterSheet sheet = Sheet(200f) with { AbsorptionLength = 4f };
        Igi2WaterTessellation tessellation = Build(
            sheet, new Vector3f(0f, 0f, 3f * Metre), -0.3f, ground: (x, _) => x < 0 ? -1.0 * Metre : -100.0 * Metre);

        Vertex3D[] vertices = tessellation.Vertices.ToArray();
        Assert.Contains(vertices, static v => v.Position.X < -1f && MathF.Abs(v.DiffuseU - 0.75f) < 0.01f);
        Assert.Contains(vertices, static v => v.Position.X > 1f && v.DiffuseU == 0f);
    }

    private static Igi2WaterTessellation Build(
        Igi2WaterSheet sheet,
        Vector3f eye,
        float pitch,
        double seconds = 0.0,
        Func<double, double, double?>? ground = null)
    {
        Igi2WaterTessellation tessellation = new();
        tessellation.Build(sheet, Igi2WaterWaveField.For(sheet), seconds, eye, Camera3D.LookRotation(0f, pitch), Projection, ground);
        return tessellation;
    }

    private static Igi2WaterSheet Sheet(float halfExtentMetres, params Igi2WaterWave[] waves) => new(
        1,
        default,
        halfExtentMetres * 4096.0,
        0.2f,
        1f,
        0f,
        1f,
        default,
        [],
        string.Empty,
        string.Empty,
        waves);
}
