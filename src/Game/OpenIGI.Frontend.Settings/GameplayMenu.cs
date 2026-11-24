namespace OpenIGI.Frontend.Settings;

/// <summary>What the Gameplay page offers: how the game plays, how the dead fall, how aiming and the camera work, the world's weather and time, the effects, and the cheats.</summary>
/// <remarks>
/// Written out rather than gathered from the <c>g_</c> variables by name, which put the developers' own switches and a
/// physics solver's substeps in front of players under captions made from variable names. A new gameplay variable has to
/// be given a row here or a line in <see cref="ConsoleOnly"/>; <c>SettingsMenuCoverageTests</c> fails until it is.
/// </remarks>
public static class GameplayMenu
{
    /// <summary>The variable the cheat rows follow: none of them does anything until it is on.</summary>
    public const string CheatsEnabled = "g_cheats_enabled";

    /// <summary>Gets the page's panels, in the order they are shown.</summary>
    public static IReadOnlyList<SettingsPanel> Panels { get; } =
    [
        new SettingsPanel(
            "General",
            [],
            [
                new SettingRow(
                    "g_igi1_saves",
                    "Mid-Mission Saves",
                    "Lets you save and load in the middle of a Project I.G.I. mission, from the pause menu and the quick save "
                        + "keys, which the original never allowed. Off plays each mission through in one go. I.G.I. 2 keeps its own saves."),
                new SettingRow(
                    "g_igi2_movement",
                    "I.G.I. 2 Movement",
                    "How you move and look in I.G.I. 2. Project I.G.I. gives the sequel's soldier the first game's walk, run, "
                        + "strafe, crouch and jump, and its one-to-one mouse look. Your weapons, aim and health stay I.G.I. 2's.",
                    Choices: new Dictionary<string, string>
                    {
                        ["igi2"] = "I.G.I. 2",
                        ["igi1"] = "Project I.G.I.",
                    })
                {
                    // The first game's movement comes as the first game had it: no weapon weight, which can be turned on after.
                    OnChosen = new RowEffect("igi1", "g_igi2_igi1_weight", "0"),
                },
                new SettingRow(
                    "g_igi2_igi1_weight",
                    "Project I.G.I. Weapon Weight",
                    "On Project I.G.I. movement in I.G.I. 2, the weapons you carry slow you as they do in I.G.I. 2: the one in hand in "
                        + "full, the rest by half, never below half speed. Off moves as freely as the first game. Choosing "
                        + "Project I.G.I. movement turns it off."),
                new SettingRow(
                    "g_igi2_loadout",
                    "I.G.I. 2 Loadout",
                    "What you carry in I.G.I. 2. Tactical carries a long gun, a submachine gun and a launcher side by side, so "
                        + "taking a rocket launcher keeps your rifle. Original carries one primary weapon and swaps it, as the original "
                        + "does. Every gun you carry still slows you.",
                    Choices: new Dictionary<string, string>
                    {
                        ["original"] = "Original",
                        ["tactical"] = "Tactical",
                    }),
            ]),

        new SettingsPanel(
            "Realism",
            [],
            [
                new SettingRow(
                    "g_ragdoll",
                    "Ragdoll Deaths",
                    "The dead fall as real bodies, thrown by the round that killed them, instead of playing the original's "
                        + "canned death animations."),
                new SettingRow(
                    "g_realistic_drops",
                    "Realistic Weapon Drops",
                    "A dropped weapon falls, tumbles and slides as a real object. Off lays it flat on the ground beneath, as "
                        + "the original does."),
                new SettingRow(
                    "g_bodies_stay",
                    "Bodies Stay",
                    "Dead soldiers stay where they fell for the rest of the mission. Off takes a body away once you are not "
                        + "looking at it, as the original does."),
                new SettingRow(
                    "g_steady_turns",
                    "Steady Turns",
                    "In I.G.I. 2, you keep running the way you face as you turn. Off slides you sideways out of a turn at a run, "
                        + "as the original does."),
                new SettingRow(
                    "g_impact_sounds",
                    "Impact Sounds",
                    "Each round that lands makes the sound of the material it hits: metal rings, wood thuds, dirt puffs."),
                new SettingRow(
                    "g_ragdoll_force",
                    "Ragdoll Knock-Back",
                    "How hard a round throws the body it kills. 1 is the round's real momentum; 0 lets them simply fall.",
                    Step: 0.25f) { Advanced = true, ShownWhen = new("g_ragdoll") },
                new SettingRow(
                    "g_ragdoll_gravity",
                    "Ragdoll Gravity",
                    "How fast a body falls, as a share of real gravity. 1 is real; lower is the slow fall of a film.") { Advanced = true, ShownWhen = new("g_ragdoll") },
                new SettingRow(
                    "g_ragdoll_muscle",
                    "Ragdoll Muscle Tone",
                    "How many seconds a body keeps some strength before it goes limp. 0 is limp at once.") { Advanced = true, ShownWhen = new("g_ragdoll") },
                new SettingRow(
                    "g_ragdoll_muscle_clip",
                    "Death Animation First",
                    "A body plays the original's death animation first and goes limp after it, so falls look authored rather "
                        + "than physical.") { Advanced = true, ShownWhen = new("g_ragdoll") },
                new SettingRow(
                    "g_ragdoll_substeps",
                    "Ragdoll Precision",
                    "How finely the bodies are simulated. Higher holds their joints stiffer and costs a little more.") { Advanced = true, ShownWhen = new("g_ragdoll") },
            ]),

        new SettingsPanel(
            "Aiming",
            [],
            [
                new SettingRow(
                    "g_modern_aim",
                    "Modern Aiming",
                    "The right mouse button aims down the weapon's sights, the lean keys lean with it raised, and it braces on "
                        + "ledges and tables. Off plays the original's sight, zoom and peek."),
                new SettingRow(
                    "g_ads_toggle",
                    "Toggle Aim",
                    "One press raises the sights and the next lowers them. Off aims for as long as the button is held.") { ShownWhen = new("g_modern_aim") },
                new SettingRow(
                    "g_ads_sensitivity",
                    "Aim Sensitivity",
                    "How fast the view turns through the sights. 1 keeps the same feel as the hip at every zoom; lower is "
                        + "steadier.",
                    Step: 0.05f) { ShownWhen = new("g_modern_aim") },
                new SettingRow(
                    "g_ads_zoom",
                    "Aim Zoom",
                    "How much the view narrows through the sights: 0 not at all, 1 each weapon's own magnification.",
                    Step: 0.05f) { ShownWhen = new("g_modern_aim") },
                new SettingRow(
                    "g_auto_mount",
                    "Brace Automatically",
                    "Aiming at a ledge, table or wall edge rests the weapon on it. Off braces only on the mount key.") { ShownWhen = new("g_modern_aim") },
            ]),

        new SettingsPanel(
            "Camera",
            [],
            [
                new SettingRow(
                    "g_thirdperson",
                    "Third Person",
                    "Shows the player from behind instead of through their own eyes. Lasts the session; the third-person key "
                        + "switches it while you play."),
                new SettingRow(
                    "g_thirdperson_distance",
                    "Camera Distance",
                    "How far behind the player the camera sits, in metres.",
                    Step: 0.25f) { Advanced = true },
                new SettingRow(
                    "g_thirdperson_shoulder",
                    "Camera Shoulder Offset",
                    "How far to the right of the player the camera sits, in metres. 0 centres it behind them.",
                    Step: 0.05f) { Advanced = true },
                new SettingRow(
                    "g_thirdperson_height",
                    "Camera Height",
                    "How far above the player's eyes the camera sits, in metres.",
                    Step: 0.05f) { Advanced = true },
                new SettingRow(
                    "g_thirdperson_blend",
                    "Camera Switch Time",
                    "How many seconds the camera takes to move between the first- and third-person views.",
                    Step: 0.05f) { Advanced = true },
            ]),

        new SettingsPanel(
            "World",
            [],
            [
                new SettingRow(
                    "r_weather_enabled",
                    "Weather",
                    "Draws the rain or snow the level authors gave each mission."),
                new SettingRow(
                    "r_storm",
                    "Storm Effects",
                    "Adds wind-driven streaks, splashes where the rain lands, and lightning to a level whose "
                        + "weather is a storm.") { ShownWhen = new("r_weather_enabled") },
                new SettingRow(
                    "r_day_night_cycle",
                    "Day/Night Cycle",
                    "Lets time pass while you play. A mission still begins at the hour it was written for, and "
                        + "the sun goes on from there, so a long mission can end under a different sky. Off, "
                        + "every mission stays at its own hour, the way the original's do."),
                new SettingRow(
                    "r_day_length",
                    "Day Length",
                    "How long a whole day takes, in minutes, once the cycle above is on. Forty is a sun you "
                        + "notice having moved; a minute or two is a sunrise you can stand and watch.") { ShownWhen = new("r_day_night_cycle") },
                new SettingRow(
                    "r_time_of_day",
                    "Time of Day",
                    "The hour the sun starts each mission at. -1 is each level's own authored hour, which is the "
                        + "light it was made for; the bracket keys move it while you play. Lasts the session."),
                new SettingRow(
                    "r_lamp_daylight",
                    "Lamps Follow Daylight",
                    "Puts the level's lamps on photocells: they go out by day and strike one by one as dusk "
                        + "falls, each on its own delay. Outdoors leaves lamps under a roof lit, as a building's "
                        + "are; Everywhere includes them; Off keeps every lamp burning, as in the original."),
            ]),

        new SettingsPanel(
            "Effects",
            [],
            [
                new SettingRow(
                    "r_impact_particles",
                    "Impact Effects",
                    "Sparks, dust and debris where rounds land, chosen to suit the surface that was hit."),
                new SettingRow(
                    "r_bulletmarks",
                    "Bullet Marks",
                    "Leaves the marks rounds make on walls and floors."),
                new SettingRow(
                    "r_blast_quality",
                    "Bomb Blast",
                    "How search and destroy's bomb goes off. Original is the original's rocket explosion; Enhanced adds its fire and "
                        + "smoke, light, shake, and the props and walls it shatters; Full throws all of its fire and smoke and draws its "
                        + "after-blast sweeping over the world.",
                    new Dictionary<string, string>
                    {
                        ["0"] = "Original",
                        ["1"] = "Enhanced",
                        ["2"] = "Full",
                    }),
                new SettingRow(
                    "r_muzzle_flash",
                    "Muzzle Flash",
                    "How bright the flame at the barrel is. The original's is bright enough to swallow the first "
                        + "few metres of a tracer.") { Advanced = true },
                new SettingRow(
                    "r_damage_flash",
                    "Damage Flash",
                    "How strongly the screen washes red when you are hit.") { Advanced = true },
                new SettingRow(
                    "r_blast_shake",
                    "Blast Shake",
                    "How hard the bomb's blast shakes the view. The original's own explosion shake is unaffected.") { Advanced = true },
            ]),

        new SettingsPanel(
            "Cheats",
            [],
            [
                new SettingRow(
                    CheatsEnabled,
                    "Enable Cheats",
                    "Puts the cheats in force, and shows them here. Off, every one of them is ignored. Not saved: every launch "
                        + "starts with cheats off."),
                new SettingRow("g_cheat_infinite_health", "Infinite Health", "Nothing can hurt you.") { ShownWhen = new(CheatsEnabled) },
                new SettingRow("g_cheat_infinite_ammo", "Infinite Ammo", "Firing never uses up a round or a grenade.") { ShownWhen = new(CheatsEnabled) },
                new SettingRow("g_cheat_no_reload", "No Reload", "The magazine refills from your spare rounds as you fire, so you never stop to reload.") { ShownWhen = new(CheatsEnabled) },
                new SettingRow("g_cheat_no_recoil", "No Recoil", "Firing does not kick the view.") { ShownWhen = new(CheatsEnabled) },
                new SettingRow("g_cheat_unseen", "Unseen", "Guards and cameras do not see you. They still hear you.") { ShownWhen = new(CheatsEnabled) },
                new SettingRow("g_cheat_all_weapons", "Give All Weapons", "Carry every weapon the mission has.") { ShownWhen = new(CheatsEnabled) },
                new SettingRow("g_cheat_unlock_all_levels", "Unlock All Levels", "Every mission of the campaign can be chosen.") { ShownWhen = new(CheatsEnabled) },
                new SettingRow("g_cheat_speed", "Speed Multiplier", "How many times faster you move. 1 is the original's speed.", Step: 0.25f) { ShownWhen = new(CheatsEnabled) },
                new SettingRow("g_cheat_jump", "High Jump Multiplier", "How many times higher you jump. 1 is the original's jump.", Step: 0.25f) { ShownWhen = new(CheatsEnabled) },
                new SettingRow("g_cheat_health_scale", "Health Multiplier", "How many times the original's health you have.") { ShownWhen = new(CheatsEnabled) },
            ]),
    ];

    /// <summary>Gets every row on the page, in the order they are shown.</summary>
    public static IReadOnlyList<SettingRow> Rows { get; } = [.. Panels.SelectMany(panel => panel.Rows)];

    /// <summary>Gets the gameplay variables the page deliberately does not show, each with the reason.</summary>
    /// <remarks>
    /// Developer and hidden variables are never offered, so only the visible ones that are left out need a line here.
    /// Every one stays reachable from the console by name.
    /// </remarks>
    public static IReadOnlyList<string> ConsoleOnly { get; } =
    [
        // The sound a co-op partner going down makes, by its name in the game's sound table: a name a player cannot
        // choose without knowing the table.
        "hud_down_sound",

        // A movement to compare with the chosen one, for the session: a debug key steps it, and it is never saved.
        "g_igi2_movement_compare",

        // What the game was last asked to be. The Experience band across the top of the Gameplay and Interface pages is its
        // menu form, and reads the settings themselves rather than this.
        "g_preset",
    ];
}
