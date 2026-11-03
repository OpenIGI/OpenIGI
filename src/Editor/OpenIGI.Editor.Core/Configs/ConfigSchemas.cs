namespace OpenIGI.Editor.Core.Configs;

/// <summary>How a value of a config call is edited.</summary>
public enum ConfigFieldKind
{
    /// <summary>A whole number.</summary>
    Integer,

    /// <summary>A number with a fraction.</summary>
    Number,

    /// <summary>Text: a name, a model, a sound.</summary>
    Text,

    /// <summary>A localised text key, written in angle brackets (<c>&lt;AK-47DESC&gt;</c>).</summary>
    TextKey,

    /// <summary>One of a set of named constants (<see cref="ConfigField.Choices"/>).</summary>
    Choice,

    /// <summary>Anything the script can write, edited as it is written (a constant, a sum).</summary>
    Expression,
}

/// <summary>One value of a config call: what it is called, what section it is shown in, and how it is edited.</summary>
/// <param name="Name">Its name (<c>Rounds per clip</c>).</param>
/// <param name="Section">The section it is grouped under (<c>Ammunition</c>).</param>
/// <param name="Kind">How it is edited.</param>
/// <param name="Tip">What it does, for its tooltip.</param>
public sealed record ConfigField(string Name, string Section, ConfigFieldKind Kind, string? Tip = null)
{
    /// <summary>Gets the constants a <see cref="ConfigFieldKind.Choice"/> offers.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];
}

/// <summary>The values of one config call (<c>DefineWeaponType</c>), in the order the game reads them.</summary>
/// <param name="Native">The call's name.</param>
/// <param name="Title">What it defines, for the editor's name (<c>Weapon</c>).</param>
/// <param name="Fields">Its values, first to last; any past the end are shown by number.</param>
public sealed record ConfigSchema(string Native, string Title, IReadOnlyList<ConfigField> Fields);

/// <summary>
/// The config calls the games' common scripts make, with each value named as the game's loader reads it: what turns a
/// line of forty-seven numbers into a form.
/// </summary>
/// <remarks>
/// The weapon and ammo values are named after Project I.G.I.'s loaders (<c>DefineWeaponType</c>, <c>0x413F90</c>, and
/// <c>DefineAmmoType</c>), in the order they read them. A call with no schema is still edited, its values by number.
/// </remarks>
public static class ConfigSchemas
{
    private const string Identity = "Identity";
    private const string Display = "Shown to the player";
    private const string Ballistics = "Ballistics";
    private const string Firing = "Firing";
    private const string Recoil = "Spread and recoil";
    private const string Models = "Models";
    private const string Animations = "Animations";
    private const string Sounds = "Sounds";
    private const string Ai = "AI";
    private const string Tracer = "Tracer";

