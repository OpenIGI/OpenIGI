namespace OpenIGI.Game.World;

/// <summary>What a <see cref="SphereTreeWalk"/> asks of the line it walks a mesh for: each node's sphere test, and each face test.</summary>
public interface ISphereTreeVisitor
{
    /// <summary>Tests a node's sphere against the line.</summary>
    /// <param name="node">The node.</param>
    /// <param name="sibling">Whether the walk reached it from its sibling rather than its parent.</param>
    /// <returns>Whether the line may reach it.</returns>
    bool Sphere(int node, bool sibling);

    /// <summary>Tests one face against the line.</summary>
    /// <param name="face">The face's index.</param>
    /// <returns><see langword="false"/> to stop the walk.</returns>
    bool Face(int face);
}

/// <summary>
/// The sphere-tree walk both games' model line handlers share: Project I.G.I.'s <c>RigidDynCubeObj</c> ray handler
/// (<c>0x4C1840</c>) and I.G.I. 2's <c>RigidDynCubeObj_TraceLinesHandler</c>
/// (<c>igi2 0x58B3B0</c>) and <c>SplineObjSegment_TraceLinesHandler</c> (<c>igi2 0x5B7390</c>).
/// </summary>
/// <remarks>
/// <para>
/// Depth first over <see cref="RetailRayMesh.Links"/> (four ints a node: first face, face count, first child, next sibling),
/// on a stack of a hundred nodes. The root is never tested. A child that fails its sphere test still has its own faces tested
/// before the walk moves to its sibling, the original's quirk; a sibling that fails is passed over, faces and all.
/// </para>
/// <para>
/// Neither original checks the stack's depth. A tree deeper than the stack ends the walk here rather than overrunning it.
/// </para>
/// </remarks>
public static class SphereTreeWalk
{
    private const int StackDepth = 100;

    /// <summary>Walks a mesh's sphere tree for one line.</summary>
    /// <typeparam name="TVisitor">The line's tests.</typeparam>
    /// <param name="links">The tree, four ints a node.</param>
    /// <param name="visitor">The line's tests, which keep what they find.</param>
    public static void Walk<TVisitor>(int[] links, ref TVisitor visitor)
        where TVisitor : struct, ISphereTreeVisitor
    {
        ArgumentNullException.ThrowIfNull(links);
        Span<int> stack = stackalloc int[StackDepth];
        int top = 0;
        int node = 0;
        stack[0] = 0;
        bool first = true;
        while (true)
        {
            if (first)
            {
                first = false;
            }
            else
            {
                int sibling = links[(4 * node) + 3];
                if (sibling == 0)
                {
                    top--;
                    if (top < 0)
                    {
                        return;
                    }

                    node = stack[top];
                    continue;
                }

                node = sibling;
                stack[top] = node;
                if (!visitor.Sphere(node, sibling: true))
                {
                    continue;
                }
            }

            while (true)
            {
                int child = links[(4 * node) + 2];
                if (child == 0)
                {
                    break;
                }

                node = child;
                if (++top >= StackDepth)
                {
                    return;
                }

                stack[top] = node;
                if (!visitor.Sphere(node, sibling: false))
                {
                    break;
                }
            }

            int count = links[(4 * node) + 1];
            int firstFace = links[4 * node];
            for (int f = firstFace; f < firstFace + count; f++)
            {
                if (!visitor.Face(f))
                {
                    return;
                }
            }
        }
    }
}
