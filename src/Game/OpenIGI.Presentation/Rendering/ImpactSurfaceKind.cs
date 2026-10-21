namespace OpenIGI.Presentation.Rendering;

/// <summary>What a burst of impact particles looks like, decided by what the round struck.</summary>
public enum ImpactSurfaceKind
{
    /// <summary>Concrete, stone, brick — grey dust with a few sparks. The fallback.</summary>
    Stone,

    /// <summary>Metal — a shower of hot sparks and almost no dust.</summary>
    Metal,

    /// <summary>Ground, sand, gravel, grass — a slow brown plume and hardly any sparks.</summary>
    Dirt,

    /// <summary>Wood — warm chips and a modest puff.</summary>
    Wood,

    /// <summary>A body — a brief red mist, no sparks.</summary>
    Flesh,
}