    private static readonly Dictionary<string, ConfigSchema> ByNative = new(StringComparer.Ordinal)
    {
        ["DefineWeaponType"] = new("DefineWeaponType", "Weapon",
        [
            new("Weapon id", Identity, ConfigFieldKind.Integer, "The number scripts and the game know the weapon by; unique"),
            new("Script id", Identity, ConfigFieldKind.Text, "The constant scripts name it with (WEAPON_ID_AK47)"),
            new("Name", Display, ConfigFieldKind.TextKey, "The text key of its name"),
            new("Manufacturer", Display, ConfigFieldKind.TextKey),
            new("Description", Display, ConfigFieldKind.TextKey),
            new("Category", Identity, ConfigFieldKind.Choice, "What kind of weapon it is, which decides how it is held and used")
            {
                Choices = ["WEAPONTYPE_GUN", "WEAPONTYPE_PISTOL", "WEAPONTYPE_SHOTGUN", "WEAPONTYPE_KNIFE", "WEAPONTYPE_GRENADE", "WEAPONTYPE_BINOCULAR", "WEAPONTYPE_MEDIPACK", "WEAPONTYPE_PROXIMITYMINE"],
            },
            new("Sight", Display, ConfigFieldKind.Choice, "Whether it shows a crosshair") { Choices = ["SIGHTDISPLAYTYPE_NONE", "SIGHTDISPLAYTYPE_CROSSHAIR"] },
            new("Ammo display", Display, ConfigFieldKind.Choice, "How the HUD counts its ammunition")
            {
                Choices = ["AMMODISPLAYTYPE_NONE", "AMMODISPLAYTYPE_NORMAL", "AMMODISPLAYTYPE_CLIP", "AMMODISPLAYTYPE_BARREL", "AMMODISPLAYTYPE_SHELL", "AMMODISPLAYTYPE_GRENADE"],
            },
            new("Mass (g)", Ballistics, ConfigFieldKind.Integer),
            new("Calibre", Ballistics, ConfigFieldKind.Integer, "The ammunition it takes, by calibre (762 for 7.62 mm)"),
            new("Damage factor", Ballistics, ConfigFieldKind.Number),
            new("Penetration", Ballistics, ConfigFieldKind.Number, "How much it goes through"),
            new("Reload time (s)", Firing, ConfigFieldKind.Number),
            new("Muzzle velocity", Ballistics, ConfigFieldKind.Number),
            new("Bullets per round", Firing, ConfigFieldKind.Integer, "Pellets a shot fires (a shotgun's several)"),
            new("Rounds per minute", Firing, ConfigFieldKind.Integer),
            new("Rounds per clip", Firing, ConfigFieldKind.Integer),
            new("Rounds per burst", Firing, ConfigFieldKind.Integer, "The most a burst fires before the trigger is let go"),
            new("Spread, least", Recoil, ConfigFieldKind.Number),
            new("Spread, most", Recoil, ConfigFieldKind.Number),
            new("Recoil X, fixed", Recoil, ConfigFieldKind.Number),
            new("Recoil Z, fixed", Recoil, ConfigFieldKind.Number),
            new("Recoil X, random", Recoil, ConfigFieldKind.Number),
            new("Recoil Z, random", Recoil, ConfigFieldKind.Number),
            new("Type of weapon", Display, ConfigFieldKind.TextKey, "The text key of its kind, as the briefing shows it"),
            new("Effective range", Ballistics, ConfigFieldKind.Number),
            new("Users", Display, ConfigFieldKind.TextKey, "The text key of who uses it"),
            new("Weapon length", Display, ConfigFieldKind.Integer),
            new("Barrel length", Display, ConfigFieldKind.Integer),
            new("Gun model", Models, ConfigFieldKind.Text),
            new("Casing model", Models, ConfigFieldKind.Text, "The spent case it ejects"),
            new("Stand", Animations, ConfigFieldKind.Integer),
            new("Move", Animations, ConfigFieldKind.Integer),
            new("Fire 1", Animations, ConfigFieldKind.Integer),
            new("Fire 2", Animations, ConfigFieldKind.Integer),
            new("Fire 3", Animations, ConfigFieldKind.Integer),
            new("Reload", Animations, ConfigFieldKind.Integer),
            new("Upper body, stand", Animations, ConfigFieldKind.Integer),
            new("Upper body, walk", Animations, ConfigFieldKind.Integer),
            new("Upper body, crouch", Animations, ConfigFieldKind.Integer),
            new("Upper body, crouch run", Animations, ConfigFieldKind.Integer),
            new("Upper body, run", Animations, ConfigFieldKind.Integer),
            new("Upper body, fire", Animations, ConfigFieldKind.Integer),
            new("Upper body, reload", Animations, ConfigFieldKind.Integer),
            new("Fire sound", Sounds, ConfigFieldKind.Text),
            new("Fire loop end sound", Sounds, ConfigFieldKind.Text),
            new("Heard by guards within", Ai, ConfigFieldKind.Expression, "How far a guard hears it fire, in world units"),
            new("Projectile task", Firing, ConfigFieldKind.Expression, "The task each shot is, followed by its own values"),
        ]),
        ["DefineAmmoType"] = new("DefineAmmoType", "Ammo",
        [
            new("Ammo id", Identity, ConfigFieldKind.Integer, "The number scripts and the game know the ammunition by; unique"),
            new("Script id", Identity, ConfigFieldKind.Text, "The constant scripts name it with (AMMO_ID_762)"),
            new("Name", Display, ConfigFieldKind.TextKey),
            new("Description", Display, ConfigFieldKind.TextKey),
            new("Calibre", Ballistics, ConfigFieldKind.Integer, "The weapons that take it name this calibre"),
            new("Mass (g)", Ballistics, ConfigFieldKind.Number),
            new("Near colour, red", Tracer, ConfigFieldKind.Integer),
            new("Near colour, green", Tracer, ConfigFieldKind.Integer),
            new("Near colour, blue", Tracer, ConfigFieldKind.Integer),
            new("Far colour, red", Tracer, ConfigFieldKind.Integer),
            new("Far colour, green", Tracer, ConfigFieldKind.Integer),
            new("Far colour, blue", Tracer, ConfigFieldKind.Integer),
            new("Draws a tracer", Tracer, ConfigFieldKind.Integer, "1 to draw a tracer, 0 not to"),
            new("Pickup model", Models, ConfigFieldKind.Text),
            new("Pickup sound", Sounds, ConfigFieldKind.Text),
        ]),
    };

    /// <summary>Gets a call's schema, or null for one without.</summary>
    /// <param name="native">The call's name.</param>
    /// <returns>The schema.</returns>
    public static ConfigSchema? For(string native) => ByNative.GetValueOrDefault(native);

    /// <summary>Gets the field for a value of a call, a numbered one past its schema or for a call with none.</summary>
    /// <param name="schema">The call's schema, or null.</param>
    /// <param name="index">The value's position.</param>
    /// <returns>The field.</returns>
    public static ConfigField FieldAt(ConfigSchema? schema, int index) =>
        schema is not null && index < schema.Fields.Count
            ? schema.Fields[index]
            : new ConfigField(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Value {index + 1}"), schema is null ? "Values" : "More values", ConfigFieldKind.Expression);
}
