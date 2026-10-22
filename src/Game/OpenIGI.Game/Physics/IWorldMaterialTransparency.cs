namespace OpenIGI.Game.Physics;

/// <summary>How much of a line of sight a level's materials let through: what <c>World_LineOfSight</c> asks of a material.</summary>
public interface IWorldMaterialTransparency
{
    /// <summary>Gets how much of a line of sight a material lets through, <c>QMaterial_t.vTransparency</c>.</summary>
    /// <param name="material">The material's row in the level's table.</param>
    /// <returns>Its transparency, from nought (it stops sight) to one; nought for a row the table does not hold.</returns>
    float TransparencyOf(int material);
}
