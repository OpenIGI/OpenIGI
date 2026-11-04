using OpenIGI.Editor.Core.Configs;
using OpenIGI.Scripting.Qsc;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>The games' config scripts edited as forms: read, named, changed and compiled back.</summary>
public sealed class ConfigDocumentTests
{
    /// <summary>
    /// Every weapon and ammo script reads as a config with its schema, names every value the game's loader reads, and
    /// compiles back to the same script; an AI script, which branches, is not a config.
    /// </summary>
    [RequiresTitleDataFact("igi1")]
    public void Retail_weapon_and_ammo_scripts_are_configs()
    {
        int weapons = 0, ammo = 0;
        foreach (string path in GameDataSource.Igi1.EnumerateFiles("*.qvm").Where(static p => p.Contains("weapon.qvm", StringComparison.OrdinalIgnoreCase) || p.Contains("ammo.qvm", StringComparison.OrdinalIgnoreCase)))
        {
            byte[] bytes = File.ReadAllBytes(path);
            ConfigDocument document = ConfigDocument.TryRead(bytes) ?? throw new Xunit.Sdk.XunitException($"{path} is not a config");
            ConfigCall call = Assert.Single(document.Calls);
            Assert.NotNull(call.Schema);
            Assert.True(call.Values.Count >= call.Schema!.Fields.Count, path);
            Assert.Equal(QscPrinter.Print(QscDecompiler.Decompile(bytes)), QscPrinter.Print(QscDecompiler.Decompile(document.Compile())));
            Assert.False(document.IsDirty);
            weapons += call.Name == "DefineWeaponType" ? 1 : 0;
            ammo += call.Name == "DefineAmmoType" ? 1 : 0;
        }

        Assert.True(weapons > 20 && ammo > 10, $"{weapons} weapons, {ammo} ammo");
        Assert.Null(ConfigDocument.TryRead(File.ReadAllBytes(GameData.Resolve("common/ai/guard.qvm"))));
    }

    /// <summary>A value changed reads back changed after compiling; a typed expression is kept as written; revert puts it back.</summary>
    [RequiresTitleDataFact("igi1")]
    public void An_edit_compiles_and_reverts()
    {
        ConfigDocument document = ConfigDocument.TryRead(File.ReadAllBytes(GameData.Resolve("weapons/AK47/weapon.qvm")))!;
        int clip = document.Calls[0].Schema!.Fields.ToList().FindIndex(static f => f.Name == "Rounds per clip");
        Assert.Equal("30", document.Text(0, clip));

        document.Set(0, clip, new QscIntLiteral(45));
        Assert.True(document.TrySetText(0, 46, "20 * WORLD_METER", out _));
        Assert.False(document.TrySetText(0, 46, "20 *", out string? problem));
        Assert.NotNull(problem);
        Assert.True(document.IsModified(0, clip));

        ConfigDocument again = ConfigDocument.TryRead(document.Compile())!;
        Assert.Equal("45", again.Text(0, clip));
        Assert.Equal("20 * WORLD_METER", again.Text(0, 46));

        document.RevertAll();
        Assert.False(document.IsDirty);
        Assert.Equal("30", document.Text(0, clip));
    }
}
