# Added sound assets

Sounds that belong to the remaster rather than to the retail data. The retail bank stays exactly what
the game files say; everything here reaches the game through `SoundSystem.RegisterExternal` or is
loaded directly by the system that owns it.

Every file here is **uncompressed 16-bit PCM WAV**. `WavPcm` accepts nothing else on purpose: an added
asset can meet that bar by construction, so anything else is a preparation mistake worth refusing
loudly rather than decoding generously.

## `bullet-impact.wav`

Mono. The port's own impact sound, played alongside the material table's own six. Mono because only a
mono source is positioned in 3D.

## `rain-loop.wav`

Mono, 48 kHz, 20.0 s, ~1.9 MB. The bed `RainAmbience` builds the whole weather mix from — six taps
read it at different offsets and filter it differently, so this one file is the rain outside, the rain
through a wall, the rain through a doorway and the rain on a roof.

**Mono is a requirement, not a preference.** Only a mono source is placed in three dimensions; a
stereo buffer plays flat on the listener's head, which is the one thing the system exists to avoid.
The stereo width comes back from the six emitters, not from the file.

### Provenance

Source: `liecio-calming-rain-257596.mp3`, supplied for this purpose.

> **The licence for this recording has not been recorded here.** This repository is GPL-3.0, and a
> shipped audio asset needs its licence and attribution stated before release. Fill this section in —
> or replace the file with a recording whose terms are known — before the asset ships.

### How it was prepared

The game never decodes MP3, and nothing in the repository depends on an MP3 decoder: the conversion
below is a one-time step whose only output is the committed WAV.

1. **Decode to WAV.** Any decoder will do; the original used VLC, which is already cross-platform:

   ```sh
   vlc -I dummy --play-and-exit rain.mp3 \
     ':sout=#transcode{acodec=s16l,samplerate=48000,channels=2}:standard{access=file,mux=wav,dst=rain-raw.wav}'
   ```

2. **Choose the loop window by measurement, not by ear.** The 108 s recording is not uniform: seconds
   33–34 and 95–105 carry heavy low-frequency energy (thunder or wind) while the high band stays flat,
   and a loop containing either repeats that event every twenty seconds, which is the single most
   audible way a bed gives itself away. Scoring every candidate window by the spread of its broadband,
   above-2 kHz and below-150 Hz levels — weighting the rumble double — picks **52.75 s – 72.75 s**.

3. **High-pass at 45 Hz**, two one-pole sections run forward then backward so the filter adds no phase
   shift. A bed that will be low-passed and layered six times has no use for subsonic rumble, and it
   competes with gunfire for headroom.

4. **Close the loop with a 1.5 s equal-power crossfade**, blending the window's tail against the
   material immediately *before* the window's start. Equal power (sin/cos) rather than linear, because
   the two sides of the seam are uncorrelated noise and a linear fade dips about 3 dB in the middle of
   every seam. Taking the crossfade material from before the start is what makes the wrap itself
   continuous: the last sample and the first are adjacent samples of the original recording.

5. **Normalise** to −20 dBFS RMS with a −3 dBFS peak ceiling. The peak binds, so the file sits at
   −23.7 dBFS RMS / −3.0 dBFS peak. The runtime decides loudness; the asset only needs headroom.

### How it was verified

Level across the wrap is flat to within 1 dB in quarter-second blocks — that is ordinary rain
variance, not a seam. More usefully, the step between the last sample and the first is **0.0055**,
which sits at the **19th percentile** of ordinary adjacent-sample steps in the body (median 0.021), and
the slope energy in 64-sample blocks across the seam (−30.5 dB) is indistinguishable from the
whole-loop mean (−28.3 dB). There is no discontinuity to hear.
