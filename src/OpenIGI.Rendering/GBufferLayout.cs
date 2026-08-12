namespace OpenIGI.Rendering;

/// <summary>Optional deferred attachments selected from the active preset's actual consumers.</summary>
[Flags]
public enum GBufferLayout
{
    /// <summary>Albedo, normal and material only.</summary>
    Compact = 0,

    /// <summary>The baked-light attachment consumed by baked and hybrid lighting.</summary>
    BakedLighting = 1,

    /// <summary>The motion attachment consumed by temporal AA or motion blur.</summary>
    MotionVectors = 2,

    /// <summary>The legacy complete layout.</summary>
    Full = BakedLighting | MotionVectors,
}
