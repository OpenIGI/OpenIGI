// The register model every ported I.G.I. 2 calculation runs on: the client's FPU computes at 24-bit precision,
// rounding toward zero (see X87Single). One alias, so the whole title's arithmetic follows a single switch.
global using Fpu = OpenIGI.Core.Numerics.X87Single;
