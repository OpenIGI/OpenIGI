using OpenIGI.Core.Configuration;
using OpenIGI.Desktop.Configuration;
using OpenIGI.Game;
using OpenIGI.Host.Configuration;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Guards the one rule the default key bindings have to keep.</summary>
public sealed class DefaultBindTests
{
    /// <summary>
    /// A key the game binds for itself must not write a setting the game saves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These keys exist to flip between two implementations while looking at the same frame. If the
    /// setting behind one is archived, that flip is written into <c>settings.cfg</c> and silently becomes
    /// what the game starts as — which is not something anybody pressing a key to compare two pictures
    /// has agreed to.
    /// </para>
    /// <para>
    /// It happened. <c>r_rt</c> was archived while F6 toggled it: comparing the hardware and software
    /// trace once left the software path in the settings file, the next launch came up on it, the
    /// graphics preset read as Custom because a managed value no longer matched, and the picture was
    /// quietly worse with nothing on screen explaining why. The report was "my ultra preset looks bad and
    /// I think something overrode my settings", and it took a diff of the saved file against a freshly
    /// applied Ultra to find. This is that diff, made automatic.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoDefaultBindTogglesASavedSetting()
    {
        // Forces the render variables to register, so the registry can be asked about them.
        _ = RenderCVars.Lighting;

        // And the gameplay ones, which live in another assembly and another static constructor: the
        // third-person view is bound by default, so the rule below has to be able to see it.
        _ = GameplayCVars.ThirdPerson;

        List<string> archived = [];
        foreach ((string key, string statement) in DefaultBinds.All)
        {
            if (DefaultBinds.ToggledVariable(statement) is not { } name)
            {
                continue;
            }

            Assert.True(
                CVarRegistry.Default.TryGet(name, out ICVar? variable) && variable is not null,
                $"The default bind on {key} toggles {name}, which is not a variable that exists.");

            if (variable!.Flags.HasFlag(CVarOptions.Archive))
            {
                archived.Add($"{key} -> {name}");
            }
        }

        Assert.True(
            archived.Count == 0,
            "These keys are bound by default to settings the game saves, so pressing one to compare two "
                + "pictures decides what the game starts as next time: " + string.Join(", ", archived));
    }

    /// <summary>Every default bind names a distinct key, so none of them quietly loses to another.</summary>
    [Fact]
    public void DefaultBindsDoNotCollide()
    {
        HashSet<string> keys = [];
        foreach ((string key, _) in DefaultBinds.All)
        {
            Assert.True(keys.Add(key), $"Two default binds claim {key}; only the first would ever apply.");
        }
    }
}
