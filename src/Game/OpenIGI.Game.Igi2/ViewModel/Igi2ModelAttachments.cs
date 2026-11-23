using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.ViewModel;

/// <summary>The rigid models nested under a held model, <c>modelobj.c ModelObj_CreateAttachObjects</c>.</summary>
/// <param name="models">The mission's models.</param>
/// <param name="magic">The configured magic-object types.</param>
public sealed class Igi2ModelAttachments(Func<string, MefModel?> models, Igi2MagicObjTypes? magic)
{
    /// <summary>Resolves an animation or model attachment through <c>magicobj.c MagicObj_GetMagicObjType</c>.</summary>
    /// <param name="name">The attachment name.</param>
    /// <returns>The configured model, its own name when unconfigured, or null for a marker or shadow.</returns>
    public string? ModelOf(string name) => magic is null ? name : magic.ModelOf(name);

    /// <summary>Expands a rigid model's complete attachment tree in the view's local frame.</summary>
    /// <param name="part">The root part, which is also returned.</param>
    /// <param name="disabled">
    /// Attach points not made anywhere in the tree, by exact name (<c>ModelObj_apzDisabledAttachObjects</c>), or <see langword="null"/>
    /// for none.
    /// </param>
    /// <returns>The root and its drawable children, in creation order.</returns>
    /// <remarks>
    /// <c>ModelObj_CreateAttachObject</c> makes nothing for an attach point named in the disabled list, nor anything under it. The list
    /// is set only while a <c>WeaponMagicObj</c> makes its model (<c>WeaponMagicObj_GetModelName</c>): the names of the attach objects of
    /// the clip of every controller above it, so a part the clip carries and moves itself is not also drawn standing on the weapon.
    /// </remarks>
    public IEnumerable<TitleViewModelPart> Expand(TitleViewModelPart part, IReadOnlyCollection<string>? disabled = null)
    {
        ArgumentNullException.ThrowIfNull(part);
        return Walk(part, new HashSet<string>(StringComparer.OrdinalIgnoreCase), disabled);
    }

    private IEnumerable<TitleViewModelPart> Walk(TitleViewModelPart part, HashSet<string> ancestors, IReadOnlyCollection<string>? disabled)
    {
        yield return part;
        if (part.Pose is not null || ancestors.Count >= 16 || !ancestors.Add(part.Model))
        {
            yield break;
        }

        if (models(part.Model) is { } model)
        {
            Igi2Pose parent = Igi2Pose.FromMatrix(new Vector3d(part.Position.X, part.Position.Y, part.Position.Z), part.Orientation);
            foreach (MefAttachment attachment in model.Attachments)
            {
                // A strcmp over the disabled list: an exact match makes nothing.
                if (disabled is not null && disabled.Contains(attachment.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                // modelobj.c ModelObj_CreateAttachObject: names are resolved through MagicObj_GetMagicObjType;
                // unknown names are rigid models, while configured "none" and shadows draw nothing.
                string? name = ModelOf(attachment.Name);
                if (string.IsNullOrEmpty(name) || string.Equals(name, Igi2MagicObjTypes.NoModel, StringComparison.Ordinal))
                {
                    continue;
                }

                Igi2Pose child = parent.Attach(attachment);
                TitleViewModelPart attached = new(name, null,
                    new Vector3f((float)child.Position.X, (float)child.Position.Y, (float)child.Position.Z), child.ToMatrix());
                foreach (TitleViewModelPart nested in Walk(attached, ancestors, disabled))
                {
                    yield return nested;
                }
            }
        }

        ancestors.Remove(part.Model);
    }
}
