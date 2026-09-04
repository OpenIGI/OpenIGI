using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// Pins the memory layout of <see cref="Matrix4f"/>, which <see cref="Matrix4f.Multiply"/> depends on.
/// </summary>
/// <remarks>
/// The product is computed by reading the sixteen elements as four four-wide vectors straight out of
/// the struct, which is only correct while they sit in memory in declaration order with no padding.
/// The type declares <see cref="LayoutKind.Sequential"/> to say so, but the elements are auto-properties
/// and their backing fields are compiler-generated — so the guarantee rests on the compiler emitting
/// those fields in source order. That is worth checking rather than assuming: if it ever stopped being
/// true the multiply would not throw, it would silently return a matrix with its rows shuffled, and the
/// first symptom would be a rendered frame that looks wrong in a way no test asserts on.
/// </remarks>
public sealed class Matrix4fLayoutTests
{
    /// <summary>The struct must be exactly sixteen floats, with no padding.</summary>
    [Fact]
    public void MatrixIsSixteenContiguousFloats() =>
        Assert.Equal(Matrix4f.ComponentCount * sizeof(float), Unsafe.SizeOf<Matrix4f>());

    /// <summary>The elements must lie in memory in row-major declaration order.</summary>
    [Fact]
    public void ElementsLieInRowMajorOrder()
    {
        // Sixteen distinguishable values, so a transposed or rotated layout cannot pass by coincidence
        // the way a symmetric matrix of zeroes and ones would.
        Matrix4f matrix = new(
            1f, 2f, 3f, 4f,
            5f, 6f, 7f, 8f,
            9f, 10f, 11f, 12f,
            13f, 14f, 15f, 16f);

        ReadOnlySpan<float> elements =
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<Matrix4f, float>(ref matrix), 16);

        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(i + 1, elements[i]);
        }
    }

    /// <summary>The vectorised product must agree with the arithmetic spelled out element by element.</summary>
    /// <remarks>
    /// Not asserted bit-for-bit: a fused multiply-add keeps full precision through the multiply where
    /// two separate operations round twice, so the vector path can differ in the last bit — in the
    /// direction of being more accurate. The tolerance is relative to the magnitudes involved.
    /// </remarks>
    [Fact]
    public void VectorisedProductMatchesTheScalarArithmetic()
    {
        uint state = 0x5EED_1234u;

        float Next()
        {
            state = (state * 1664525u) + 1013904223u;
            return (((state >> 8) & 0xFFFF) / 65535f * 20f) - 10f;
        }

        for (int trial = 0; trial < 64; trial++)
        {
            Matrix4f left = new(
                Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(),
                Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next());

            Matrix4f right = new(
                Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(),
                Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next());

            Matrix4f product = left * right;

            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float expected = 0f;

                    for (int k = 0; k < 4; k++)
                    {
                        expected += Element(left, row, k) * Element(right, k, column);
                    }

                    float actual = Element(product, row, column);

                    Assert.True(
                        MathF.Abs(expected - actual) <= 1e-3f * MathF.Max(MathF.Abs(expected), 1f),
                        $"trial {trial}, element ({row}, {column}): expected {expected}, got {actual}.");
                }
            }
        }
    }

    private static float Element(Matrix4f matrix, int row, int column) =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<Matrix4f, float>(ref matrix), 16)[(row * 4) + column];
}
