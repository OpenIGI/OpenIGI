namespace OpenIGI.Game.World;

/// <summary>
/// A level's level-of-detail settings, both games' <c>LODSettings</c> for one kind of object: how soon a model gives up detail
/// and where it is not drawn at all, both relative to its size.
/// </summary>
/// <param name="Detail">The first switch's distance over the object's radius, as a factor (the authored percentage times 0.02).</param>
/// <param name="Cull">The cutoff's distance over the object's radius, as a factor.</param>
/// <param name="Blend">How far the radius stands back from the ideal one in the effective size, nought to one.</param>
/// <param name="Range">The ideal radius, world units.</param>
public readonly record struct ModelLodSettings(float Detail, float Cull, float Blend, float Range)
{
    /// <summary>The factor a percentage is taken by (<c>0x1.47ae14p-6f</c>).</summary>
    public const float Percent = 0.02f;
}

/// <summary>
/// Which of a model's levels of detail a view draws, Project I.G.I.'s <c>0x4CFCB0</c> and I.G.I. 2's mesh LOD pick: a model
/// with distances of its own switches at them; any other gives up detail as its size on screen shrinks, a level's share of
/// the first one's triangles squared setting how small it must be.
/// </summary>
public static class ModelLodSelector
{
    /// <summary>Picks a level.</summary>
    /// <param name="depth">How far ahead of the eye the model's centre is, world units.</param>
    /// <param name="radius">Its radius, world units, its largest scale applied.</param>
    /// <param name="scale">The detail scale, one unless the profile asks for less (<c>gLodScale</c>).</param>
    /// <param name="fieldOfView">The view's horizontal half-angle tangent.</param>
    /// <param name="settings">The level's settings.</param>
    /// <param name="triangles">Each level's triangle count, the first level's first.</param>
    /// <param name="distances">
    /// The model's own distances, world units: to its second to fifth levels and its cutoff; or empty when it has none.
    /// </param>
    /// <returns>The level, or <c>-1</c> when the model is beyond its cutoff and not drawn.</returns>
    public static int Select(
        double depth,
        double radius,
        double scale,
        float fieldOfView,
        in ModelLodSettings settings,
        ReadOnlySpan<int> triangles,
        ReadOnlySpan<float> distances)
    {
        int count = triangles.Length;
        if (count == 0)
        {
            return 0;
        }

        if (distances.Length >= 5)
        {
            // The model's own table, in the view's terms.
            float d = (float)(fieldOfView * ((depth - radius) * scale));
            if (d > distances[4])
            {
                return -1;
            }

            int level = 0;
            if (distances[0] == 0f)
            {
                level = Math.Min(count - 1, 1);
            }

            while (d > distances[level] && level < count - 1)
            {
                level++;
            }

            return level;
        }

        // The effective size stands between the object's own radius and the ideal one.
        float effective = (float)(((settings.Range - radius) * (1f - settings.Blend)) + radius);
        double ahead = (depth - effective) * scale;
        float q = (float)ahead;
        if (!(ahead > 0.0))
        {
            return 0;
        }

        float inverse = 1f / q;
        float first = 1f / (settings.Detail * effective);
        int chosen = 0;
        while (chosen < count - 1 && fieldOfView * Threshold(triangles, chosen, first) > inverse)
        {
            chosen++;
        }

        if (chosen == count - 1 && !(settings.Cull * effective >= fieldOfView * q))
        {
            return -1;
        }

        return chosen;
    }

    // A level's threshold: the first's, times its share of the first's triangles squared.
    private static float Threshold(ReadOnlySpan<int> triangles, int level, float first)
    {
        if (level == 0)
        {
            return first;
        }

        double ratio = (double)triangles[level] / triangles[0];
        return (float)(ratio * ratio * first);
    }
}
