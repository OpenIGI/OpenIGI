using OpenIGI.Core.Diagnostics;
using Silk.NET.OpenAL;

namespace OpenIGI.Audio.OpenAL;

/// <content>
/// The time warp: every voice slowed and muffled at once, through OpenAL's pitch and, where the driver has the effects extension
/// (<c>ALC_EXT_EFX</c>), a low-pass filter on each source's direct path.
/// </content>
/// <remarks>
/// The extension's entry points are looked up by name rather than through a binding of their own: three calls and a handful of
/// constants, from the OpenAL 1.1 effects extension's specification. A driver without it still slows; it only does not muffle.
/// </remarks>
public sealed partial class OpenAlAudioDevice
{
    // ALC_EXT_EFX's constants: the filter-type property and its low-pass value, the low-pass's two gains, and the source's
    // direct-path filter.
    private const int FilterTypeProperty = 0x8001;
    private const int LowPassFilter = 0x0001;
    private const int LowPassGain = 0x0001;
    private const int LowPassGainHighFrequencies = 0x0002;
    private const int DirectFilter = 0x20005;

    /// <summary>How small a change to the warp is not worth re-applying every voice for.</summary>
    private const float WarpStep = 0.01f;

    private float _warpPitch = 1f;
    private float _warpMuffle;
    private uint _warpFilter;
    private bool _efxLooked;
    private unsafe delegate* unmanaged[Cdecl]<int, uint*, void> _genFilters;
    private unsafe delegate* unmanaged[Cdecl]<int, uint*, void> _deleteFilters;
    private unsafe delegate* unmanaged[Cdecl]<uint, int, int, void> _filteri;
    private unsafe delegate* unmanaged[Cdecl]<uint, int, float, void> _filterf;

    /// <inheritdoc/>
    /// <remarks>
    /// Muffling is a low-pass on each source's direct path: the high frequencies fall to a tenth and the whole a little, so the
    /// world sounds heard through glass. Every sounding voice is re-applied when the warp moves, as a bus's gain is.
    /// </remarks>
    public unsafe void SetTimeWarp(float pitch, float muffle)
    {
        pitch = Math.Clamp(pitch, 0.5f, 1f);
        muffle = Math.Clamp(muffle, 0f, 1f);
        if (MathF.Abs(pitch - _warpPitch) < WarpStep && MathF.Abs(muffle - _warpMuffle) < WarpStep
            && (pitch < 1f || _warpPitch == 1f) && (muffle > 0f || _warpMuffle == 0f))
        {
            return;
        }

        lock (_gate)
        {
            if (_al is null)
            {
                return;
            }

            _warpPitch = pitch;
            _warpMuffle = muffle;
            if (EnsureWarpFilter())
            {
                _filterf(_warpFilter, LowPassGain, 1f - (0.25f * muffle));
                _filterf(_warpFilter, LowPassGainHighFrequencies, MathF.Max(0.05f, 1f - (0.92f * muffle)));
            }

            foreach (OpenAlVoice voice in _pool.Voices)
            {
                if (voice.IsActive)
                {
                    ApplyParameters(voice, voice.Parameters);
                }
            }
        }
    }

    // A voice's pitch under the warp.
    private float WarpedPitch(float pitch) => pitch * _warpPitch;

    // Puts the warp's filter on a source's direct path, or takes it off when there is nothing to muffle.
    private void ApplyWarpFilter(AL al, uint source)
    {
        if (_warpFilter != 0)
        {
            al.SetSourceProperty(source, (SourceInteger)DirectFilter, _warpMuffle > 0f ? (int)_warpFilter : 0);
        }
    }

    // Looks the extension up once, and makes the one low-pass filter every source shares.
    private unsafe bool EnsureWarpFilter()
    {
        if (_warpFilter != 0)
        {
            return true;
        }

        if (_efxLooked || _al is not { } al || _alc is not { } alc || _device is null)
        {
            return false;
        }

        _efxLooked = true;
        if (!alc.IsExtensionPresent(_device, "ALC_EXT_EFX"))
        {
            _logger.Information(LogCategory, "No OpenAL effects extension: the time warp slows sounds without muffling them.");
            return false;
        }

        _genFilters = (delegate* unmanaged[Cdecl]<int, uint*, void>)al.GetProcAddress("alGenFilters");
        _deleteFilters = (delegate* unmanaged[Cdecl]<int, uint*, void>)al.GetProcAddress("alDeleteFilters");
        _filteri = (delegate* unmanaged[Cdecl]<uint, int, int, void>)al.GetProcAddress("alFilteri");
        _filterf = (delegate* unmanaged[Cdecl]<uint, int, float, void>)al.GetProcAddress("alFilterf");
        if (_genFilters == null || _deleteFilters == null || _filteri == null || _filterf == null)
        {
            return false;
        }

        uint filter = 0;
        _genFilters(1, &filter);
        if (filter == 0)
        {
            return false;
        }

        _filteri(filter, FilterTypeProperty, LowPassFilter);
        _warpFilter = filter;
        return true;
    }

    // Lets the filter go as the device closes.
    private unsafe void ReleaseWarpFilter()
    {
        if (_warpFilter != 0 && _deleteFilters != null)
        {
            uint filter = _warpFilter;
            _deleteFilters(1, &filter);
        }

        _warpFilter = 0;
    }
}
