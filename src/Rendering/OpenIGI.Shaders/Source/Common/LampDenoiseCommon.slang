// What the two lamp-shadow denoising passes agree on.
//
// Both passes have to decide whether two depth samples lie on the same surface. Doing it with a
// fixed distance in metres fails at range: the depth buffer's precision falls away with distance, so a
// threshold tight enough to separate a person from the wall behind them up close would split one flat
// floor into many surfaces far away. The tolerance is therefore a fraction of how far away the point is.

#ifndef OPENIGI_LAMP_DENOISE_COMMON_INCLUDED
#define OPENIGI_LAMP_DENOISE_COMMON_INCLUDED

#include "ViewDepth.slang"

// How far apart two view depths may be, as a fraction of the view depth, and still count as one surface.
static const float kLampSameSurfaceFraction = 0.03;

#endif
