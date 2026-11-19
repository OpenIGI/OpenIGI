using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player.Aim;

/// <summary>One weapon's live changes to its sights, laid over its table entry while tuning (<see cref="AdsTuningCVars.Tune"/>).</summary>
/// <param name="Right">Metres the weapon is moved right in the eye's frame.</param>
/// <param name="Up">Metres it is moved up.</param>
/// <param name="Relief">Metres added to the eye relief: positive pushes the weapon away.</param>
/// <param name="ViewModelTan">Added to the view model's tangent on the sights: positive draws the weapon smaller.</param>
/// <param name="Zoom">Added to the magnification.</param>
/// <param name="Tilt">Degrees the muzzle is tipped up.</param>
public readonly record struct AdsTune(float Right, float Up, float Relief, float ViewModelTan, float Zoom, float Tilt)
{
    /// <summary>Gets a value indicating whether the weapon is changed at all.</summary>
    public bool Changes => this != default;
}

/// <summary>
/// The developer's live tuning of each weapon's sights: kept per weapon while the game runs, nudged from the numpad, and
/// written out as aim-table entries (<see cref="AdsProfileTable"/>) to copy into the table. <b>A developer's tool</b>: it
/// does nothing unless <see cref="AdsTuningCVars.Tune"/> is on.
/// </summary>
/// <remarks>
/// The changes are in the eye's frame, as seen through the sights -- right, up, nearer and farther -- and are turned into the
/// weapon bone's frame by moving the sights the other way: a weapon moved up is one whose sights went down by as much.
/// </remarks>
public static class AdsLiveTuning
{
    private static readonly Dictionary<string, AdsTune> Tunes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>Gets the script id of the weapon the player held at its last tick, or empty.</summary>
    public static string Held { get; private set; } = string.Empty;

    /// <summary>Gets the profile the held weapon played with at the last tick, tuning laid over it, or <see langword="null"/>.</summary>
    public static AdsProfile? HeldProfile { get; private set; }

    /// <summary>Gets the table entry the held weapon's tuning was laid over, or <see langword="null"/>.</summary>
    public static AdsProfile? HeldBase { get; private set; }

    /// <summary>Lays a weapon's tuning over its profile, and notes it as the one held.</summary>
    /// <param name="profile">The weapon's table entry.</param>
    /// <returns>The tuned profile, or <paramref name="profile"/> itself with tuning off.</returns>
    public static AdsProfile Apply(AdsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!AdsTuningCVars.Tune.Value)
        {
            return profile;
        }

        AdsProfile tuned = Tuned(profile, Of(profile.ScriptId));
        lock (Gate)
        {
            Held = profile.ScriptId;
            HeldBase = profile;
            HeldProfile = tuned;
        }

        return tuned;
    }

    /// <summary>Gets a weapon's tuning.</summary>
    /// <param name="scriptId">The weapon's script id.</param>
    /// <returns>Its tuning, nothing for one never tuned.</returns>
    public static AdsTune Of(string scriptId)
    {
        lock (Gate)
        {
            return Tunes.TryGetValue(scriptId, out AdsTune tune) ? tune : default;
        }
    }

    /// <summary>Changes the held weapon's tuning.</summary>
    /// <param name="change">What to add to it.</param>
    public static void Nudge(AdsTune change)
    {
        lock (Gate)
        {
            if (Held.Length == 0)
            {
                return;
            }

            AdsTune now = Tunes.TryGetValue(Held, out AdsTune was) ? was : default;
            Tunes[Held] = new AdsTune(
                now.Right + change.Right,
                now.Up + change.Up,
                now.Relief + change.Relief,
                now.ViewModelTan + change.ViewModelTan,
                now.Zoom + change.Zoom,
                now.Tilt + change.Tilt);
        }
    }

    /// <summary>Puts the held weapon back to its table entry.</summary>
    public static void ResetHeld()
    {
        lock (Gate)
        {
            Tunes.Remove(Held);
        }
    }

    /// <summary>The lines the tuning panel shows for the held weapon.</summary>
    /// <returns>The lines, or none when nothing aimable is held.</returns>
    public static IReadOnlyList<string> Describe()
    {
        AdsProfile? tuned;
        string held;
        lock (Gate)
        {
            tuned = HeldProfile;
            held = Held;
        }

        if (tuned is null)
        {
            return ["ADS TUNING -- hold an aimable weapon"];
        }

        AdsTune tune = Of(held);
        static string F(float value) => value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        return
        [
            $"ADS TUNING  {held}",
            $"move  right {F(tune.Right)}  up {F(tune.Up)}  away {F(tune.Relief)}  tilt {F(tune.Tilt)} deg",
            FormattableString.Invariant($"now   relief {tuned.EyeReliefMetres:0.000} m  arms tan {tuned.ViewModelTanX:0.000}  zoom {tuned.Magnification:0.00}x"),
            "Num 4/6 left/right  8/2 up/down  9/3 away/near  7/1 smaller/bigger",
            "Num +/- zoom  * / tilt  0 reset  Enter save   (Shift = fine)",
        ];
    }

    /// <summary>Writes every tuned weapon as an aim-table entry, as the table spells one, into a file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="table">The table the tuned weapons' entries come from.</param>
    /// <returns>How many weapons were written.</returns>
    public static int Save(string path, AdsProfileTable table)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(table);
        KeyValuePair<string, AdsTune>[] tuned;
        lock (Gate)
        {
            tuned = [.. Tunes.Where(static entry => entry.Value.Changes).OrderBy(static entry => entry.Key, StringComparer.Ordinal)];
        }

        StringBuilder text = new();
        text.AppendLine("{");
        text.AppendLine("  \"weapons\": {");
        int written = 0;
        foreach ((string id, AdsTune tune) in tuned)
        {
            if (!table.TryGet(id, out AdsProfile entry))
            {
                continue;
            }

            AdsProfile now = Tuned(entry, tune);
            static string V(Vector3f v) => FormattableString.Invariant($"[{v.X:0.0000}, {v.Y:0.000}, {v.Z:0.0000}]");
            text.Append(CultureInfo.InvariantCulture, $"    \"{id}\": {{ \"rear\": {V(now.RearSight)}, \"front\": {V(now.FrontSight)}, ");
            text.Append(CultureInfo.InvariantCulture, $"\"relief\": {now.EyeReliefMetres:0.000}, \"viewModelTan\": {now.ViewModelTanX:0.000}, \"zoom\": {now.Magnification:0.00} }}");
            text.AppendLine(++written < tuned.Length ? "," : string.Empty);
        }

        text.AppendLine("  }");
        text.AppendLine("}");
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, text.ToString());
        return written;
    }

    // The weapon moved by its tuning: moving it up, right or tipped is moving its sights the other way in its bone's frame.
    private static AdsProfile Tuned(AdsProfile profile, AdsTune tune)
    {
        if (!tune.Changes)
        {
            return profile;
        }

        Vector3f shift = new(-tune.Right, 0f, -tune.Up);
        float length = MathF.Max(0.05f, profile.FrontSight.Y - profile.RearSight.Y);
        Vector3f tilt = new(0f, 0f, -MathF.Tan(tune.Tilt * MathF.PI / 180f) * length);
        return profile with
        {
            RearSight = profile.RearSight + shift,
            FrontSight = profile.FrontSight + shift + tilt,
            EyeReliefMetres = MathF.Max(0.06f, profile.EyeReliefMetres + tune.Relief),
            ViewModelTanX = MathF.Max(0.2f, profile.ViewModelTanX + tune.ViewModelTan),
            Magnification = MathF.Max(1f, profile.Magnification + tune.Zoom),
        };
    }
}
