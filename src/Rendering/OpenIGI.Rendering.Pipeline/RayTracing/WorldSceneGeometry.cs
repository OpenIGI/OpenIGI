namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>One mesh's traceable form, and how much of it was built.</summary>
/// <param name="Structure">The structure over its triangles.</param>
/// <param name="IndexCount">How many indices it covered when it was built.</param>
/// <param name="RecordBase">
/// Where this structure's run of material records starts. An instance carries it as its custom index,
/// so a hit reaches its own record by adding the geometry index traversal reports.
/// </param>
/// <param name="RecordCount">
/// How many of those records the structure owns and gives back when it goes; nought for a terrain
/// page, whose one record outlives its rebuilds.
/// </param>
internal readonly record struct WorldSceneGeometry(
    BottomLevelStructure Structure, int IndexCount, int RecordBase, int RecordCount);
