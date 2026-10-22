using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Titles;
using OpenIGI.Rendering;
using OpenIGI.Shaders;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The running title's mod passes, their modules registered, and the ones the device runs now.
    private readonly List<(TitlePostPass Pass, ShaderModuleId Module)> _modPasses = [];
    private readonly List<PostPassModule> _modPassesRunning = [];

    /// <summary>Registers the running title's mod passes' compiled modules, for the device to build as it runs them.</summary>
    /// <remarks>
    /// Once a title: each module is read from its mod's <c>shaders/</c> folder and given an id by the shader catalogue. A
    /// module that is not there or cannot be read is left out with the reason in the log, and the game runs without it.
    /// </remarks>
    private void LoadModPostPasses()
    {
        _modPasses.Clear();
        _modPassesRunning.Clear();
        _device.SetPostPasses([]);
        foreach (TitlePostPass pass in _titleRuntime.PostPasses)
        {
            try
            {
                ShaderModuleId module = ShaderCatalog.Register(ExternalShaderModule.Read(pass.ShadersDirectory, pass.Module));
                _modPasses.Add((pass, module));
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _logger.Warning("Mods", $"The mod pass '{pass.Name}' is left out: {error.Message}");
            }
        }

        if (_modPasses.Count > 0)
        {
            _logger.Information("Mods", $"{_modPasses.Count} mod pass(es) registered.");
        }
    }

    /// <summary>Tells the device which mod passes run this frame, and what their settings give them.</summary>
    private void UpdateModPostPasses()
    {
        if (_modPasses.Count == 0)
        {
            return;
        }

        List<PostPassModule> running = [];
        foreach ((TitlePostPass pass, ShaderModuleId module) in _modPasses)
        {
            if (pass.Toggle is null || SettingValue(pass.Toggle) != 0f)
            {
                running.Add(new PostPassModule(pass.Name, module, pass.Stage));
            }
        }

        if (!running.SequenceEqual(_modPassesRunning))
        {
            _modPassesRunning.Clear();
            _modPassesRunning.AddRange(running);
            _device.SetPostPasses(running);
        }

        Span<float> values = stackalloc float[16];
        foreach ((TitlePostPass pass, _) in _modPasses)
        {
            values.Clear();
            for (int i = 0; i < pass.Parameters.Count && i < values.Length; i++)
            {
                values[i] = SettingValue(pass.Parameters[i]);
            }

            _device.SetPostPassValues(pass.Name, values);
        }
    }

    // A setting's value as a number: a number as it reads, a switch as one or nought, anything else (or none) as nought.
    private static float SettingValue(string name)
    {
        if (!CVarRegistry.Default.TryGet(name, out ICVar? cvar) || cvar is null)
        {
            return 0f;
        }

        string text = cvar.ValueText;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ? number
            : bool.TryParse(text, out bool on) ? (on ? 1f : 0f)
            : 0f;
    }
}
