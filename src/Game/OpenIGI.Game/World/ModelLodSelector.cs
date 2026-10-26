namespace OpenIGI.Game.World;

/// <summary>
/// How a title picks its levels: Project I.G.I. by each model's depth ahead of the eye throughout; I.G.I. 2 its rigid models by their
/// straight distance (<c>ModelObj_GetLOD</c>), with the view's field of view held to one and the model-detail option's bias.
/// </summary>
/// <param name="RigidByDistance">Whether rigid models are picked by their straight distance, I.G.I. 2's way.</param>
/// <param name="FieldOfViewCeiling">The most the view's half-angle tangent counts for: one in I.G.I. 2, unbounded in Project I.G.I.</param>
/// <param name="Bias">
/// <c>ModelObj_vLODBias</c>, one less the graphics options' model detail: nought at full detail. It stretches a rigid model's distances
/// (buildings excepted) and holds a body at a coarser level outside cutscenes.
/// </param>
public readonly record struct ModelLodRules(bool RigidByDistance, float FieldOfViewCeiling, float Bias)
{
    /// <summary>Gets Project I.G.I.'s rules.</summary>
    public static ModelLodRules Igi1 { get; } = new(false, float.PositiveInfinity, 0f);

    /// <summary>Gets <c>ModelObj_vActualLODBiasSquared</c>, <c>(4 × bias + 1)²</c>.</summary>
    public float BiasSquared => ((Bias * 4f) + 1f) * ((Bias * 4f) + 1f);

    /// <summary>Gets I.G.I. 2's rules at a model detail.</summary>
    /// <param name="modelDetail">The graphics options' model detail, one at full.</param>
    /// <returns>The rules.</returns>
    public static ModelLodRules Igi2(float modelDetail) => new(true, 1f, 1f - modelDetail);

    /// <summary>
    /// The level a body is drawn at once the bias has had its say, I.G.I. 2's <c>Direct3DRender_DrawBoneMesh</c>: with any bias, a body
    /// at or nearer than the second level (the third from a bias of a half) is drawn there, one beyond its cutoff among them.
    /// </summary>
    /// <param name="level">The level its distance picks, or <c>-1</c>.</param>
    /// <param name="count">The model's number of levels.</param>
    /// <param name="held">Whether a cutscene holds the bias off.</param>
    /// <returns>The level drawn, or <c>-1</c>.</returns>
    public int Biased(int level, int count, bool held)
    {
        if (Bias == 0f || held)
        {
            return level;
        }

        int coarse = Math.Min(Bias >= 0.5f ? 2 : 1, count - 1);
        return level <= coarse ? coarse : level;
    }
}

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

    /// <summary>Picks a rigid model's level by its straight distance, I.G.I. 2's <c>ModelObj_GetLOD</c>.</summary>
    /// <param name="distanceSquared">
    /// The squared distance from the eye to the model's render-sphere centre, world units (<c>ModelObj_CalcDistanceSquared</c>).
    /// </param>
    /// <param name="scale">The model's largest scale.</param>
    /// <param name="fieldOfView">The view's horizontal half-angle tangent, held to one.</param>
    /// <param name="biasSquared">The model-detail bias, squared; one for a building.</param>
    /// <param name="radius">The render sphere's radius, world units.</param>
    /// <param name="cutoff">The level's rigid cutoff factor, <c>Mesh3D_vRigidObjCutoff</c>.</param>
    /// <param name="count">The model's number of levels.</param>
    /// <param name="distances">The model's own distances, world units: to its second to fifth levels and its cutoff; or empty.</param>
    /// <returns>The level, or <c>-1</c> beyond the cutoff.</returns>
    /// <remarks>
    /// With distances of its own a model steps a level each time the biased distance passes the next one, and is gone past the
    /// unbiased cutoff. Without, it is gone past its radius times the cutoff, and nearer than that steps down in equal parts of it,
    /// coarsest first.
    /// </remarks>
    public static int SelectByDistance(
        double distanceSquared,
        float scale,
        float fieldOfView,
        float biasSquared,
        float radius,
        float cutoff,
        int count,
        ReadOnlySpan<float> distances)
    {
        if (count <= 0)
        {
            return 0;
        }

        float fov = Math.Min(fieldOfView, 1f);
        float seen = (float)(distanceSquared * fov * fov);
        if (distances.Length >= 5)
        {
            float biased = seen * biasSquared;
            int level = 0;
            while (level < count - 1)
            {
                float next = scale * distances[level];
                if (!(biased >= next * next))
                {
                    return level;
                }

                level++;
            }

            float last = scale * distances[4];
            return seen >= last * last ? -1 : count - 1;
        }

        float reach = scale * radius * cutoff;
        if (!(reach * reach >= seen))
        {
            return -1;
        }

        float seenBiased = seen * biasSquared;
        float step = reach / count;
        int chosen = count - 1;
        for (; chosen > 0; chosen--)
        {
            reach -= step;
            if (reach * reach < seenBiased)
            {
                break;
            }
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
