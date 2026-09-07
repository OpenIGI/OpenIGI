using BenchmarkDotNet.Attributes;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Benchmarks;

/// <summary>What the port's own math types cost per operation.</summary>
/// <remarks>
/// <para>
/// <see cref="Matrix4f"/> and <see cref="Vector3f"/> are hand-written scalar structs rather than the
/// SIMD-accelerated <c>System.Numerics</c> shapes, which is a deliberate choice — the port needs
/// row-major storage and double-precision world positions that <c>System.Numerics</c> does not offer.
/// The cost of that choice is what this class measures, so any move to a <c>Vector128</c>-backed
/// implementation has a before to compare against rather than an argument.
/// </para>
/// <para>
/// Batched over an array rather than timed one call at a time: a single multiply is a few nanoseconds,
/// which is below the resolution a benchmark can attribute reliably, and the array also keeps the JIT
/// from folding the whole thing to a constant.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class NumericsBenchmarks
{
    private const int Count = 4096;

    private Matrix4f[] _left = [];
    private Matrix4f[] _right = [];
    private Vector3f[] _points = [];

    /// <summary>Builds the operand arrays.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _left = new Matrix4f[Count];
        _right = new Matrix4f[Count];
        _points = new Vector3f[Count];

        for (int i = 0; i < Count; i++)
        {
            float angle = MathF.Tau * i / Count;
            Matrix3f orientation = Matrix3f.RotationY(angle);

            _left[i] = Matrix4f.CreateRigid(orientation, new Vector3f(i, i * 0.5f, i * 0.25f));
            _right[i] = Matrix4f.CreatePerspective(1.2f, 1.777f, 0.1f + (i % 7), 4096f);
            _points[i] = new Vector3f(MathF.Sin(angle), MathF.Cos(angle), i);
        }
    }

    /// <summary>Composing two transforms, which every draw call does at least once.</summary>
    /// <returns>A digest of the products, so nothing is dead code.</returns>
    [Benchmark(Description = "Matrix4f.Multiply x4096")]
    public float MultiplyMatrices()
    {
        float sink = 0f;

        for (int i = 0; i < Count; i++)
        {
            sink += (_left[i] * _right[i]).M11;
        }

        return sink;
    }

    /// <summary>Projecting a point through a transform.</summary>
    /// <returns>A digest of the results.</returns>
    [Benchmark(Description = "Matrix4f.TransformPoint x4096")]
    public float TransformPoints()
    {
        float sink = 0f;

        for (int i = 0; i < Count; i++)
        {
            sink += _left[i].TransformPoint(_points[i]).X;
        }

        return sink;
    }
}
