using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>The soldiers sharing one <c>(model, skeleton, stand animation)</c> triple: the geometry they are drawn from, and each soldier drawn.</summary>
/// <param name="roster">The simulation's side of the group, which the soldiers' agents join.</param>
/// <param name="geometry">The model's geometry, which every soldier's own skinned mesh starts as.</param>
/// <param name="mesh">The group's mesh.</param>
/// <param name="instance">The model's instance in its pack, which names its textures.</param>
/// <param name="pack">The pack the model came from.</param>
internal sealed class SoldierGroup(SoldierModelGroup roster, MeshGeometry geometry, MeshHandle mesh, MtpInstance? instance, LoadedPack? pack)
{
    /// <summary>The soldiers this group draws, as the simulation keeps them, and what it reads of their model.</summary>
    public SoldierModelGroup Roster { get; } = roster;

    /// <summary>What the simulation reads of the group's model: the model, its collision mesh and its lowest vertex.</summary>
    public SoldierBodyModel Body => Roster.Body;

    public MeshGeometry Geometry { get; } = geometry;

    public MeshHandle Mesh { get; set; } = mesh;

    public MtpInstance? Instance { get; } = instance;

    public LoadedPack? Pack { get; } = pack;

    /// <summary>Gets or sets the model's lower levels of detail, the second level first.</summary>
    public IReadOnlyList<SoldierLevel> Lower { get; set; } = [];

    /// <summary>Gets or sets each level's triangle count, the first level's first.</summary>
    public int[] Triangles { get; set; } = [];

    private readonly List<SoldierMember> _soldiers = [];

    /// <summary>The soldiers the group draws, in the order the roster holds their agents; joined through <see cref="Add"/>.</summary>
    public List<SoldierMember> Soldiers => _soldiers;

    /// <summary>Adds a soldier whose agent the simulation has just added to the roster, to be drawn beside it.</summary>
    /// <param name="member">The soldier.</param>
    public void AddDrawn(SoldierMember member) => _soldiers.Add(member);

    /// <summary>Adds a soldier to the group, and its agent to the roster in the same place.</summary>
    /// <param name="member">The soldier.</param>
    public void Add(SoldierMember member)
    {
        _soldiers.Add(member);
        Roster.Add(member.Agent);
    }
}
